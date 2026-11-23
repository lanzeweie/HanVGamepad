using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HanVGamepad.Desktop.Services;
using HanVGamepad.Desktop.ViewModels;
using System.Runtime.InteropServices;

namespace HanVGamepad.Desktop.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>用户点 × 收进托盘时通知（App 订阅后弹系统通知）。</summary>
        public event Action<string, string>? MinimizedToTray;

        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _toastTimer;

        private bool _allowClose;
        private bool _exiting;
        private bool _languageSeeding = true;
        private nint _titleBarIcon;

        public MainWindow()
        {
            InitializeComponent();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refreshTimer.Tick += (_, _) =>
            {
                // 收进托盘/最小化时没人看得见，不用每 500ms 建一遍模型（还顺带枚举网卡）。
                if (IsVisible && WindowState != WindowState.Minimized)
                {
                    (DataContext as MainViewModel)?.Refresh();
                }
            };

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                if (DataContext is MainViewModel vm)
                {
                    vm.IsToastOpen = false;
                }
            };

            Opened += OnOpened;
            Closing += OnWindowClosing;

            // 标题栏图标与任务栏/托盘分开：Win32 的 ICON_SMALL（标题栏）和 ICON_BIG
            // （任务栏按钮、Alt+Tab）是两套句柄，Avalonia 的 Window.Icon 一刀切，
            // 所以这里只覆盖标题栏那颗。Activated 重设一次，防止窗口状态切换时被还原。
            Opened += (_, _) => ApplyTitleBarIcon();
            Activated += (_, _) => ApplyTitleBarIcon();
        }

        /// <summary>
        /// 把标题栏左上角图标换成 Assets/TitleIcon.ico（透明手柄）。
        ///
        /// 任务栏（ICON_BIG，来自 csproj 的 ApplicationIcon）和托盘
        /// （TrayController 读 Resources/HanVGamepadIcon.ico）都不经过这里。
        /// </summary>
        private void ApplyTitleBarIcon()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            nint handle = TryGetPlatformHandle()?.Handle ?? 0;
            if (handle == 0)
            {
                return;
            }

            if (_titleBarIcon == 0)
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "TitleIcon.ico");
                if (!File.Exists(path))
                {
                    return;
                }

                // LR_SHARED：图标由系统缓存管理，不需要 DestroyIcon。
                _titleBarIcon = LoadImage(0, path, ImageIcon, 16, 16, LrLoadFromFile | LrShared);
            }

            if (_titleBarIcon != 0)
            {
                SendMessage(handle, WmSetIcon, IconSmall, _titleBarIcon);
            }
        }

        private async void OnOpened(object? sender, EventArgs e)
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            // 语言下拉：语言自身名字不随界面语言变化。
            var languageBox = this.FindControl<ComboBox>("LanguageBox");
            if (languageBox != null)
            {
                languageBox.ItemsSource = Languages.All.Select(Strings.LanguageName).ToArray();
                languageBox.SelectedIndex = Array.IndexOf(Languages.All, vm.Language);
            }

            // 密码显示/隐藏初始态。
            ApplyPasswordVisibility(vm.PasswordVisible);

            // VM 属性联动：toast 弹出自动关闭、密码可见性切换、语言变更刷新密码框。
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsToastOpen) && vm.IsToastOpen)
                {
                    _toastTimer.Stop();
                    _toastTimer.Start();
                }
                else if (args.PropertyName == nameof(MainViewModel.PasswordVisible))
                {
                    ApplyPasswordVisibility(vm.PasswordVisible);
                }
                else if (args.PropertyName == nameof(MainViewModel.Language))
                {
                    SyncLanguageBox(vm.Language);
                }
            };

            _languageSeeding = false;
            _refreshTimer.Start();

            await vm.InitializeAsync();
        }

        private void ApplyPasswordVisibility(bool visible)
        {
            var passwordBox = this.FindControl<TextBox>("PasswordBox");
            if (passwordBox != null)
            {
                // 只影响显示；Text 本身始终是明文值。
                passwordBox.PasswordChar = visible ? '\0' : '●';
            }
        }

        private void SyncLanguageBox(string language)
        {
            var languageBox = this.FindControl<ComboBox>("LanguageBox");
            if (languageBox != null)
            {
                int index = Array.IndexOf(Languages.All, language);
                if (index >= 0 && languageBox.SelectedIndex != index)
                {
                    _languageSeeding = true;
                    languageBox.SelectedIndex = index;
                    _languageSeeding = false;
                }
            }
        }

        /// <summary>
        /// 点在非输入区域时主动清焦点：Avalonia 点空白不会自动让 TextBox 失焦，
        /// 光标会一直闪下去，看起来像卡住了。
        /// </summary>
        private void OnBackgroundPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.Source is Avalonia.Visual source &&
                source.FindAncestorOfType<TextBox>(includeSelf: true) != null)
            {
                // 点在输入框本身（或其内部元素）上：让它正常拿焦点。
                return;
            }

            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }

        private void OnCredentialsLostFocus(object? sender, RoutedEventArgs e)        {
            if (DataContext is MainViewModel vm && vm.CommitCredentialsCommand.CanExecute(null))
            {
                vm.CommitCredentialsCommand.Execute(null);
            }
        }

        private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_languageSeeding)
            {
                return;
            }

            var languageBox = sender as ComboBox;
            if (languageBox?.SelectedItem is string name && DataContext is MainViewModel vm)
            {
                // 显示名 → 语言码。
                int index = Array.IndexOf(Languages.All.Select(Strings.LanguageName).ToArray(), name);
                if (index >= 0)
                {
                    vm.ChangeLanguage(Languages.All[index]);
                    (App.Current as App)?.ApplyLanguageToTray(Languages.All[index]);
                }
            }
        }

        // ── 关闭行为：点 × 收进托盘，托盘「退出」才真退 ──────────────
        private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            // 用户点 × ：只是收进托盘，服务继续跑。用系统通知告诉用户窗口去哪了。
            if (!_allowClose && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                Hide();

                if (DataContext is MainViewModel vm)
                {
                    MinimizedToTray?.Invoke(
                        Strings.T(vm.Language, "tray.minimized.title"),
                        Strings.T(vm.Language, "tray.minimized.text"));
                }

                return;
            }

            // 已经走过一次关停流程：放行。
            if (_exiting)
            {
                return;
            }

            // 真退出（托盘菜单）：先停服再关窗，不能在 UI 线程上干等。
            if (e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                await ExitAfterShutdownAsync();
                return;
            }

            // 关机 / 任务管理器结束：不能取消，最多等两秒就走。
            if (DataContext is MainViewModel vm2)
            {
                try
                {
                    Task.Run(() => vm2.ToggleServerCommand.ExecuteAsync(null)).Wait(2000);
                }
                catch (Exception)
                {
                    // 关停路径上不再冒泡。
                }
            }
        }

        /// <summary>托盘菜单「退出」调用：走完整的关停流程。</summary>
        public async void CloseFromTray()
        {
            _allowClose = true;
            await ExitAfterShutdownAsync();
        }

        private async Task ExitAfterShutdownAsync()
        {
            _exiting = true;
            _refreshTimer.Stop();
            _toastTimer.Stop();

            if (DataContext is MainViewModel vm)
            {
                try
                {
                    // 停服务（StopServerAsync 默认排空上限 30 秒：等 5 秒就走，进程退出会回收端口和设备）。
                    if (vm.IsRunning)
                    {
                        Task stop = vm.ToggleServerCommand.ExecuteAsync(null);
                        Task finished = await Task.WhenAny(stop, Task.Delay(5000));
                        if (finished == stop)
                        {
                            await stop;
                        }
                    }

                    vm.Shutdown();
                }
                catch (Exception ex)
                {
                    Server.Logging.ServerLog.Warn($"退出时停止服务失败：{ex.Message}");
                }
            }

            (App.Current as App)?.DisposeTray();

            _allowClose = true;
            Close();
        }

        // ── Win32：只设置 ICON_SMALL，不动任务栏的 ICON_BIG ──────────
        private const int WmSetIcon = 0x0080;
        private const uint ImageIcon = 1;
        private const uint LrLoadFromFile = 0x0010;
        private const uint LrShared = 0x8000;
        private static readonly nint IconSmall = 0;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint LoadImage(nint hinst, string lpszName, uint uType,
            int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
    }
}
