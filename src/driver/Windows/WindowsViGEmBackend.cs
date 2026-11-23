using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver.Windows
{
    /// <summary>
    /// Windows 虚拟手柄后端：包装 Nefarius ViGEmClient（ViGEmBus 内核驱动的用户态客户端）。
    /// 由 WindowsDriverManager 在驱动就绪后创建。
    /// </summary>
    internal sealed class WindowsViGEmBackend : IGamepadBackend
    {
        private readonly ViGEmClient _client;
        private readonly object _gate = new();
        private bool _disposed;

        public WindowsViGEmBackend()
        {
            _client = new ViGEmClient();
        }

        public IVirtualPad CreatePad(VirtualPadType type)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(WindowsViGEmBackend));
                }

                IVirtualGamepad device = type == VirtualPadType.DualShock4
                    ? _client.CreateDualShock4Controller()
                    : _client.CreateXbox360Controller();

                device.Connect();
                return new ViGEmVirtualPad(device, type);
            }
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
                    _client.Dispose();
                }
                catch
                {
                    // 客户端关闭失败不影响进程退出。
                }
            }
        }
    }
}
