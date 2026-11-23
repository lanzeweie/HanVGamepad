using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver.Windows
{
    /// <summary>
    /// ViGEmBus 虚拟手柄实例。包装 Nefarius 的 IXbox360Controller / IDualShock4Controller，
    /// 把平台无关的 PadReport 映射成 ViGEm 的按钮 / 轴 / 扳机调用并整包提交。
    /// </summary>
    internal sealed class ViGEmVirtualPad : IVirtualPad
    {
        private readonly IVirtualGamepad _device;
        private readonly Xbox360FeedbackReceivedEventHandler? _x360FeedbackHandler;
        private readonly DualShock4FeedbackReceivedEventHandler? _ds4FeedbackHandler;
        private readonly object _gate = new();
        private bool _disposed;

        public VirtualPadType Type { get; }

        public event Action<byte, byte>? VibrationReceived;

        public ViGEmVirtualPad(IVirtualGamepad device, VirtualPadType type)
        {
            _device = device;
            Type = type;

            // 两种控制器的 FeedbackReceived 是各自专用的委托签名：分别订阅后转发到统一事件。
            // 回调不捕获外部状态：设备可能被多个连接先后复用，由 GamepadManager 按槽位当前 Owner 转发。
            if (device is IXbox360Controller x360)
            {
                _x360FeedbackHandler = (_, args) => VibrationReceived?.Invoke(args.LargeMotor, args.SmallMotor);
                x360.FeedbackReceived += _x360FeedbackHandler;
            }
            else if (device is IDualShock4Controller ds4)
            {
                _ds4FeedbackHandler = (_, args) => VibrationReceived?.Invoke(args.LargeMotor, args.SmallMotor);
                ds4.FeedbackReceived += _ds4FeedbackHandler;
            }
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
                    if (_device is IXbox360Controller x360)
                    {
                        SubmitXbox360(x360, report);
                    }
                    else if (_device is IDualShock4Controller ds4)
                    {
                        SubmitDualShock4(ds4, report);
                    }
                }
                catch
                {
                    // 设备可能正被驱动回收：丢弃这一帧，不让异常穿出到输入线程。
                }
            }
        }

        private static void SubmitXbox360(IXbox360Controller pad, PadReport report)
        {
            pad.SetButtonState(Xbox360Button.A, Has(report.Buttons, PadButtons.A));
            pad.SetButtonState(Xbox360Button.B, Has(report.Buttons, PadButtons.B));
            pad.SetButtonState(Xbox360Button.X, Has(report.Buttons, PadButtons.X));
            pad.SetButtonState(Xbox360Button.Y, Has(report.Buttons, PadButtons.Y));
            pad.SetButtonState(Xbox360Button.LeftShoulder, Has(report.Buttons, PadButtons.LeftShoulder));
            pad.SetButtonState(Xbox360Button.RightShoulder, Has(report.Buttons, PadButtons.RightShoulder));
            pad.SetButtonState(Xbox360Button.Back, Has(report.Buttons, PadButtons.Back));
            pad.SetButtonState(Xbox360Button.Start, Has(report.Buttons, PadButtons.Start));
            pad.SetButtonState(Xbox360Button.LeftThumb, Has(report.Buttons, PadButtons.LeftThumb));
            pad.SetButtonState(Xbox360Button.RightThumb, Has(report.Buttons, PadButtons.RightThumb));
            pad.SetButtonState(Xbox360Button.Guide, Has(report.Buttons, PadButtons.Guide));
            pad.SetButtonState(Xbox360Button.Up, Has(report.Buttons, PadButtons.DPadUp));
            pad.SetButtonState(Xbox360Button.Down, Has(report.Buttons, PadButtons.DPadDown));
            pad.SetButtonState(Xbox360Button.Left, Has(report.Buttons, PadButtons.DPadLeft));
            pad.SetButtonState(Xbox360Button.Right, Has(report.Buttons, PadButtons.DPadRight));

            pad.SetSliderValue(Xbox360Slider.LeftTrigger, report.LeftTrigger);
            pad.SetSliderValue(Xbox360Slider.RightTrigger, report.RightTrigger);

            pad.SetAxisValue(Xbox360Axis.LeftThumbX, report.LeftX);
            pad.SetAxisValue(Xbox360Axis.LeftThumbY, report.LeftY);
            pad.SetAxisValue(Xbox360Axis.RightThumbX, report.RightX);
            pad.SetAxisValue(Xbox360Axis.RightThumbY, report.RightY);

            pad.SubmitReport();
        }

        private static void SubmitDualShock4(IDualShock4Controller pad, PadReport report)
        {
            pad.SetButtonState(DualShock4Button.Cross, Has(report.Buttons, PadButtons.A));
            pad.SetButtonState(DualShock4Button.Circle, Has(report.Buttons, PadButtons.B));
            pad.SetButtonState(DualShock4Button.Square, Has(report.Buttons, PadButtons.X));
            pad.SetButtonState(DualShock4Button.Triangle, Has(report.Buttons, PadButtons.Y));
            pad.SetButtonState(DualShock4Button.ShoulderLeft, Has(report.Buttons, PadButtons.LeftShoulder));
            pad.SetButtonState(DualShock4Button.ShoulderRight, Has(report.Buttons, PadButtons.RightShoulder));
            pad.SetButtonState(DualShock4Button.Share, Has(report.Buttons, PadButtons.Back));
            pad.SetButtonState(DualShock4Button.Options, Has(report.Buttons, PadButtons.Start));
            pad.SetButtonState(DualShock4Button.ThumbLeft, Has(report.Buttons, PadButtons.LeftThumb));
            pad.SetButtonState(DualShock4Button.ThumbRight, Has(report.Buttons, PadButtons.RightThumb));

            // DualShock4SpecialButton 继承自 DualShock4Button：SetButtonState 直接接受。
            pad.SetButtonState(DualShock4SpecialButton.Ps, Has(report.Buttons, PadButtons.Guide));
            pad.SetButtonState(DualShock4SpecialButton.Touchpad, Has(report.Buttons, PadButtons.Touchpad));

            // DS4 的方向键是单一 DPad 方向值：按 Xbox 方向位推导，优先级 Up > Down > Left > Right。
            pad.SetDPadDirection(ResolveDPad(report.Buttons));

            pad.SetSliderValue(DualShock4Slider.LeftTrigger, report.LeftTrigger);
            pad.SetSliderValue(DualShock4Slider.RightTrigger, report.RightTrigger);

            // DS4 摇杆轴是 0..255 的无符号字节，按 DS4 报告格式映射。
            pad.SetAxisValue(DualShock4Axis.LeftThumbX, ToDs4Axis(report.LeftX));
            pad.SetAxisValue(DualShock4Axis.LeftThumbY, ToDs4Axis(report.LeftY));
            pad.SetAxisValue(DualShock4Axis.RightThumbX, ToDs4Axis(report.RightX));
            pad.SetAxisValue(DualShock4Axis.RightThumbY, ToDs4Axis(report.RightY));

            pad.SubmitReport();
        }

        private static DualShock4DPadDirection ResolveDPad(uint buttons)
        {
            if (Has(buttons, PadButtons.DPadUp))
            {
                return DualShock4DPadDirection.North;
            }

            if (Has(buttons, PadButtons.DPadDown))
            {
                return DualShock4DPadDirection.South;
            }

            if (Has(buttons, PadButtons.DPadLeft))
            {
                return DualShock4DPadDirection.West;
            }

            if (Has(buttons, PadButtons.DPadRight))
            {
                return DualShock4DPadDirection.East;
            }

            return DualShock4DPadDirection.None;
        }

        /// <summary>short(-32768..32767) → byte(0..255)。</summary>
        private static byte ToDs4Axis(short value)
        {
            return (byte)((value + 32768) / 256);
        }

        private static bool Has(uint buttons, PadButtons flag) => (buttons & (uint)flag) != 0;

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
                    if (_x360FeedbackHandler != null && _device is IXbox360Controller x360)
                    {
                        x360.FeedbackReceived -= _x360FeedbackHandler;
                    }

                    if (_ds4FeedbackHandler != null && _device is IDualShock4Controller ds4)
                    {
                        ds4.FeedbackReceived -= _ds4FeedbackHandler;
                    }

                    // 顺序必须是先 Disconnect 再 Dispose。
                    _device.Disconnect();
                    if (_device is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch
                {
                    // 旧设备回收失败不影响新设备接管。
                }
            }
        }
    }
}
