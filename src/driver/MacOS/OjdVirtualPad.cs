using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver.MacOS
{
    /// <summary>
    /// macOS 虚拟手柄：把 <see cref="PadReport"/> 桥接给 OpenJoystickDriver（OJD）的
    /// <c>ojd virtual feed</c> 子进程，由 OJD 负责把它发布成系统可见的手柄。
    ///
    /// 为什么不在本进程里直接建 HID 设备：macOS 上创建虚拟 HID 需要 Apple 授予的受限
    /// entitlement（com.apple.developer.hid.virtual.device），普通签名的程序拿不到。
    /// OJD 是持有该权限的已签名应用，本进程只走它公开的 feed 接口。
    ///
    /// 协议要点（见 OJD docs/sdk 与 virtual feed 说明）：
    /// - 每行一个 JSON 表示完整状态，未出现的 control 视为中性；
    /// - 摇杆 -1..1（Y 正向下），扳机 0..1；
    /// - 每帧至少播放 8ms；2 秒没有任何行，OJD 会关掉这条 feed；
    /// - OJD 把游戏的振动命令以 RumbleCommand 行打印到 stdout。
    /// </summary>
    internal sealed class OjdVirtualPad : IVirtualPad
    {
        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly Thread _reader;
        private readonly Thread _heartbeat;
        private readonly Thread _errorPump;
        private readonly object _gate = new();

        // 最近一次提交的整帧状态：心跳原样重发它，玩家静止时手柄也不会被 OJD 回收。
        private string _lastFrame;

        private bool _disposed;

        public VirtualPadType Type { get; }

        public event Action<byte, byte>? VibrationReceived;

        internal OjdVirtualPad(Process process, VirtualPadType type)
        {
            _process = process;
            _stdin = process.StandardInput;
            _stdin.AutoFlush = true;
            Type = type;
            _lastFrame = BuildFrame(PadReport.Neutral);

            _reader = new Thread(ReadRumbleLoop)
            {
                IsBackground = true,
                Name = "HanVGamepad.OJD.Rumble"
            };
            _reader.Start();

            _heartbeat = new Thread(HeartbeatLoop)
            {
                IsBackground = true,
                Name = "HanVGamepad.OJD.Heartbeat"
            };
            _heartbeat.Start();

            _errorPump = new Thread(DrainErrorLoop)
            {
                IsBackground = true,
                Name = "HanVGamepad.OJD.Stderr"
            };
            _errorPump.Start();
        }

        public void Submit(PadReport report)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _lastFrame = BuildFrame(report);
                    _stdin.WriteLine(_lastFrame);
                }
                catch
                {
                    // OJD 退出、管道断开或进程已回收：丢弃这一帧，不让异常穿出到输入线程。
                }
            }
        }

        public void ResetNeutral()
        {
            Submit(PadReport.Neutral);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                try
                {
                    // 关 stdin 等于告诉 OJD「输入结束」，它会移除这台虚拟手柄并退出。
                    _stdin.Close();
                }
                catch
                {
                    // 管道已断：进程多半已经退出。
                }
            }

            try
            {
                if (!_process.WaitForExit(3000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 进程回收失败不影响退出流程。
            }
            finally
            {
                _process.Dispose();
            }
        }

        /// <summary>
        /// OJD 的 feed 在「最后一帧播完后 2 秒没有新行」时会移除虚拟手柄。
        /// 玩家静止不动时没有输入变化，也就没有 Submit —— 必须定期原样重发当前状态，
        /// 否则手柄会在游戏里凭空消失（等下次输入时进程早已退出，只能重连）。
        /// 间隔取 1 秒，给 2 秒的超时留一半余量。
        /// </summary>
        private void HeartbeatLoop()
        {
            while (true)
            {
                Thread.Sleep(1000);

                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        _stdin.WriteLine(_lastFrame);
                    }
                    catch
                    {
                        // 进程已退出：心跳没有意义了。
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// stderr 必须一直读走：管道写满（约 64KB）会把 OJD 卡在写日志上，
        /// 表现成手柄突然不再响应。这里只负责排空；建进程失败时的错误由创建阶段取。
        /// </summary>
        private void DrainErrorLoop()
        {
            try
            {
                while (_process.StandardError.ReadLine() != null)
                {
                }
            }
            catch
            {
                // 进程退出、管道关闭：结束。
            }
        }

        /// <summary>把 OJD 打印的 RumbleCommand 转成统一振动事件（largeMotor, smallMotor）。</summary>
        private void ReadRumbleLoop()
        {
            try
            {
                string? line;
                while ((line = _process.StandardOutput.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] != '{')
                    {
                        continue;
                    }

                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(line);
                        JsonElement root = document.RootElement;

                        if (!root.TryGetProperty("kind", out JsonElement kind) ||
                            kind.ValueKind != JsonValueKind.String ||
                            !string.Equals(kind.GetString(), "RumbleCommand", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string type = root.TryGetProperty("type", out JsonElement typeElement)
                            ? typeElement.GetString() ?? string.Empty
                            : string.Empty;

                        if (string.Equals(type, "StopRumble", StringComparison.Ordinal))
                        {
                            VibrationReceived?.Invoke(0, 0);
                            continue;
                        }

                        if (!string.Equals(type, "SetRumble", StringComparison.Ordinal) ||
                            !root.TryGetProperty("intensities", out JsonElement intensities))
                        {
                            continue;
                        }

                        // OJD 用 0..65535；Xbox 的两路马达是 0..255。
                        VibrationReceived?.Invoke(
                            ReadIntensity(intensities, "lowFrequency"),
                            ReadIntensity(intensities, "highFrequency"));
                    }
                    catch (JsonException)
                    {
                        // 不是给我们的行：跳过。
                    }
                }
            }
            catch
            {
                // 进程退出、管道关闭：读循环结束即可。
            }
        }

        private static byte ReadIntensity(JsonElement intensities, string name)
        {
            if (!intensities.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out int raw))
            {
                return 0;
            }

            return (byte)Math.Clamp(raw * 255 / 65535, 0, 255);
        }

        /// <summary>
        /// 一帧完整状态。没按的键不出现在 buttons 里，轴的 Y 要取反：
        /// PadReport 沿用 XInput 语义（正向上），OJD 的 Frame 是正向下。
        /// </summary>
        private static string BuildFrame(PadReport report)
        {
            var builder = new StringBuilder(192);

            builder.Append("{\"buttons\":[");
            bool first = true;

            AppendButton(builder, ref first, report.Buttons, PadButtons.A, "South");
            AppendButton(builder, ref first, report.Buttons, PadButtons.B, "East");
            AppendButton(builder, ref first, report.Buttons, PadButtons.X, "West");
            AppendButton(builder, ref first, report.Buttons, PadButtons.Y, "North");
            AppendButton(builder, ref first, report.Buttons, PadButtons.LeftShoulder, "LeftShoulder");
            AppendButton(builder, ref first, report.Buttons, PadButtons.RightShoulder, "RightShoulder");
            AppendButton(builder, ref first, report.Buttons, PadButtons.Back, "Back");
            AppendButton(builder, ref first, report.Buttons, PadButtons.Start, "Start");
            AppendButton(builder, ref first, report.Buttons, PadButtons.LeftThumb, "LeftStickClick");
            AppendButton(builder, ref first, report.Buttons, PadButtons.RightThumb, "RightStickClick");
            AppendButton(builder, ref first, report.Buttons, PadButtons.Guide, "Guide");

            // Touchpad 按钮 OJD 的虚拟手柄不携带（文档明确列出），这里不发。

            builder.Append("],\"dpad\":[");
            first = true;

            AppendButton(builder, ref first, report.Buttons, PadButtons.DPadUp, "DpadUp");
            AppendButton(builder, ref first, report.Buttons, PadButtons.DPadDown, "DpadDown");
            AppendButton(builder, ref first, report.Buttons, PadButtons.DPadLeft, "DpadLeft");
            AppendButton(builder, ref first, report.Buttons, PadButtons.DPadRight, "DpadRight");

            builder.Append("],\"axes\":{\"LeftStickX\":");
            builder.Append(Axis(report.LeftX));
            builder.Append(",\"LeftStickY\":");
            builder.Append(Axis(-(int)report.LeftY));
            builder.Append(",\"RightStickX\":");
            builder.Append(Axis(report.RightX));
            builder.Append(",\"RightStickY\":");
            builder.Append(Axis(-(int)report.RightY));
            builder.Append(",\"LeftTrigger\":");
            builder.Append(Trigger(report.LeftTrigger));
            builder.Append(",\"RightTrigger\":");
            builder.Append(Trigger(report.RightTrigger));
            builder.Append("}}");

            return builder.ToString();
        }

        private static void AppendButton(StringBuilder builder, ref bool first, uint buttons, PadButtons flag, string name)
        {
            if ((buttons & (uint)flag) == 0)
            {
                return;
            }

            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(name).Append('"');
            first = false;
        }

        /// <summary>short 轴值（-32768..32767）→ -1..1；除 32768 是为了让两端都落在范围内。</summary>
        private static string Axis(int value)
        {
            double normalized = Math.Clamp(value / 32768.0, -1.0, 1.0);
            return normalized.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Trigger(byte value)
        {
            return (value / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
