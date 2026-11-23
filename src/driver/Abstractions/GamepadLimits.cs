namespace HanVGamepad.Driver.Abstractions
{
    /// <summary>
    /// 手柄槽位上限。
    /// Windows 走 ViGEmBus，系统侧只有 4 个 XInput 用户槽（0–3），超过就是建得出来也读不到；
    /// Linux 走 uinput，没有这个硬限制，按产品需要定软上限；
    /// macOS 走 OpenJoystickDriver 的 feed，对方最多同时发布 4 台虚拟手柄，同样是硬上限。
    /// </summary>
    public static class GamepadLimits
    {
        /// <summary>Windows：受 XInput 用户槽限制，写死 4。</summary>
        public const int WindowsMaxPads = 4;

        /// <summary>macOS：OpenJoystickDriver 的 feed 上限是 4 条，对应 4 台虚拟手柄。</summary>
        public const int MacMaxPads = 4;

        /// <summary>便携平台起步槽位数：跟 Windows 一样先给 4 个，用户再按需加。</summary>
        public const int PortableDefaultPads = 4;

        /// <summary>便携平台的硬上限：不占 XInput 槽，按 UI 密度（4 列 × 4 行）定。</summary>
        public const int PortableMaxPads = 16;

        /// <summary>
        /// 测试开关：把本进程伪装成便携平台（16 槽 + 紧凑 UI），用于在 Windows 上验证那一套布局。
        /// 只影响容量、界面与真实手柄扣减，不改变驱动后端 —— Windows 上照样只有 4 个 XInput 槽，
        /// 第 5 个虚拟手柄会创建失败，这是预期行为，不是 bug。
        /// </summary>
        public static bool PretendPortable { get; private set; }

        /// <summary>
        /// 当前进程是否按便携平台的布局跑（Linux 或伪装模式）。
        /// macOS 走固定 4 路，和 Windows 一样用大卡布局，所以不算便携平台。
        /// </summary>
        public static bool IsPortablePlatform => PretendPortable || OperatingSystem.IsLinux();

        /// <summary>当前平台的槽位上限。</summary>
        public static int ForCurrentPlatform =>
            IsPortablePlatform ? PortableMaxPads
            : OperatingSystem.IsMacOS() ? MacMaxPads
            : WindowsMaxPads;

        /// <summary>
        /// 解析启动参数里的伪装开关。支持 <c>--pretend-platform=linux|mac|portable</c>，
        /// 以及不带值的 <c>--pretend-platform</c>（等价于 portable）。
        /// </summary>
        public static void ApplyStartupOverride(IEnumerable<string> args)
        {
            const string prefix = "--pretend-platform";

            foreach (string arg in args)
            {
                if (arg.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = arg[(prefix.Length + 1)..].Trim();
                    if (value.Equals("linux", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("mac", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("macos", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("portable", StringComparison.OrdinalIgnoreCase))
                    {
                        PretendPortable = true;
                    }
                }
                else if (string.Equals(arg, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    PretendPortable = true;
                }
            }
        }
    }
}
