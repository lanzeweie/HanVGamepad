using System.Diagnostics;

namespace HanVGamepad.Cli.Commands
{
    /// <summary>
    /// `hanvgamepad stop`：停止由 `hanvgamepad start` 运行的实例（按 pid 文件定位）。
    /// </summary>
    public static class StopCommand
    {
        public static Task<int> RunAsync()
        {
            int? pid = PidFile.ReadAlive();
            if (pid == null)
            {
                Console.WriteLine("no running instance found (no alive pid file).");
                PidFile.Clear();
                return Task.FromResult(0);
            }

            try
            {
                using Process process = Process.GetProcessById(pid.Value);

                // start 是控制台进程（无主窗口）：直接终止。
                // 端口与虚拟设备随进程回收（ViGEm 驱动连接断开、uinput fd 关闭自动消失）。
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);

                PidFile.Clear();
                Console.WriteLine($"stopped instance (pid {pid}).");
                return Task.FromResult(0);
            }
            catch (ArgumentException)
            {
                // 已经退出了。
                PidFile.Clear();
                Console.WriteLine("instance already exited.");
                return Task.FromResult(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to stop instance {pid}: {ex.Message}");
                return Task.FromResult(1);
            }
        }
    }
}
