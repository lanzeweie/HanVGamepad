namespace HanVGamepad.Driver.Abstractions
{
    /// <summary>虚拟手柄设备类型。Linux 端 DualShock4 降级为 Xbox360 映射（uinput 仅仿真 evdev/xpad 格式）。</summary>
    public enum VirtualPadType
    {
        Xbox360,
        DualShock4
    }
}
