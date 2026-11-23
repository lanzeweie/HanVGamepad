using System.Diagnostics;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver.MacOS
{
    /// <summary>
    /// macOS 虚拟手柄后端：每台手柄对应一个 <c>ojd virtual feed</c> 子进程。
    ///
    /// OJD（OpenJoystickDriver）持有 Apple 的虚拟 HID entitlement，由它把设备发布给系统；
    /// 本后端不直接碰 IOKit。OJD 一侧最多同时跑 4 条 feed，正好对上这里的槽位上限。
    ///
    /// DualShock4 请求在 macOS 端降级为 OJD 的通用手柄档位（与 Linux 端一致）：
    /// 真正发布 DS4 需要 HID 层仿真，不在本项目范围内。
    /// </summary>
    internal sealed class MacGamepadBackend : IGamepadBackend
    {
        /// <summary>
        /// OJD 的虚拟手柄档位。<c>sdl-compatible</c> 是 OJD 里被 SDL / Steam 认得的档位
        /// （GameControllerCompatible 对默认 SDL hints 不生效，generic 的 GUID 没有 CRC）。
        /// </summary>
        private const string Persona = "sdl-compatible";

        private readonly string _ojdPath;
        private readonly object _gate = new();
        private bool _disposed;

        internal MacGamepadBackend(string ojdPath)
        {
            _ojdPath = ojdPath;
        }

        public IVirtualPad CreatePad(VirtualPadType type)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(MacGamepadBackend));
                }

                var startInfo = new ProcessStartInfo(_ojdPath)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("virtual");
                startInfo.ArgumentList.Add("feed");
                startInfo.ArgumentList.Add("--as");
                startInfo.ArgumentList.Add(Persona);

                Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start 'ojd virtual feed'.");

                // OJD 未运行 / 没有权限时会立刻退出：把 stderr 的第一行带回给界面，
                // 否则用户只会看到「手柄创建失败」，不知道是 OJD 服务没起来。
                if (process.WaitForExit(150))
                {
                    string error = TryReadError(process);
                    process.Dispose();

                    throw new InvalidOperationException(
                        error.Length > 0
                            ? "OpenJoystickDriver refused the virtual gamepad: " + error
                            : "OpenJoystickDriver exited immediately. Start its service (ojd service start) and try again.");
                }

                return new OjdVirtualPad(process, type);
            }
        }

        private static string TryReadError(Process process)
        {
            try
            {
                string text = process.StandardError.ReadToEnd().Trim();
                int lineBreak = text.IndexOf('\n');
                return lineBreak > 0 ? text[..lineBreak].Trim() : text;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                // 后端本身不持有子进程：每条 feed 的生命周期归它自己的 IVirtualPad。
                _disposed = true;
            }
        }
    }
}
