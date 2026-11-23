using HanVGamepad.Driver;
using HanVGamepad.Driver.Abstractions;
using HanVGamepad.Server;

namespace HanVGamepad.Cli.Commands
{
    /// <summary>
    /// `hanvgamepad status`：显示服务与驱动状态。
    /// </summary>
    public static class StatusCommand
    {
        public static async Task<int> RunAsync()
        {
            Console.WriteLine("HanVGamepad status");
            Console.WriteLine(new string('─', 40));

            // 驱动状态。
            try
            {
                IDriverManager driverManager = DriverServices.CreateDriverManager();
                DriverStatus driver = await driverManager.GetStatusAsync();

                Console.WriteLine($"driver      : {(driver.IsInstalled ? "installed" : "not installed")}");
                Console.WriteLine($"driver svc  : {(driver.IsServiceRunning ? "running" : "stopped")}");
                if (driver.Version != null)
                {
                    Console.WriteLine($"driver ver  : {driver.Version}");
                }

                Console.WriteLine($"permission  : {driver.Permission}");
                if (driver.DiagnosticMessage != null)
                {
                    Console.WriteLine($"diagnostic  : {driver.DiagnosticMessage}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"driver      : check failed ({ex.Message})");
            }

            Console.WriteLine(new string('─', 40));

            // 服务进程。
            int? pid = PidFile.ReadAlive();
            Console.WriteLine($"service     : {(pid != null ? $"running (pid {pid})" : "stopped")}");

            if (pid != null)
            {
                // 端口探测：从 pid 文件旁的进程拿不到端口，这里探测常见端口帮助定位。
                foreach (int port in new[] { 5000 })
                {
                    ServerHealthCheck.Result check = await Task.Run(() => ServerHealthCheck.Check(port, 1500));
                    if (check.Ok)
                    {
                        Console.WriteLine($"port {port}    : reachable");
                    }
                }
            }

            return 0;
        }
    }
}
