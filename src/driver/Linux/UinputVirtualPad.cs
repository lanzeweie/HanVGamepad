using System.Runtime.InteropServices;
using HanVGamepad.Driver.Abstractions;
using static HanVGamepad.Driver.Linux.LinuxNative;

namespace HanVGamepad.Driver.Linux
{
    /// <summary>
    /// Linux uinput 虚拟手柄实例（Xbox 360 / xpad 兼容 evdev 格式）。
    /// DualShock4 请求在 Linux 端降级为 Xbox 360 映射（真实 DS4 仿真需要 HID 层，超出范围）。
    /// 每次 Submit 收集差异化的 evdev 事件批量写入 /dev/uinput 并以 SYN_REPORT 提交。
    /// </summary>
    internal sealed class UinputVirtualPad : IVirtualPad
    {
        private readonly int _fd;
        private readonly int _eventSize;
        private readonly object _gate = new();
        private bool _disposed;

        // 上一次已提交的状态：只写差异事件，降低 write 次数。
        private uint _lastButtons;
        private short _lastLeftX, _lastLeftY, _lastRightX, _lastRightY;
        private byte _lastLeftTrigger, _lastRightTrigger;

        public VirtualPadType Type => VirtualPadType.Xbox360;

        /// <summary>Linux uinput 端不注册 EV_FF，振动事件不触发。</summary>
        public event Action<byte, byte>? VibrationReceived
        {
            add { }
            remove { }
        }

        /// <summary>由 LinuxUinputBackend 在设备创建成功后调用，fd 所有权移交本实例。</summary>
        internal UinputVirtualPad(int fd)
        {
            _fd = fd;
            _eventSize = Marshal.SizeOf<InputEvent>();
        }

        public void Submit(PadReport report)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                uint buttons = report.Buttons;
                uint changed = buttons ^ _lastButtons;

                var events = new List<InputEvent>(21);

                // 按键差异：只发翻转的键位。
                AddButton(events, changed, buttons, PadButtons.A, BTN_SOUTH);
                AddButton(events, changed, buttons, PadButtons.B, BTN_EAST);
                AddButton(events, changed, buttons, PadButtons.X, BTN_NORTH);
                AddButton(events, changed, buttons, PadButtons.Y, BTN_WEST);
                AddButton(events, changed, buttons, PadButtons.LeftShoulder, BTN_TL);
                AddButton(events, changed, buttons, PadButtons.RightShoulder, BTN_TR);
                AddButton(events, changed, buttons, PadButtons.Back, BTN_SELECT);
                AddButton(events, changed, buttons, PadButtons.Start, BTN_START);
                AddButton(events, changed, buttons, PadButtons.LeftThumb, BTN_THUMBL);
                AddButton(events, changed, buttons, PadButtons.RightThumb, BTN_THUMBR);
                AddButton(events, changed, buttons, PadButtons.Guide, BTN_MODE);

                // DPad：xpad 格式用 HAT0X / HAT0Y（-1/0/1）。
                int hatX = (Has(buttons, PadButtons.DPadRight) ? 1 : 0) - (Has(buttons, PadButtons.DPadLeft) ? 1 : 0);
                int hatY = (Has(buttons, PadButtons.DPadDown) ? 1 : 0) - (Has(buttons, PadButtons.DPadUp) ? 1 : 0);
                int lastHatX = (Has(_lastButtons, PadButtons.DPadRight) ? 1 : 0) - (Has(_lastButtons, PadButtons.DPadLeft) ? 1 : 0);
                int lastHatY = (Has(_lastButtons, PadButtons.DPadDown) ? 1 : 0) - (Has(_lastButtons, PadButtons.DPadUp) ? 1 : 0);

                if (hatX != lastHatX)
                {
                    events.Add(InputEvent.Abs(ABS_HAT0X, hatX));
                }

                if (hatY != lastHatY)
                {
                    events.Add(InputEvent.Abs(ABS_HAT0Y, hatY));
                }

                // 摇杆差异。
                if (report.LeftX != _lastLeftX)
                {
                    events.Add(InputEvent.Abs(ABS_X, report.LeftX));
                }

                if (report.LeftY != _lastLeftY)
                {
                    events.Add(InputEvent.Abs(ABS_Y, report.LeftY));
                }

                if (report.RightX != _lastRightX)
                {
                    events.Add(InputEvent.Abs(ABS_RX, report.RightX));
                }

                if (report.RightY != _lastRightY)
                {
                    events.Add(InputEvent.Abs(ABS_RY, report.RightY));
                }

                // 扳机差异。
                if (report.LeftTrigger != _lastLeftTrigger)
                {
                    events.Add(InputEvent.Abs(ABS_Z, report.LeftTrigger));
                }

                if (report.RightTrigger != _lastRightTrigger)
                {
                    events.Add(InputEvent.Abs(ABS_RZ, report.RightTrigger));
                }

                if (events.Count == 0)
                {
                    return;
                }

                // SYN_REPORT 提交整帧。
                events.Add(InputEvent.Sync());

                WriteEvents(events);

                _lastButtons = buttons;
                _lastLeftX = report.LeftX;
                _lastLeftY = report.LeftY;
                _lastRightX = report.RightX;
                _lastRightY = report.RightY;
                _lastLeftTrigger = report.LeftTrigger;
                _lastRightTrigger = report.RightTrigger;
            }
        }

        private static void AddButton(List<InputEvent> events, uint changed, uint buttons, PadButtons flag, ushort code)
        {
            if ((changed & (uint)flag) == 0)
            {
                return;
            }

            events.Add(InputEvent.Key(code, (buttons & (uint)flag) != 0 ? 1 : 0));
        }

        private void WriteEvents(List<InputEvent> events)
        {
            byte[] buffer = new byte[events.Count * _eventSize];
            for (int i = 0; i < events.Count; i++)
            {
                InputEvent evt = events[i];
                MemoryMarshal.Write(buffer.AsSpan(i * _eventSize, _eventSize), in evt);
            }

            IntPtr written = Write(_fd, buffer, (nuint)buffer.Length);
            if (written == (IntPtr)(-1))
            {
                // 设备已被回收（ENODEV）或写入失败：丢弃这一帧，不让异常穿出到输入线程。
                _ = Marshal.GetLastPInvokeError();
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
                    // 销毁虚拟设备（close 之后 uinput 设备自动消失，但显式 destroy 更干净）。
                    IoctlValue(_fd, UI_DEV_DESTROY, 0);
                    Close(_fd);
                }
                catch
                {
                    // 回收失败不影响进程退出。
                }
            }
        }

        private static bool Has(uint buttons, PadButtons flag) => (buttons & (uint)flag) != 0;
    }
}
