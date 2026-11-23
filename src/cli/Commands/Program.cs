namespace HanVGamepad.Cli.Commands
{
    /// <summary>
    /// 命令行入口：解析 start / stop / status / driver 子命令。
    /// 无 UI 控制台入口（Linux headless / 开发调试）。
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                PrintUsage();
                return 0;
            }

            try
            {
                return args[0].ToLowerInvariant() switch
                {
                    "start" => await StartCommand.RunAsync(args.Skip(1).ToArray()),
                    "stop" => await StopCommand.RunAsync(),
                    "status" => await StatusCommand.RunAsync(),
                    "driver" => await DriverCommand.RunAsync(args.Skip(1).ToArray()),
                    _ => Unknown(args[0])
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 1;
            }
        }

        private static int Unknown(string command)
        {
            Console.Error.WriteLine($"unknown command: {command}");
            PrintUsage();
            return 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("""
                HanVGamepad — turn any smartphone browser into a virtual PC controller

                usage:
                  hanvgamepad start [--port <1-65535>] [--password <pwd>]
                  hanvgamepad stop
                  hanvgamepad status
                  hanvgamepad driver status
                  hanvgamepad driver install
                  hanvgamepad driver uninstall

                commands:
                  start            Start the gamepad service in the foreground (Ctrl+C to stop).
                  stop             Stop a background instance started with `hanvgamepad start`.
                  status           Show service and driver status.
                  driver status    Show the gamepad driver status (ViGEmBus / uinput).
                  driver install   Install / configure the gamepad driver.
                  driver uninstall Uninstall / clean up the gamepad driver.
                """);
        }
    }
}
