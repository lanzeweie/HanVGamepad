using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Server.Slots
{
    /// <summary>客户端选手柄类型的协议值（0 = Xbox360，1 = DualShock4）。</summary>
    public enum TypeController
    {
        Xbox360,
        DualShock4
    }

    /// <summary>协议手柄类型 → 驱动层设备类型。</summary>
    public static class TypeControllerMap
    {
        public static VirtualPadType ToPadType(this TypeController type) =>
            type == TypeController.DualShock4 ? VirtualPadType.DualShock4 : VirtualPadType.Xbox360;
    }

    /// <summary>单个连接的会话设置与设备引用。</summary>
    public sealed class GamepadSettings
    {
        public GamepadSettings(bool enableVibration, int joystickSensitivity, TypeController controllerType, IVirtualPad gamepad, int controllerIndex)
        {
            EnableVibration = enableVibration;
            JoystickSensitivity = joystickSensitivity;
            ControllerType = controllerType;
            Gamepad = gamepad;
            ControllerIndex = controllerIndex;
        }

        public bool EnableVibration { get; set; }
        public int JoystickSensitivity { get; set; }
        public TypeController ControllerType { get; set; }
        public IVirtualPad Gamepad { get; }
        public int ControllerIndex { get; }

        /// <summary>
        /// 该连接当前的输入状态（协议消息是增量的，PadReport 提交是全量的）。
        /// 只在 GamepadManager 的锁内读写。
        /// </summary>
        public PadReport Input { get; set; } = PadReport.Neutral;
    }

    /// <summary>
    /// 推给手机端的槽位投影。Slot 是 0-based，展示号 = Slot + 1 由客户端负责。
    /// </summary>
    public sealed record SlotInfo(int Slot, bool Occupied);

    /// <summary>
    /// GetSnapshot() 的只读投影。Slot 是 0-based 的 ControllerIndex（等于 XInput 槽位），
    /// 展示号 = Slot + 1 由界面负责。ControllerType 是该槽位当前设备的实际类型，
    /// DeviceName / RemoteIp 是手机端自报的设备名与局域网 IP，空槽为 null。
    /// </summary>
    public sealed record GamepadSnapshot(
        int Slot, string? ControllerType, bool Occupied, string? ConnectionId,
        string? DeviceName, string? RemoteIp);
}
