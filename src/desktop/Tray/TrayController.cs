using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using HanVGamepad.Desktop.Services;

namespace HanVGamepad.Desktop.Tray
{
    /// <summary>
    /// 系统托盘：显示窗口 / 退出两个菜单项。刻意不做启动/停止服务，也不做开机自启。
    /// 文案走 Strings（tray.show / tray.exit）；语言切换时 ApplyLanguage 就地改菜单文案。
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        private readonly TrayIcon _trayIcon;
        private readonly NativeMenuItem _showItem;
        private readonly NativeMenuItem _exitItem;

        public event Action? ShowRequested;
        public event Action? ExitRequested;

        public TrayController(string language)
        {
            _showItem = new NativeMenuItem(Strings.T(language, "tray.show"));
            _showItem.Click += (_, _) => ShowRequested?.Invoke();

            _exitItem = new NativeMenuItem(Strings.T(language, "tray.exit"));
            _exitItem.Click += (_, _) => ExitRequested?.Invoke();

            var menu = new NativeMenu();
            menu.Items.Add(_showItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(_exitItem);

            _trayIcon = new TrayIcon
            {
                Icon = LoadIcon(),
                ToolTipText = "HanVGamepad",
                Menu = menu
            };

            // 双击 = 显示窗口（与原 WinForms 版一致）。
            _trayIcon.Clicked += (_, _) => ShowRequested?.Invoke();

            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _trayIcon });
        }

        private static WindowIcon? LoadIcon()
        {
            try
            {
                var uri = new Uri("avares://HanVGamepad.Desktop/Resources/HanVGamepadIcon.ico");
                using Stream stream = AssetLoader.Open(uri);
                return new WindowIcon(stream);
            }
            catch
            {
                // 图标缺失不影响功能：托盘图标置空，由系统给默认图标。
                // 注意不能用空流构造 WindowIcon —— 那会直接抛 ArgumentException 崩掉启动。
                return null;
            }
        }

        /// <summary>只改两项菜单文案，不动托盘图标本身。</summary>
        public void ApplyLanguage(string language)
        {
            _showItem.Header = Strings.T(language, "tray.show");
            _exitItem.Header = Strings.T(language, "tray.exit");
        }

        /// <summary>关闭窗口后的系统通知（Windows 通知中心 / Linux 桌面通知）。</summary>
        public void ShowMinimizedHint(string title, string message, string language)
        {
            // Avalonia 托盘气泡在 Windows 上走 Shell_NotifyIcon；方向标记保证阿语排版。
            _trayIcon.ToolTipText = Strings.WithDirectionMarks(title, language);
        }

        public void Dispose()
        {
            _trayIcon.Dispose();
        }
    }
}
