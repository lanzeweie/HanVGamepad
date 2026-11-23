namespace HanVGamepad.Driver.Abstractions
{
    /// <summary>
    /// 虚拟手柄实例（平台无关）。
    /// 生命周期：由 IGamepadBackend.CreatePad 创建，Dispose 时从系统注销。
    /// 实现必须保证 Submit / ResetNeutral / Dispose 线程安全（输入线程与 UI 线程都会触碰）。
    /// </summary>
    public interface IVirtualPad : IDisposable
    {
        /// <summary>将按键/摇杆/扳机状态写入驱动并原子提交。</summary>
        void Submit(PadReport report);

        /// <summary>重置为中性状态（所有按键松开，摇杆归中，扳机归零）。</summary>
        void ResetNeutral();

        /// <summary>
        /// 游戏侧触发振动时回调（largeMotor, smallMotor，各 0..255）。
        /// Linux uinput 端不注册 EV_FF，不触发此事件。
        /// </summary>
        event Action<byte, byte>? VibrationReceived;

        /// <summary>设备类型。</summary>
        VirtualPadType Type { get; }
    }
}
