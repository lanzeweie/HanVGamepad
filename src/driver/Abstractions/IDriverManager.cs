namespace HanVGamepad.Driver.Abstractions
{
    /// <summary>驱动权限 / 环境状态。</summary>
    public enum DriverPermissionState
    {
        Ok,

        /// <summary>Windows: 需要 UAC 提权；Linux: 需要 sudo 或 udev rule。</summary>
        NeedsElevation,

        /// <summary>无法取得权限。</summary>
        PermissionDenied,

        /// <summary>Windows: 驱动签名被组策略 / UEFI Secure Boot 拦截。</summary>
        SignatureBlocked,

        /// <summary>Linux: uinput 内核模块未加载。</summary>
        KernelModuleNotLoaded,

        Unknown
    }

    /// <summary>驱动状态快照。GetStatusAsync 绝不抛异常，错误以 DiagnosticMessage 返回。</summary>
    public record DriverStatus(
        bool IsInstalled,
        bool IsServiceRunning,
        string? Version,
        DriverPermissionState Permission,
        string? DiagnosticMessage);

    public record DriverInstallResult(bool Success, bool RequiresReboot, string? ErrorMessage);

    /// <summary>
    /// 卸载结果。RequiresReboot 用于「驱动文件已删、但服务注册项要重启才消失」的情况：
    /// 这时功能上已经卸载成功，不该报成失败让用户去控制面板手动折腾。
    /// </summary>
    public record DriverUninstallResult(bool Success, string? ErrorMessage, bool RequiresReboot = false);

    /// <summary>
    /// 驱动管理器：负责检测、安装、卸载底层驱动。
    /// 实现：WindowsDriverManager（ViGEmBus）、LinuxDriverManager（uinput 权限配置）。
    /// </summary>
    public interface IDriverManager
    {
        /// <summary>检测当前驱动状态（不修改系统）。</summary>
        Task<DriverStatus> GetStatusAsync(CancellationToken ct = default);

        /// <summary>执行安装 / 配置。需要提权时自动处理（UAC / 引导用户 sudo）。progress 可空。</summary>
        Task<DriverInstallResult> InstallAsync(IProgress<string>? progress, CancellationToken ct = default);

        /// <summary>执行卸载 / 清理。</summary>
        Task<DriverUninstallResult> UninstallAsync(CancellationToken ct = default);

        /// <summary>
        /// 安装后初始化后端。返回 null 表示驱动未就绪（调用方须先 InstallAsync 成功）。
        /// </summary>
        IGamepadBackend? CreateBackend();
    }
}
