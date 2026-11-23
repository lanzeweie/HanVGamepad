using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using HanVGamepad.Desktop.Services;
using HanVGamepad.Desktop.Settings;
using HanVGamepad.Desktop.Tray;
using HanVGamepad.Desktop.ViewModels;
using HanVGamepad.Desktop.Views;
using HanVGamepad.Driver;
using HanVGamepad.Driver.Abstractions;
using HanVGamepad.Server;
using HanVGamepad.Server.Logging;

namespace HanVGamepad.Desktop
{
    public partial class App : Application
    {
        private MainViewModel? _mainViewModel;
        private TrayController? _tray;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            ServerLog.Reset();
            ServerLog.Info("HanVGamepad 启动");

            if (GamepadLimits.PretendPortable)
            {
                ServerLog.Info(
                    $"伪装便携平台：{GamepadLimits.PortableMaxPads} 槽 / 紧凑布局。仅用于测试，" +
                    "驱动后端仍是本机真实实现（Windows 上 XInput 只认前 4 个手柄）。");
            }

            AppSettings settings = AppSettings.Load();

            // 主题先定：手动选过就按存的变体走，空值交回系统（App.axaml 的 Default）。
            // 启动时不应用的话，界面跟随系统、开关却读设置，两边口径就对不上。
            ApplyStartupTheme(settings.ThemeMode);

            // 驱动层按平台分派：Windows → ViGEmBus 管理器，Linux → uinput 管理器。
            IDriverManager driverManager = DriverServices.CreateDriverManager();
            IGamepadProbe padProbe = DriverServices.CreateGamepadProbe();
            var server = new ServerRunner(driverManager, padProbe);

            _mainViewModel = new MainViewModel(server, driverManager, settings, padProbe);
            _mainViewModel.ThemeChangeRequested += ApplyTheme;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow
                {
                    DataContext = _mainViewModel
                };

                desktop.MainWindow = mainWindow;

                // 平台主题要等窗口起来才解析得出来：跟随系统时在这里把开关位置对齐一次。
                mainWindow.Opened += (_, _) => _mainViewModel?.SyncThemeWithSystem();

                // 托盘：点 × 收进托盘，服务继续运行；托盘菜单「退出」才真退。
                _tray = new TrayController(settings.Language);
                _tray.ShowRequested += () => Dispatcher.UIThread.Post(() =>
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                    _mainViewModel.Refresh();
                });
                _tray.ExitRequested += () => Dispatcher.UIThread.Post(mainWindow.CloseFromTray);

                mainWindow.MinimizedToTray += (title, text) =>
                    _tray.ShowMinimizedHint(title, text, settings.Language);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>启动时恢复主题：手动选过就按存的变体，空值交回系统（App.axaml 的 Default）。</summary>
        private void ApplyStartupTheme(string themeMode)
        {
            RequestedThemeVariant = themeMode switch
            {
                AppSettings.ThemeDark => ThemeVariant.Dark,
                AppSettings.ThemeLight => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
        }

        /// <summary>主题切换：手动选 dark / light；空值跟随系统。</summary>
        private void ApplyTheme(bool dark)
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        public void ApplyLanguageToTray(string language)
        {
            _tray?.ApplyLanguage(language);
        }

        public void DisposeTray()
        {
            _tray?.Dispose();
            _tray = null;
            _mainViewModel?.Shutdown();
        }
    }
}
