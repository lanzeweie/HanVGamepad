using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver
{
    /// <summary>
    /// 驱动层入口：按当前操作系统创建对应的 IDriverManager。
    /// Windows 实现仅在 Windows 构建中编入（见 HanVGamepad.Driver.csproj 的条件编译）。
    /// macOS 没有自带的内核虚拟手柄接口，后端桥接到 OpenJoystickDriver（见 MacOS 目录）。
    /// </summary>
    public static class DriverServices
    {
        public static IDriverManager CreateDriverManager()
        {
            if (OperatingSystem.IsWindows())
            {
#if VGP_WINDOWS_IMPL
                return new Windows.WindowsDriverManager();
#else
                throw new PlatformNotSupportedException(
                    "This build was compiled without the Windows driver backend. Build on Windows to get ViGEmBus support.");
#endif
            }

            if (OperatingSystem.IsMacOS())
            {
                return new MacOS.MacDriverManager();
            }

            return new Linux.LinuxDriverManager();
        }

        /// <summary>当前平台是否受支持（Windows / Linux / macOS）。</summary>
        public static bool IsPlatformSupported =>
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

        /// <summary>
        /// 真实手柄探针。Windows 用 XInput 查 4 个用户槽；Linux / macOS 返回 -1（未知），
        /// 也就是不做虚拟手柄扣减 —— 没有 XInput 这套固定槽位，扣减也就没有依据。
        /// </summary>
        public static IGamepadProbe CreateGamepadProbe()
        {
            // 便携平台（含伪装模式）没有 XInput 槽位概念，不做真实手柄扣减。
            if (GamepadLimits.IsPortablePlatform)
            {
                return UnknownGamepadProbe.Instance;
            }

            if (OperatingSystem.IsWindows())
            {
#if VGP_WINDOWS_IMPL
                return new Windows.XInputGamepadProbe();
#else
                return UnknownGamepadProbe.Instance;
#endif
            }

            return UnknownGamepadProbe.Instance;
        }
    }

    /// <summary>不支持检测的平台：永远返回 -1，调用方按「未知」处理。</summary>
    internal sealed class UnknownGamepadProbe : IGamepadProbe
    {
        public static readonly UnknownGamepadProbe Instance = new UnknownGamepadProbe();

        private UnknownGamepadProbe()
        {
        }

        public int CountXInputPads() => -1;
    }
}
