using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HanVGamepad.Desktop.Services;
using HanVGamepad.Desktop.Settings;

namespace HanVGamepad.Desktop
{
    internal static class Program
    {
        /// <summary>
        /// 单实例：两个进程会抢同一个端口、抢同一份 settings.json，托盘里还会并排两个图标。
        /// 拿不到 Mutex 就退出（句柄活到 Main 结束，也就是进程退出）。
        /// </summary>
        private static Mutex? _singleInstance;

        // Avalonia 初始化前不能用 Application.Current：这里用原生消息框兜底。
        [STAThread]
        public static void Main(string[] args)
        {
            // 测试开关要在任何平台判定之前生效：槽位数、UI 密度、真实手柄扣减都读它。
            HanVGamepad.Driver.Abstractions.GamepadLimits.ApplyStartupOverride(args);

            AppSettings settings = AppSettings.Load();
            string language = settings.Language;

            _singleInstance = new Mutex(true, "HanVGamepad.SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                ShowNativeMessage(
                    Strings.T(language, "dialog.alreadyRunning.text"),
                    Strings.T(language, "dialog.alreadyRunning.title"));
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// 启动早期的系统消息框（单实例冲突）。跨平台：
        /// Windows 用 Win32 MessageBox；其余平台尝试 zenity/kdialog，都没有就写 stderr。
        /// </summary>
        private static void ShowNativeMessage(string text, string title)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Win32MessageBox(text, title);
                    return;
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = $"--info --title=\"{title}\" --text=\"{text}\"",
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);
            }
            catch
            {
                Console.Error.WriteLine($"{title}: {text}");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private static void Win32MessageBox(string text, string title)
        {
            if (OperatingSystem.IsWindows())
            {
                MessageBox(IntPtr.Zero, text, title, 0x40); // MB_ICONINFORMATION
            }
        }
    }
}
