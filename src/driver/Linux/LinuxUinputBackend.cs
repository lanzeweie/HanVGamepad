using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using HanVGamepad.Driver.Abstractions;
using static HanVGamepad.Driver.Linux.LinuxNative;

namespace HanVGamepad.Driver.Linux
{
    /// <summary>
    /// Linux 虚拟手柄后端：通过 /dev/uinput 创建 Xbox 360 兼容（xpad evdev 格式）虚拟设备。
    /// 设备 VID/PID = 0x045E/0x028E，确保 SDL、Wine/Proton、Steam Input 自动识别。
    /// DualShock4 请求降级为 Xbox 360 映射（真实 DS4 仿真需要 HID 层，超出本项目范围）。
    /// </summary>
    internal sealed class LinuxUinputBackend : IGamepadBackend
    {
        private const ushort VendorId = 0x045E;   // Microsoft
        private const ushort ProductId = 0x028E;  // Xbox 360 Controller
        private const string DeviceName = "HanVGamepad Xbox 360 Controller";

        private readonly object _gate = new();
        private bool _disposed;

        public IVirtualPad CreatePad(VirtualPadType type)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LinuxUinputBackend));
                }

                // DualShock4 在 Linux 端降级：始终创建 Xbox 360 格式设备。
                return CreateXbox360Pad();
            }
        }

        private static IVirtualPad CreateXbox360Pad()
        {
            int fd = Open(UinputDevicePath, O_WRONLY | O_NONBLOCK);
            if (fd < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                throw errno switch
                {
                    13 => new UnauthorizedAccessException(
                        "Permission denied opening /dev/uinput. Add your user to the 'input' group or configure the udev rule (see docs/driver-setup/linux-uinput.md)."),
                    2 => new FileNotFoundException(
                        "/dev/uinput does not exist. Load the kernel module first: sudo modprobe uinput"),
                    _ => new Win32Exception(errno, $"Failed to open {UinputDevicePath} (errno {errno}).")
                };
            }

            try
            {
                // 1. 注册事件类型。
                IoctlCheck(fd, UI_SET_EVBIT, EV_KEY, "UI_SET_EVBIT(EV_KEY)");
                IoctlCheck(fd, UI_SET_EVBIT, EV_ABS, "UI_SET_EVBIT(EV_ABS)");

                // 2. 注册按键（xpad 布局）。
                ushort[] keys =
                {
                    BTN_SOUTH, BTN_EAST, BTN_NORTH, BTN_WEST,
                    BTN_TL, BTN_TR, BTN_SELECT, BTN_START, BTN_MODE,
                    BTN_THUMBL, BTN_THUMBR
                };
                foreach (ushort key in keys)
                {
                    IoctlCheck(fd, UI_SET_KEYBIT, key, $"UI_SET_KEYBIT({key})");
                }

                // 3. 注册轴并设置范围（摇杆 -32768..32767 fuzz=16 flat=128；扳机 0..255；hat -1..1）。
                SetupAxis(fd, ABS_X, -32768, 32767, 16, 128);
                SetupAxis(fd, ABS_Y, -32768, 32767, 16, 128);
                SetupAxis(fd, ABS_RX, -32768, 32767, 16, 128);
                SetupAxis(fd, ABS_RY, -32768, 32767, 16, 128);
                SetupAxis(fd, ABS_Z, 0, 255, 0, 0);
                SetupAxis(fd, ABS_RZ, 0, 255, 0, 0);
                SetupAxis(fd, ABS_HAT0X, -1, 1, 0, 0);
                SetupAxis(fd, ABS_HAT0Y, -1, 1, 0, 0);

                // 4. 设备标识。
                var setup = new UinputSetup
                {
                    Bustype = BUS_USB,
                    Vendor = VendorId,
                    Product = ProductId,
                    Version = 0x0110,
                    Name = new byte[80],
                    FfEffectsMax = 0
                };
                byte[] nameBytes = Encoding.UTF8.GetBytes(DeviceName);
                Array.Copy(nameBytes, setup.Name, Math.Min(nameBytes.Length, 79));

                if (IoctlPtr(fd, UI_DEV_SETUP, ref setup) < 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "UI_DEV_SETUP failed (requires Linux >= 4.5).");
                }

                // 5. 创建设备。
                if (IoctlValue(fd, UI_DEV_CREATE, 0) < 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "UI_DEV_CREATE failed.");
                }

                return new UinputVirtualPad(fd);
            }
            catch
            {
                // 建不出来就把 fd 收干净，别泄漏设备节点。
                try { Close(fd); } catch { }
                throw;
            }
        }

        private static void SetupAxis(int fd, ushort code, int min, int max, int fuzz, int flat)
        {
            IoctlCheck(fd, UI_SET_ABSBIT, code, $"UI_SET_ABSBIT({code})");

            var absSetup = new UinputAbsSetup
            {
                Code = code,
                Padding = 0,
                AbsInfo = new InputAbsInfo
                {
                    Value = 0,
                    Minimum = min,
                    Maximum = max,
                    Fuzz = fuzz,
                    Flat = flat,
                    Resolution = 0
                }
            };

            if (IoctlAbs(fd, UI_ABS_SETUP, ref absSetup) < 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"UI_ABS_SETUP({code}) failed (requires Linux >= 4.5).");
            }
        }

        private static void IoctlCheck(int fd, nuint request, nint value, string label)
        {
            if (IoctlValue(fd, request, value) < 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"{label} failed.");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                // 后端本身不持有 fd（每个 pad 独立）：uinput 设备随各 pad 的 Dispose 消失。
            }
        }
    }
}
