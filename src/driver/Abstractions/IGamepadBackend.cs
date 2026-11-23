namespace HanVGamepad.Driver.Abstractions
{
    /// <summary>
    /// 虚拟手柄后端的抽象接口。
    /// 实现：WindowsViGEmBackend（Windows）、LinuxUinputBackend（Linux）。
    /// 生命周期：由 IDriverManager 在驱动就绪后创建，程序退出前销毁。
    /// </summary>
    public interface IGamepadBackend : IDisposable
    {
        /// <summary>创建虚拟手柄实例并向系统注册。失败抛出异常（调用方负责捕获）。</summary>
        IVirtualPad CreatePad(VirtualPadType type);
    }
}
