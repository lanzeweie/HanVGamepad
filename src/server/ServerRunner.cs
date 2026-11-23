using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using HanVGamepad.Driver.Abstractions;
using HanVGamepad.Server.Hubs;
using HanVGamepad.Server.Logging;
using HanVGamepad.Server.Session;
using HanVGamepad.Server.Slots;

namespace HanVGamepad.Server
{
    /// <summary>
    /// ASP.NET Core 服务宿主：Kestrel HTTP + SignalR + 静态资源（React SPA）。
    /// 通过 IDriverManager 获取平台后端（Windows ViGEmBus / Linux uinput）。
    /// </summary>
    public class ServerRunner
    {
        private readonly IDriverManager _driverManager;
        private readonly IGamepadProbe? _padProbe;
        private WebApplication? _app;

        // 服务运行期每秒探一次真实手柄：真实手柄占了槽位，虚拟手柄额度就得跟着收。
        private Timer? _padProbeTimer;

        /// <summary>玩家拿到槽位号（供界面即时刷新）。</summary>
        public event Action<string, int>? OnControllerIdAssigned;

        /// <summary>玩家断开（供界面即时刷新）。</summary>
        public event Action<string>? OnPlayerDisconnected;

        public ServerRunner(IDriverManager driverManager, IGamepadProbe? padProbe = null)
        {
            _driverManager = driverManager;
            _padProbe = padProbe;
        }

        public GamepadManager? GamepadManager => _app?.Services.GetRequiredService<GamepadManager>();
        public WebRTCSessionManager? WebRTCSessionManager => _app?.Services.GetRequiredService<WebRTCSessionManager>();

        public async Task StartServerAsync(int port = 5000, string password = "", int? slotCapacity = null)
        {
            ServerLog.Info($"正在启动服务：端口 {port}{(string.IsNullOrEmpty(password) ? "" : "，已设置密码")}");

            try
            {
                // 驱动未就绪就别开服务：手机连上来也建不了手柄。
                IGamepadBackend? backend = _driverManager.CreateBackend();
                if (backend == null)
                {
                    throw new InvalidOperationException(
                        "The gamepad driver is not ready. Install / configure it first (see the driver management view).");
                }

                // 密码在服务启动前就位，第一秒就是安全的。
                GamepadHub.SetPassword(password);

                // 内容根固定为程序所在目录：wwwroot 就在它下面，
                // 不能让内容根落到 dotnet run 的当前目录（那会让静态资源找错地方）。
                string wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                bool hasWwwroot = Directory.Exists(wwwrootPath);

                var options = new WebApplicationOptions
                {
                    ContentRootPath = AppContext.BaseDirectory,
                    WebRootPath = hasWwwroot ? wwwrootPath : null
                };

                var builder = WebApplication.CreateBuilder(options);

                // 槽位数在服务启动时就定死：便携平台允许用户加到 16，Windows 固定 4。
                int capacity = slotCapacity ?? GamepadLimits.ForCurrentPlatform;

                builder.Services.AddSignalR();
                builder.Services.AddSingleton(backend);
                builder.Services.AddSingleton(sp => new GamepadManager(sp.GetRequiredService<IGamepadBackend>(), capacity));
                builder.Services.AddSingleton<WebRTCSessionManager>();

                if (hasWwwroot)
                {
                    // WebRootPath 必须在 WebApplicationOptions 里给：静态文件中间件用的
                    // WebRootFileProvider 是按内容根算出来的，事后改字符串不会重建 provider，首页照样 404。
                    ServerLog.Info($"静态资源目录：{wwwrootPath}");
                }
                else
                {
                    // 没有 wwwroot 时页面会是 404 —— 这正是「假启动」最常见的一种。
                    ServerLog.Warn($"找不到静态资源目录：{wwwrootPath}（网页将无法打开，需先执行 npm run build）");
                }

                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy =>
                    {
                        policy.AllowAnyHeader()
                              .AllowAnyMethod()
                              .SetIsOriginAllowed(_ => true)
                              .AllowCredentials();
                    });
                });

                builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

                _app = builder.Build();

                _app.UseCors();

                // 默认文档（index.html）与静态资源（JS/CSS/图片）。
                _app.UseDefaultFiles();
                _app.UseStaticFiles();

                // SignalR Hub。
                _app.MapHub<GamepadHub>("/gamepadhub");

                // SPA 路由兜底：任意未知路径回 index.html，避免前端路由 404。
                _app.MapFallbackToFile("index.html");

                var hubContext = _app.Services.GetRequiredService<IHubContext<GamepadHub>>();
                var rtcManager = _app.Services.GetRequiredService<WebRTCSessionManager>();
                var gamepadManager = _app.Services.GetRequiredService<GamepadManager>();

