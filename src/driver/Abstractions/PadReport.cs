namespace HanVGamepad.Driver.Abstractions
{
    /// <summary>
    /// 按键位掩码，语义统一为 Xbox 布局；DualShock4 实现层自行映射
    /// （A→Cross、B→Circle、X→Square、Y→Triangle 等）。
    /// </summary>
    [Flags]
    public enum PadButtons : uint
    {
        None = 0,
        A = 1u << 0,
        B = 1u << 1,
        X = 1u << 2,
        Y = 1u << 3,
        LeftShoulder = 1u << 4,
        RightShoulder = 1u << 5,
        Back = 1u << 6,
        Start = 1u << 7,
        LeftThumb = 1u << 8,
        RightThumb = 1u << 9,
        Guide = 1u << 10,
        Touchpad = 1u << 11,
        DPadUp = 1u << 12,
        DPadDown = 1u << 13,
        DPadLeft = 1u << 14,
        DPadRight = 1u << 15
    }

    /// <summary>
    /// 平台无关的手柄输入报告（由 GamepadManager 维护状态并整体提交）。
    /// 按键为位掩码（见 <see cref="PadButtons"/>）；摇杆轴 -32768..32767；扳机 0..255。
    /// </summary>
    public record PadReport
    {
        /// <summary>按键位掩码（Xbox 语义）。</summary>
        public uint Buttons { get; init; }

        public short LeftX { get; init; }
        public short LeftY { get; init; }
        public short RightX { get; init; }
        public short RightY { get; init; }

        public byte LeftTrigger { get; init; }
        public byte RightTrigger { get; init; }

        /// <summary>中性状态：全键松开、摇杆归中、扳机归零。</summary>
        public static PadReport Neutral => new();
    }
}
