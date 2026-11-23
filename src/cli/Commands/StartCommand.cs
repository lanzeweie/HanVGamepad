using HanVGamepad.Driver;
using HanVGamepad.Driver.Abstractions;
using HanVGamepad.Server;
using HanVGamepad.Server.Logging;

namespace HanVGamepad.Cli.Commands
{
    /// <summary>
    /// `hanvgamepad start`：前台运行服务，Ctrl+C 优雅退出。
    /// 参数：--port &lt;1-65535&gt;（默认 5000）、--password &lt;pwd&gt;（默认空）。
    /// </summary>
    public static class StartCommand
    {
        public static async Task<int> RunAsync(string[] args)
        {
            int port = 5000;
            string password = string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port" when i + 1 < args.Length:
                        if (!int.TryParse(args[++i], out port) || port < 1 || port > 65535)
                        {
                            Console.Error.WriteLine("error: --port must be between 1 and 65535");
                            return 1;
                        }

                        break;

                    case "--password" when i + 1 < args.Length:
                        password = args[++i];
                        break;

                    default:
                        Console.Error.WriteLine($"error: unknown option {args[i]}");
                        return 1;
                }
            }

            if (!DriverServices.IsPlatformSupported)
            {
                Console.Error.WriteLine("error: this platform is not supported (Windows / Linux / macOS).");
                return 1;
            }

            ServerLog.Reset();
            ServerLog.Info("HanVGamepad CLI 启动");

            IDriverManager driverManager = DriverServices.CreateDriverManager();

            DriverStatus driverStatus = await driverManager.GetStatusAsync();
            if (!driverStatus.IsInstalled)
            {
                Console.Error.WriteLine("error: the gamepad driver is not ready. Run: hanvgamepad driver install");
                return 1;
            }

            var server = new ServerRunner(driverManager, DriverServices.CreateGamepadProbe());
            using var shutdown = new CancellationTokenSource();

            // Ctrl+C 优雅退出。
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdown.Cancel();
            };

            try
            {
                await server.StartServerAsync(port, password);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to start the service: {ex.Message}");
                return 1;
            }

            // 启动自检：端口在监听、首页可访问才算真启动。
            ServerHealthCheck.Result portCheck = await Task.Run(() => ServerHealthCheck.Check(port));
            if (!portCheck.Ok)
            {
                Console.Error.WriteLine($"error: startup self-check failed: {portCheck.Message}");
                await server.StopServerAsync();
                return 1;
            }

            ServerHealthCheck.Result homeCheck = await ServerHealthCheck.CheckHomepageAsync(port, "127.0.0.1");
            if (!homeCheck.Ok)
            {
                Console.Error.WriteLine($"error: startup self-check failed: {homeCheck.Message}");
                await server.StopServerAsync();
                return 1;
            }

            // 记录 pid，供 stop / status 使用。
            PidFile.Write(Environment.ProcessId);

            Console.WriteLine($"HanVGamepad service running on http://0.0.0.0:{port}");
            Console.WriteLine("Phones on the same Wi-Fi / LAN can connect now. Press Ctrl+C to stop.");

            try
            {
                await Task.Delay(Timeout.Infinite, shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C：正常退出路径。
            }

            Console.WriteLine("Stopping the service...");
            try
            {
                await server.StopServerAsync();
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"停止服务时出错：{ex.Message}");
            }

            PidFile.Clear();
            Console.WriteLine("Stopped.");
            return 0;
        }
    }
}
