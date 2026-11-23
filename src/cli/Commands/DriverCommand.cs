using HanVGamepad.Driver;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Cli.Commands
{
    /// <summary>
    /// `hanvgamepad driver status|install|uninstall`：驱动管理。
    /// </summary>
    public static class DriverCommand
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("error: driver requires a subcommand: status | install | uninstall");
                return 1;
            }

            IDriverManager driverManager = DriverServices.CreateDriverManager();
            var progress = new Progress<string>(message => Console.WriteLine("  " + message));

            switch (args[0].ToLowerInvariant())
            {
                case "status":
                {
                    DriverStatus status = await driverManager.GetStatusAsync();
                    Console.WriteLine($"installed   : {status.IsInstalled}");
                    Console.WriteLine($"service     : {(status.IsServiceRunning ? "running" : "stopped")}");
                    Console.WriteLine($"version     : {status.Version ?? "unknown"}");
                    Console.WriteLine($"permission  : {status.Permission}");
                    if (status.DiagnosticMessage != null)
                    {
                        Console.WriteLine($"diagnostic  : {status.DiagnosticMessage}");
                    }

                    return 0;
                }

                case "install":
                {
                    Console.WriteLine("installing / configuring the gamepad driver...");
                    DriverInstallResult result = await driverManager.InstallAsync(progress);

                    if (result.Success)
                    {
                        Console.WriteLine("driver installed successfully.");
                        return 0;
                    }

                    Console.Error.WriteLine($"driver install failed: {result.ErrorMessage}");
                    return 1;
                }

                case "uninstall":
                {
                    Console.WriteLine("uninstalling / cleaning up the gamepad driver...");
                    DriverUninstallResult result = await driverManager.UninstallAsync();

                    if (result.Success)
                    {
                        Console.WriteLine("driver uninstalled successfully.");
                        return 0;
                    }

                    Console.Error.WriteLine($"driver uninstall failed: {result.ErrorMessage}");
                    return 1;
                }

                default:
                    Console.Error.WriteLine($"error: unknown driver subcommand {args[0]}");
                    return 1;
            }
        }
    }
}