                // 事件桥接：业务层 → 宿主（界面 / CLI）。
                gamepadManager.OnControllerIdAssigned += (connId, id) => OnControllerIdAssigned?.Invoke(connId, id);
                rtcManager.OnPlayerDisconnected += (connId) => OnPlayerDisconnected?.Invoke(connId);

                // 槽位任何变化（占用 / 释放 / 设备类型）都广播给所有手机端，选槽界面靠这个刷新。
                gamepadManager.OnSlotsChanged += () => BroadcastSlots(hubContext, gamepadManager.GetSlotInfos());

                rtcManager.OnAnswerReady += async (connectionId, sdpAnswer) =>
                {
                    await hubContext.Clients.Client(connectionId).SendAsync("ReceiveAnswer", sdpAnswer);
                };

                rtcManager.OnIceCandidateReady += async (connectionId, iceCandidate) =>
                {
                    await hubContext.Clients.Client(connectionId).SendAsync("ReceiveIceCandidate", iceCandidate);
                };

                await _app.StartAsync();

                // 服务在跑才需要探：没有虚拟设备的时候扣减没有意义。
                StartPadProbe();

                ServerLog.Info($"服务已启动，正在监听 http://0.0.0.0:{port}");
            }
            catch (Exception ex)
            {
                ServerLog.Error($"服务启动失败：{ex.Message}", ex);

                // 失败也要把半成品 host 收掉：留着 _app != null，属性会继续返回死容器里的实例，
                // 界面按「有服务」渲染，下一次启动还会把旧 host 直接覆盖漏掉。
                await CleanupFailedAppAsync();
                throw;
            }
        }

        private async Task CleanupFailedAppAsync()
        {
            StopPadProbe();

            if (_app == null)
            {
                return;
            }

            try
            {
                await _app.DisposeAsync();
            }
            catch
            {
                // 收尾失败就算了，进程还在，下一次启动会重新建。
            }

            _app = null;
        }

        public void UpdateServerPassword(string newPassword)
        {
            GamepadHub.SetPassword(newPassword);
        }

        private void StartPadProbe()
        {
            if (_padProbe == null || _padProbeTimer != null)
            {
                return;
            }

            _padProbeTimer = new Timer(_ => ProbePads(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        private void StopPadProbe()
        {
            _padProbeTimer?.Dispose();
            _padProbeTimer = null;
        }

        private void ProbePads()
        {
            try
            {
                int count = _padProbe!.CountXInputPads();
                GamepadManager?.UpdateFromProbe(count);
            }
            catch
            {
                // 探测失败不影响服务本身，下一秒再来。
            }
        }

        /// <summary>
        /// 踢出：先把稳定码发给手机端，再断开 WebRTC 并释放槽位。
        /// SignalR 连接不动，客户端收到 Kicked 自己收尾（否则它只会看到「掉线」）。
        /// </summary>
        public bool Kick(string connectionId, string reason)
        {
            if (string.IsNullOrEmpty(connectionId))
            {
                return false;
            }

            WebRTCSessionManager? rtc = WebRTCSessionManager;
            if (rtc == null)
            {
                return false;
            }

            IHubContext<GamepadHub>? hubContext = _app?.Services.GetService<IHubContext<GamepadHub>>();
            if (hubContext != null)
            {
                SendKicked(hubContext, connectionId, reason);
            }

            rtc.EndConnection(connectionId);
            return true;
        }

        private static async void SendKicked(IHubContext<GamepadHub> hubContext, string connectionId, string reason)
        {
            try
            {
                await hubContext.Clients.Client(connectionId).SendAsync("Kicked", reason);
            }
            catch
            {
                // 客户端已经走了：断开本身仍然有效。
            }
        }

        private static async void BroadcastSlots(IHubContext<GamepadHub> hubContext, IReadOnlyList<SlotInfo> slots)
        {
            try
            {
                await hubContext.Clients.All.SendAsync("SlotChanged", slots);
            }
            catch
            {
                // 广播失败不影响槽位状态本身。
            }
        }

        public async Task StopServerAsync()
        {
            if (_app == null)
            {
                ServerLog.Info("停止服务：当前没有运行中的服务");
                return;
            }

            try
            {
                ServerLog.Info("正在停止服务…");

                // 先停探测：容器拆掉后 GamepadManager 就没了。
                StopPadProbe();

                // 先收会话：容器 Dispose 会连 GamepadManager 和虚拟设备一起拆掉，
                // 还活着的数据通道再收到输入就是打在已释放的设备上。
                _app.Services.GetService<WebRTCSessionManager>()?.CloseAll();

                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
                ServerLog.Info("服务已停止");
            }
            catch (Exception ex)
            {
                ServerLog.Error("停止服务时出错", ex);
                await CleanupFailedAppAsync();
                throw;
            }
        }
    }
}
