using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanVGamepad.Desktop.Services;
using HanVGamepad.Desktop.Settings;
using HanVGamepad.Driver.Abstractions;
using HanVGamepad.Server;
using HanVGamepad.Server.Logging;
using HanVGamepad.Server.Slots;

namespace HanVGamepad.Desktop.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：服务启停、槽位投影、地址/二维码、设置、驱动管理、多语言。
    /// 对应原 WinForms 版 Form1 的全部功能逻辑。
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject
    {
        private const string KickReason = "kicked_by_admin";

        private readonly ServerRunner _server;
        private readonly IDriverManager _driverManager;
        private readonly AppSettings _settings;
        private readonly IGamepadProbe _padProbe;

        // 槽位数量：Windows 固定 4（XInput 上限）；便携平台从 4 起步，用户可以按需加到 16。
        [ObservableProperty]
        private int _slotCapacity = GamepadLimits.PortableDefaultPads;

        private DateTimeOffset? _startedAt;

        // 真实手柄探测节流：XInput 枚举要走系统调用，500ms 刷新里每秒探一次就够。
        private DateTimeOffset _lastPadProbeAt = DateTimeOffset.MinValue;

        // 二维码按「地址字符串」缓存；地址变了才重建，旧图交接完再释放。
        private Bitmap? _qrImage;
        private string _qrAddress = string.Empty;

        // ── 状态 ────────────────────────────────────────────────
        [ObservableProperty]
        private ServerPhase _phase = ServerPhase.Stopped;

        [ObservableProperty]
        private int _port;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private bool _passwordVisible;

        [ObservableProperty]
        private string _language;

        [ObservableProperty]
        private bool _isDark;

        // 跟随系统时在窗口打开后回填开关位置，这条路径不能把手动选择写死进 settings。
        private bool _suppressThemeWrite;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AddressHostText))]
        [NotifyPropertyChangedFor(nameof(AddressPortText))]
        private string _addressText = string.Empty;

        [ObservableProperty]
        private Bitmap? _qrBitmap;

        [ObservableProperty]
        private string _toastMessage = string.Empty;

        [ObservableProperty]
        private string _toastLevel = "success";

        [ObservableProperty]
        private bool _isToastOpen;

        // ── 驱动管理 ────────────────────────────────────────────
        [ObservableProperty]
        private string _driverStatusText = string.Empty;

        [ObservableProperty]
        private string _driverDetailText = string.Empty;

        [ObservableProperty]
        private string _driverDiagnostic = string.Empty;

        [ObservableProperty]
        private bool _driverBusy;

        /// <summary>系统里已连接的真实手柄数量（不含本程序创建的虚拟手柄）。</summary>
        [ObservableProperty]
        private int _realPadCount;

        public ObservableCollection<SlotViewModel> Slots { get; } = new();
        public ObservableCollection<IpItemViewModel> Ips { get; } = new();

        /// <summary>toast 展示（View 订阅）。</summary>
        public event Action? ToastShown;

        /// <summary>主题切换请求（View 订阅并应用到 Application）。</summary>
        public event Action<bool>? ThemeChangeRequested;

        public MainViewModel(ServerRunner server, IDriverManager driverManager, AppSettings settings, IGamepadProbe padProbe)
        {
            _server = server;
            _driverManager = driverManager;
            _settings = settings;
            _padProbe = padProbe;

            _port = settings.Port;
            _password = settings.Password;
            _language = settings.Language;
            _isDark = ResolveDark(settings.ThemeMode);

            for (int slot = 1; slot <= _slotCapacity; slot++)
            {
                Slots.Add(new SlotViewModel { Slot = slot, Language = _language });
            }

            _server.OnControllerIdAssigned += HandlePlayerChanged;
            _server.OnPlayerDisconnected += _ => HandlePlayerChanged(string.Empty, -1);
        }

        private static bool ResolveDark(string themeMode)
        {
            if (string.Equals(themeMode, AppSettings.ThemeDark, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(themeMode, AppSettings.ThemeLight, StringComparison.Ordinal))
            {
                return false;
            }

            // 跟随系统：开关初值必须跟界面实际生效的变体一致，否则深色系统下界面是深色、
            // 开关却停在浅色位。RequestedThemeVariant 是 Default 时，ActualThemeVariant 会回落到系统变体。
            return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        }

        // ── 文案（语言相关，Language 变更时全刷）──────────────────
        public string TitleText => "HanVGamepad";

        /// <summary>底部版本号：取程序集版本，避免手写版本号与构建脱节。</summary>
        public string VersionText => "HanVGamepad v" +
            (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        /// <summary>点版本号跳转的仓库地址（HyperlinkButton.NavigateUri 要 Uri，不写字符串）。</summary>
        public Uri RepoUrl { get; } = new("https://github.com/lanzeweie/HanVGamepad");

        public string StatusText => Phase switch
        {
            ServerPhase.Running => Strings.T(Language, "state.running"),
            ServerPhase.Starting => Strings.T(Language, "state.starting"),
            ServerPhase.Stopping => Strings.T(Language, "state.stopping"),
            _ => Strings.T(Language, "state.stopped")
        };

        public string StatusHintText => Phase == ServerPhase.Running
            ? Strings.T(Language, "uptime", FormatUptime())
            : Strings.T(Language, "state.stoppedHint");

        public string PowerButtonText => Phase switch
        {
            ServerPhase.Running => Strings.T(Language, "power.stop"),
            ServerPhase.Starting => Strings.T(Language, "power.starting"),
            ServerPhase.Stopping => Strings.T(Language, "power.stopping"),
            _ => Strings.T(Language, "power.start")
        };

        public bool CanTogglePower => Phase == ServerPhase.Stopped || Phase == ServerPhase.Running;

        public string AddrTitleText => Strings.T(Language, "addr.title");
        public string CopyAddressText => Strings.T(Language, "addr.copy");
        public string PortLabel => Strings.T(Language, "addr.port");
        public string PasswordLabel => Strings.T(Language, "addr.password");
        public string PasswordToggleText => PasswordVisible
            ? Strings.T(Language, "addr.hide")
            : Strings.T(Language, "addr.show");

        /// <summary>
        /// 地址卡拆两段显示：IP 用正文色，端口号单独着色（冒号不算在内）。
        /// 复制和二维码仍然用整串 AddressText，别改成只复制主机。
        /// </summary>
        public string AddressHostText => SplitAddress().Host;

        public string AddressPortText => SplitAddress().Port;

        private (string Host, string Port) SplitAddress()
        {
            int split = AddressText.LastIndexOf(':');
            return split < 0
                ? (AddressText, string.Empty)
                : (AddressText[..split], AddressText[(split + 1)..]);
        }

        public int OnlineCount => Slots.Count(s => s.Occupied);

        /// <summary>还能开几个虚拟手柄：上限减去真实手柄占用的部分。</summary>
        public int AvailableSlotCount => Math.Max(0, SlotCapacity - RealPadCount);

        /// <summary>被真实手柄顶掉的槽位数。</summary>
        public int ReservedSlotCount => Slots.Count(s => !s.IsAvailable);

        /// <summary>槽位密度由数量决定：4 个 2 列大卡，5–8 个 3 列中卡，9–16 个 4 列紧凑卡。</summary>
        public int SlotColumns => SlotCapacity <= 4 ? 2 : SlotCapacity <= 8 ? 3 : 4;

        /// <summary>3 列中密度。</summary>
        public bool IsSlotsMedium => SlotColumns == 3;

        /// <summary>4 列紧凑密度。</summary>
        public bool IsSlotsDense => SlotColumns == 4;

        /// <summary>
        /// 固定 4 路布局：Windows（XInput 4 槽）与 macOS（OJD 4 条 feed）都用这套。
        /// 只有它才显示「最多 4 个手柄」的说明。
        /// </summary>
        public bool IsFourPadLayout => !GamepadLimits.IsPortablePlatform;

        /// <summary>便携平台才有「新增手柄」入口（固定 4 路的平台加不了）。</summary>
        public bool IsPortableLayout => GamepadLimits.IsPortablePlatform;

        /// <summary>
        /// 还能不能再加槽位：便携平台、没到 16 上限、且服务没在跑。
        /// 槽位数在 StartServerAsync 时就定死了，运行中加会给客户端一个不存在的槽位。
        /// </summary>
        public bool CanAddSlot => GamepadLimits.IsPortablePlatform
            && SlotCapacity < GamepadLimits.PortableMaxPads
            && Phase == ServerPhase.Stopped;

        /// <summary>能不能删槽位：便携平台、至少留 1 个、且服务没在跑。</summary>
        public bool CanRemoveSlot => GamepadLimits.IsPortablePlatform
            && SlotCapacity > 1
            && Phase == ServerPhase.Stopped;

        public string AddSlotText => Strings.T(Language, "slot.add");

        public string RemoveSlotText => Strings.T(Language, "slot.remove");

        public string PadLimitText => Strings.T(Language, "slot.padLimit");

        /// <summary>伪装模式（Windows 上按便携平台跑），界面上要看得见，免得误以为是真机环境。</summary>
        public bool IsPortablePretend => GamepadLimits.PretendPortable;

        public string PretendText => Strings.T(Language, "platform.pretend");

        /// <summary>
        /// 槽位区徽章的分子：虚拟连接数 + 被真实手柄占掉的槽位。
        /// 真实手柄同样占 XInput 名额，算作 1 个在线；分母固定是平台上限（Windows 4）。
        /// </summary>
        public string SlotCountText =>
            Strings.T(Language, "slot.count", OnlineCount + ReservedSlotCount, SlotCapacity);

        public string SlotSectionText => Strings.T(Language, "slot.section");
        public string QrTitleText => Strings.T(Language, "qr.title");
        public string QrHintText => Strings.T(Language, "qr.hint.stop");

        public string ConfigTitleText => Strings.T(Language, "config.title");
        public string LangLabel => Strings.T(Language, "lang.label");

        public string DriverSectionText => Strings.T(Language, "driver.section");
        public string DriverStatusLabel => Strings.T(Language, "driver.status");
        public string DriverInstallText => Strings.T(
            Language, OperatingSystem.IsMacOS() ? "driver.installOjd" : "driver.install");
        public string DriverUninstallText => Strings.T(Language, "driver.uninstall");
        public string DriverRefreshText => Strings.T(Language, "driver.refresh");

        public bool IsStopped => Phase == ServerPhase.Stopped;
        public bool IsRunning => Phase == ServerPhase.Running;
        public bool IsNotRunning => !IsRunning;

        private string FormatUptime()
        {
            if (_startedAt == null)
            {
                return "00:00:00";
            }

            TimeSpan up = DateTimeOffset.Now - _startedAt.Value;
            return up.TotalHours >= 1
                ? $"{(int)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00}"
                : $"{up.Minutes:00}:{up.Seconds:00}";
        }

        private void RefreshTexts()
        {
            OnPropertyChanged(string.Empty);
            foreach (SlotViewModel slot in Slots)
            {
                slot.Language = Language;
                slot.RefreshTexts();
            }

            foreach (IpItemViewModel ip in Ips)
            {
                ip.Language = Language;
                ip.RefreshTexts();
            }
        }

        // ── 生命周期 ────────────────────────────────────────────
        /// <summary>窗口加载后初始化：刷新一拍 + 检测驱动状态。</summary>
        public async Task InitializeAsync()
        {
            Refresh();
            await RefreshDriverStatusAsync();
        }

        /// <summary>500ms 兜底刷新：事件漏了也能自愈，界面永远读最新快照。</summary>
        public void Refresh()
        {
            List<LocalAddress> allIps = NetworkHelper.GetAllLocalIPv4Addresses();

            // 地址栏与二维码。
            string address = allIps.Count > 0 ? $"{allIps[0].Ip}:{_settings.Port}" : string.Empty;
            AddressText = address;

            if (!string.Equals(address, _qrAddress, StringComparison.Ordinal))
            {
                Bitmap? previous = _qrImage;
                _qrImage = address.Length > 0 ? QrCodeService.Build(address) : null;
                _qrAddress = address;
                QrBitmap = _qrImage;
                previous?.Dispose();
            }

            // IP 列表。
            Ips.Clear();
            foreach (LocalAddress item in allIps)
            {
                Ips.Add(new IpItemViewModel
                {
                    Ip = item.Ip,
                    Kind = item.Kind,
                    Current = string.Equals(item.Ip, allIps.Count > 0 ? allIps[0].Ip : string.Empty, StringComparison.Ordinal),
                    Language = Language
                });
            }

            // 真实手柄先探一拍：它占掉的额度决定这一轮里哪些槽位还能用。
            ProbeExternalPads();
            int availableSlots = AvailableSlotCount;

            // 可用性单独遍历：服务没起来时 GetGamepadSnapshot() 是空数组，
            // 而「系统手柄占用」跟服务状态无关，照样得显示出来。
            for (int i = 0; i < Slots.Count; i++)
            {
                Slots[i].IsAvailable = Slots[i].Slot <= availableSlots;
            }

            // 槽位投影。
            IReadOnlyList<GamepadSnapshot> snapshot = GetGamepadSnapshot();
            for (int i = 0; i < Slots.Count && i < snapshot.Count; i++)
            {
                GamepadSnapshot item = snapshot[i];
                SlotViewModel vm = Slots[i];
                vm.Occupied = item.Occupied;
                vm.Type = item.ControllerType == nameof(TypeController.DualShock4) ? "ds4"
                    : string.IsNullOrEmpty(item.ControllerType) ? string.Empty : "xbox";
                vm.DeviceName = item.Occupied ? item.DeviceName ?? string.Empty : string.Empty;
                vm.RemoteIp = item.Occupied ? item.RemoteIp ?? string.Empty : string.Empty;
                vm.ControllerId = item.Occupied ? item.Slot : -1;
                vm.RefreshTexts();
            }

            RefreshTexts();
        }

        /// <summary>
        /// 探一次系统里的真实手柄（1 秒节流）。结果同时送给运行中的服务端，
        /// 让虚拟手柄额度跟着收；平台不支持探测时维持现状，不做扣减。
        /// </summary>
        private void ProbeExternalPads()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if ((now - _lastPadProbeAt).TotalMilliseconds < 1000)
            {
                return;
            }

            _lastPadProbeAt = now;

            int detected = _padProbe.CountXInputPads();
            if (detected < 0)
            {
                return;
            }

            // 服务运行期的额度扣减由 ServerRunner 自己的定时器做，这里只负责界面显示，
            // 两条路径各探各的、互不去抖，避免把「连续两拍确认」搅乱。
            int residentXbox = _server.GamepadManager?.ResidentXboxPadCount() ?? 0;
            RealPadCount = Math.Max(0, detected - residentXbox);
        }

        private IReadOnlyList<GamepadSnapshot> GetGamepadSnapshot()
        {
            return _server.GamepadManager?.GetSnapshot() ?? Array.Empty<GamepadSnapshot>();
        }

        private void HandlePlayerChanged(string connectionId, int controllerId)
        {
            Dispatcher.UIThread.Post(Refresh);
        }

        // ── 启停 ────────────────────────────────────────────────
        [RelayCommand]
        private async Task ToggleServerAsync()
        {
            if (Phase == ServerPhase.Running || Phase == ServerPhase.Stopping)
            {
                await StopServerCoreAsync();
            }
            else
            {
                await StartServerCoreAsync();
            }
        }

        private async Task StartServerCoreAsync()
        {
            if (Phase != ServerPhase.Stopped)
            {
                return;
            }

            if (_settings.Port <= 0 || _settings.Port > 65535)
            {
                ShowToast("toast.portInvalid", "error");
                return;
            }

            try
            {
                Phase = ServerPhase.Starting;
                RefreshTexts();

                // 驱动没装好就别启动：服务起来也建不了虚拟手柄，手机连上只会收到 device_create_failed。
                if (!await EnsureDriverReadyAsync())
                {
                    Phase = ServerPhase.Stopped;
                    RefreshTexts();
                    return;
                }

                await _server.StartServerAsync(_settings.Port, _settings.Password, SlotCapacity);

                // 自检：界面上的「运行中」只说明状态机走到了这一步，不代表服务真的能用。
                // 这里真的去连一次端口、取一次首页，把「假启动」变成会报错的事。
                ServerHealthCheck.Result health = await RunHealthCheckAsync();

                if (!health.Ok)
                {
                    ServerLog.Error($"启动自检未通过：{health.Message}");

                    // 自检没过就把服务收干净：只改状态机的话，端口仍被占着，
                    // 下一次启动就会撞 address already in use。
                    await StopServerQuietlyAsync();

                    Phase = ServerPhase.Stopped;
                    RefreshTexts();
                    ShowToast("toast.selfCheckFailed", "error", health.Message);
                    return;
                }

                ServerLog.Info("启动自检通过：端口在监听，首页可访问");

                _startedAt = DateTimeOffset.Now;
                Phase = ServerPhase.Running;
                RefreshTexts();
            }
            catch (Exception ex)
            {
                _startedAt = null;
                Phase = ServerPhase.Stopped;
                RefreshTexts();
                ServerLog.Error("启动服务失败", ex);
                ShowToast("toast.startFailed", "error", ex.Message);
            }
        }

        /// <summary>
        /// 启动后自检：端口是否真的在监听、首页是否真的能打开。
        /// 只探本机：局域网地址从本机连走的是本机栈，测不出防火墙，别给自己盖章。
        /// </summary>
        private async Task<ServerHealthCheck.Result> RunHealthCheckAsync()
        {
            // Check 内部是同步的 Wait(4s)：扔到线程池，别让窗口卡在「启动中」连一帧都画不出来。
            ServerHealthCheck.Result health = await Task.Run(() => ServerHealthCheck.Check(_settings.Port));

            if (!health.Ok)
            {
                return health;
            }

            ServerHealthCheck.Result homepage = await ServerHealthCheck.CheckHomepageAsync(_settings.Port, "127.0.0.1");
            if (!homepage.Ok)
            {
                return new ServerHealthCheck.Result(false, "首页无法访问：" + homepage.Message, homepage.StatusCode);
            }

            return health;
        }

        /// <summary>
        /// 启动前的驱动门槛：固定 4 路的平台都得先有「虚拟手柄发布层」——
        /// Windows 是 ViGEmBus，macOS 是 OpenJoystickDriver。便携平台用内核自带的 uinput，不拦启动。
        /// 返回 false = 别启动（用户取消 / 处理完仍不可用）。
        /// </summary>
        private async Task<bool> EnsureDriverReadyAsync()
        {
            if (!CanInstallDriver)
            {
                return true;
            }

            DriverStatus status = await _driverManager.GetStatusAsync();
            if (status.IsInstalled && status.Permission == DriverPermissionState.Ok)
            {
                return true;
            }

            // Windows：未安装问装不装，装了但不可用（服务没起 / 权限不足）问要不要重装。
            // macOS：未安装打开 OJD 下载页，装了但服务没跑就代为拉起它的 service —— 措辞必须跟着动作变。
            bool macOS = OperatingSystem.IsMacOS();

            string titleKey = macOS ? "dialog.ojdRequired.title" : "dialog.driverRequired.title";

            string messageKey = macOS
                ? (status.IsInstalled ? "dialog.ojdNotRunning.text" : "dialog.ojdRequired.text")
                : (status.IsInstalled ? "dialog.driverUnusable.text" : "dialog.driverRequired.text");

            string actionKey = macOS
                ? (status.IsInstalled ? "dialog.ojd.action.start" : "dialog.ojd.action.install")
                : "dialog.driverRequired.install";

            bool proceed = await HanVGamepad.Desktop.Views.ConfirmDialog.ShowAsync(
                GetMainWindow(),
                Strings.T(Language, titleKey),
                Strings.T(Language, messageKey),
                Strings.T(Language, actionKey),
                Strings.T(Language, "dialog.driverRequired.cancel"));

            if (!proceed)
            {
                return false;
            }

            await InstallDriverAsync();
            await RefreshDriverStatusAsync();

            DriverStatus after = await _driverManager.GetStatusAsync();
            return after.IsInstalled && after.Permission == DriverPermissionState.Ok;
        }

        private static Avalonia.Controls.Window? GetMainWindow()
        {
            return (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
        }

        private async Task StopServerCoreAsync()
        {
            if (Phase != ServerPhase.Running)
            {
                return;
            }

            try
            {
                Phase = ServerPhase.Stopping;
                RefreshTexts();
                await _server.StopServerAsync();
            }
            catch (Exception ex)
            {
                ShowToast("toast.stopFailed", "error", ex.Message);
            }
            finally
            {
                _startedAt = null;
                Phase = ServerPhase.Stopped;
                RefreshTexts();
            }
        }

        /// <summary>启动失败/自检失败时的收尾：把服务停掉，释放端口。不再往外抛。</summary>
        private async Task StopServerQuietlyAsync()
        {
            try
            {
                await _server.StopServerAsync();
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"清理失败的服务时出错：{ex.Message}");
            }
        }

        // ── 地址与设置 ──────────────────────────────────────────
        [RelayCommand]
        private void CopyAddress()
        {
            if (AddressText.Length == 0)
            {
                ShowToast("toast.noIp", "warn");
                return;
            }

            try
            {
                // Clipboard 挂在 TopLevel（窗口）上，从桌面生命周期拿主窗口。
                var clipboard = (Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                    ?.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    clipboard.SetTextAsync(AddressText);
                }

                ShowToast("toast.copied", "success", AddressText);
            }
            catch (Exception ex)
            {
                ShowToast("toast.copyFailed", "error", ex.Message);
            }
        }

        /// <summary>端口/密码「失焦即提交」：只在停止态生效（运行态输入框由界面锁成只读）。</summary>
        [RelayCommand]
        private void CommitCredentials()
        {
            if (Phase != ServerPhase.Stopped)
            {
                ShowToast("toast.settingsLocked", "warn");
                Refresh();
                return;
            }

            if (Port <= 0 || Port > 65535)
            {
                ShowToast("toast.portInvalid", "error");
                Refresh();
                return;
            }

            _settings.Port = Port;
            _settings.Password = Password ?? string.Empty;
            _server.UpdateServerPassword(_settings.Password);

            PersistSettings();
            Refresh();
            ShowToast("toast.settingsSaved", "success");
        }

        [RelayCommand]
        private void TogglePasswordVisible()
        {
            PasswordVisible = !PasswordVisible;
            RefreshTexts();
        }

        /// <summary>语言切换（View 的下拉选择后调用）。</summary>
        public void ChangeLanguage(string language)
        {
            if (!Languages.All.Contains(language) ||
                string.Equals(language, _settings.Language, StringComparison.Ordinal))
            {
                return;
            }

            _settings.Language = language;
            Language = language;
            PersistSettings();
            RefreshTexts();

            // 驱动卡片的状态胶囊和详情行存的是「已经翻译好的字符串」，不是 getter：只刷绑定会让它们
            // 停在上一种语言。语言变了就重新取一次驱动状态，让文案跟着换；异常在方法内部已吞掉。
            _ = RefreshDriverStatusAsync();
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            IsDark = !IsDark;
        }

        /// <summary>
        /// 跟随系统（没手动选过）时，把开关位置对齐到界面实际生效的变体。
        /// 平台主题要等窗口起来才解析得出来，所以在 Opened 之后调用，而不是构造时算死。
        /// </summary>
        public void SyncThemeWithSystem()
        {
            if (!string.IsNullOrEmpty(_settings.ThemeMode))
            {
                return;
            }

            bool dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            if (dark == IsDark)
            {
                return;
            }

            _suppressThemeWrite = true;
            try
            {
                IsDark = dark;
            }
            finally
            {
                _suppressThemeWrite = false;
            }
        }

        /// <summary>
        /// 主题开关的唯一切换入口：底部开关与 IsDark 双向绑定，
        /// 所以落盘、通知应用层换变体、刷新文案都收在这里，两条路径不重复写。
        /// </summary>
        partial void OnIsDarkChanged(bool value)
        {
            // 跟随系统回填：界面已经是这个变体，不该顺手把手动选择写死进设置。
            if (_suppressThemeWrite)
            {
                return;
            }

            _settings.ThemeMode = value ? AppSettings.ThemeDark : AppSettings.ThemeLight;
            ThemeChangeRequested?.Invoke(value);
            PersistSettings();
            RefreshTexts();
        }

        private void PersistSettings()
        {
            if (!_settings.Save())
            {
                ShowToast("toast.saveFailed", "error", AppSettings.SettingsPath);
            }
        }

        // ── 槽位命令 ────────────────────────────────────────────
        /// <summary>
        /// 便携平台加一个槽位（最多 16）。只在停止态可用：槽位数在 StartServerAsync 时定死，
        /// 运行中扩槽会让客户端拿到一个服务端根本不存在的槽号。
        /// </summary>
        [RelayCommand]
        private void AddSlot()
        {
            if (!CanAddSlot)
            {
                return;
            }

            SlotCapacity++;
            Slots.Add(new SlotViewModel { Slot = SlotCapacity, Language = Language });

            PersistSettings();

            Refresh();
        }

        /// <summary>
        /// 便携平台删掉最后一个槽位（最少留 1 个）。空槽也能删：删的是槽位，不是设备。
        /// 服务停止时虚拟设备已随 GamepadManager 一起销毁，所以不存在「设备还挂着」的问题。
        /// </summary>
        [RelayCommand]
        private void RemoveSlot()
        {
            if (!CanRemoveSlot)
            {
                return;
            }

            Slots.RemoveAt(Slots.Count - 1);
            SlotCapacity--;

            PersistSettings();

            Refresh();
        }

        [RelayCommand]
        private void DisconnectSlot(int slot)
        {
            if (slot < 1 || slot > SlotCapacity)
            {
                ShowToast("toast.slotInvalid", "warn");
                return;
            }

            GamepadManager? gamepadManager = _server.GamepadManager;
            if (gamepadManager == null)
            {
                ShowToast("toast.notRunning", "warn");
                return;
            }

            GamepadSnapshot? target = null;
            foreach (GamepadSnapshot item in gamepadManager.GetSnapshot())
            {
                if (item.Occupied && item.Slot == slot - 1)
                {
                    target = item;
                    break;
                }
            }

            if (target == null || string.IsNullOrEmpty(target.ConnectionId))
            {
                ShowToast("toast.noDevice", "warn", slot);
                return;
            }

            // 走 Kick：先给手机发「你被管理员踢出」的稳定码，再关会话。
            // 直接 EndConnection 的话，手机只看到数据通道关闭，分不清是被踢还是掉线。
            if (_server.Kick(target.ConnectionId, KickReason))
            {
                ShowToast("toast.kickOk", "success", slot);
                Refresh();
                return;
            }

            // RTC 管理器还没起来（服务刚停）：退回到只释放槽位。
            gamepadManager.DisconnectGamepad(target.ConnectionId);
            Refresh();
        }

        /// <summary>
        /// 卸载槽位上的常驻设备。只在空闲时允许：手机还连着就先断开连接再删 ——
        /// 删设备等于把它从系统里拔掉，正在玩的手柄会当场消失。
        /// </summary>
        [RelayCommand]
        private void RemoveDevice(int slot)
        {
            if (slot < 1 || slot > SlotCapacity)
            {
                ShowToast("toast.slotInvalid", "warn");
                return;
            }

            GamepadManager? gamepadManager = _server.GamepadManager;
            if (gamepadManager == null)
            {
                ShowToast("toast.notRunning", "warn");
                return;
            }

            if (gamepadManager.RemoveDevice(slot - 1))
            {
                ShowToast("toast.deviceRemoved", "success", slot);
            }
            else
            {
                // 界面画完之后有人抢了这个槽：设备没删，提示先断开。
                ShowToast("toast.slotBusy", "warn", slot);
            }

            Refresh();
        }

        // ── 驱动管理 ────────────────────────────────────────────
        /// <summary>
        /// 安装/卸载 ViGEmBus 的入口，只有 Windows 需要。
        /// 便携平台用的是内核自带的 uinput，没有可安装的东西，只需要检测它当前能不能用。
        /// </summary>
        public bool CanManageDriver => OperatingSystem.IsWindows();

        /// <summary>
        /// 能执行「安装」动作的平台：Windows 装 ViGEmBus；macOS 只能引导安装 OpenJoystickDriver
        /// （macOS 的虚拟 HID 需要 Apple 受限 entitlement，第三方程序不能自带）。
        /// </summary>
        public bool CanInstallDriver => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        [RelayCommand]
        private async Task RefreshDriverStatusAsync()
        {
            try
            {
                DriverStatus status = await _driverManager.GetStatusAsync();

                DriverStatusText = CanManageDriver
                    ? (status.IsInstalled
                        ? Strings.T(Language, "driver.installed")
                        : Strings.T(Language, "driver.notInstalled"))
                    : (status.IsInstalled
                        ? Strings.T(Language, "driver.available")
                        : Strings.T(Language, "driver.unavailable"));

                string serviceText = status.IsServiceRunning
                    ? Strings.T(Language, "driver.serviceRunning")
                    : Strings.T(Language, "driver.serviceStopped");

                // 未安装时不要再拼「需要管理员权限」这类只有装好的驱动才有意义的细节。
                string permissionText = !status.IsInstalled
                    ? string.Empty
                    : status.Permission switch
                    {
                        DriverPermissionState.Ok => Strings.T(Language, "driver.permissionOk"),
                        DriverPermissionState.NeedsElevation => Strings.T(Language, "driver.needsElevation"),
                        DriverPermissionState.PermissionDenied => Strings.T(Language, "driver.permissionDenied"),
                        DriverPermissionState.SignatureBlocked => Strings.T(Language, "driver.signatureBlocked"),
                        DriverPermissionState.KernelModuleNotLoaded => Strings.T(Language, "driver.moduleNotLoaded"),
                        _ => status.Permission.ToString()
                    };

                string versionText = status.Version != null
                    ? Strings.T(Language, "driver.version", status.Version)
                    : string.Empty;

                DriverDetailText = status.IsInstalled
                    ? string.Join(" · ", new[] { serviceText, permissionText, versionText }
                        .Where(s => s.Length > 0))
                    : string.Empty;

                DriverDiagnostic = status.DiagnosticMessage ?? string.Empty;
            }
            catch (Exception ex)
            {
                DriverStatusText = Strings.T(Language, "driver.notInstalled");
                DriverDetailText = string.Empty;
                DriverDiagnostic = ex.Message;
            }

            RefreshTexts();
        }

        [RelayCommand]
        private async Task InstallDriverAsync()
        {
            if (DriverBusy)
            {
                return;
            }

            DriverBusy = true;
            try
            {
                var progress = new Progress<string>(message =>
                    Dispatcher.UIThread.Post(() => DriverDiagnostic = message));

                DriverInstallResult result = await _driverManager.InstallAsync(progress);

                if (result.Success)
                {
                    ShowToast("driver.installOk", "success");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    // macOS 的「安装」只是打开 OJD 下载页或拉起它的服务，没就绪不等于出错。
                    ShowToast("driver.ojdNotReady", "warn");
                }
                else
                {
                    ShowToast("driver.installFailed", "error", result.ErrorMessage ?? "");
                }

                DriverDiagnostic = result.ErrorMessage ?? DriverDiagnostic;
            }
            catch (Exception ex)
            {
                ShowToast("driver.installFailed", "error", ex.Message);
            }
            finally
            {
                DriverBusy = false;
                await RefreshDriverStatusAsync();
            }
        }

        [RelayCommand]
        private async Task UninstallDriverAsync()
        {
            if (DriverBusy)
            {
                return;
            }

            DriverBusy = true;
            try
            {
                // 每一步都写进诊断行：否则停服 + 等 UAC + 等注册表这十几秒里界面毫无动静，
                // 用户只会以为「点了没反应」。
                DriverDiagnostic = "Stopping the server before uninstalling...";
                RefreshTexts();

                // 卸载前先把服务停掉：虚拟手柄还挂在驱动上时，卸载器删不掉驱动文件。
                if (Phase != ServerPhase.Stopped)
                {
                    await StopServerCoreAsync();
                }

                DriverDiagnostic = "Running the registered ViGEmBus uninstaller (confirm the UAC prompt)...";
                RefreshTexts();

                DriverUninstallResult result = await _driverManager.UninstallAsync();

                if (result.Success)
                {
                    ShowToast(result.RequiresReboot ? "driver.uninstallReboot" : "driver.uninstallOk", "success");
                    DriverDiagnostic = result.ErrorMessage ?? "Driver uninstalled.";
                }
                else
                {
                    ShowToast("driver.uninstallFailed", "error", result.ErrorMessage ?? "");
                    DriverDiagnostic = result.ErrorMessage ?? DriverDiagnostic;
                }
            }
            catch (Exception ex)
            {
                ShowToast("driver.uninstallFailed", "error", ex.Message);
            }
            finally
            {
                DriverBusy = false;
                await RefreshDriverStatusAsync();
            }
        }

        // ── toast ───────────────────────────────────────────────
        private void ShowToast(string key, string level, params object[] args)
        {
            ToastMessage = Strings.T(Language, key, args);
            ToastLevel = level;
            IsToastOpen = true;
            ToastShown?.Invoke();
        }

        /// <summary>退出前释放二维码位图。</summary>
        public void Shutdown()
        {
            Bitmap? qr = _qrImage;
            _qrImage = null;
            QrBitmap = null;
            qr?.Dispose();
        }
    }

    /// <summary>IP 列表条目展示。</summary>
    public sealed partial class IpItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _ip = string.Empty;

        [ObservableProperty]
        private string _kind = "other";

        [ObservableProperty]
        private bool _current;

        [ObservableProperty]
        private string _language = Languages.Chinese;

        public string Label => Strings.IpLabel(Language, Kind);

        public void RefreshTexts() => OnPropertyChanged(string.Empty);
    }
}



