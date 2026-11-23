using System.Runtime.InteropServices;

namespace HanVGamepad.Driver.Linux
{
    /// <summary>
    /// Linux uinput 内核接口的 P/Invoke 定义（libc open/ioctl/write/close + uinput 结构体）。
    /// 结构体里的 C long / unsigned long 一律用 IntPtr / nuint 承载，
    /// 自动匹配 32 位（ILP32）与 64 位（LP64）的 ABI 差异。
    /// </summary>
    internal static class LinuxNative
    {
        // ── open flags ─────────────────────────────────────────────
        internal const int O_WRONLY = 0x0001;
        internal const int O_NONBLOCK = 0x0800;

        // ── event types / codes（linux/input-event-codes.h）────────
        internal const ushort EV_SYN = 0x00;
        internal const ushort EV_KEY = 0x01;
        internal const ushort EV_ABS = 0x03;

        internal const int SYN_REPORT = 0;

        internal const ushort BTN_SOUTH = 0x130; // A
        internal const ushort BTN_EAST = 0x131;  // B
        internal const ushort BTN_NORTH = 0x133; // X
        internal const ushort BTN_WEST = 0x134;  // Y
        internal const ushort BTN_TL = 0x136;    // LB
        internal const ushort BTN_TR = 0x137;    // RB
        internal const ushort BTN_SELECT = 0x132;// Back
        internal const ushort BTN_START = 0x13B; // Start
        internal const ushort BTN_MODE = 0x13C;  // Guide
        internal const ushort BTN_THUMBL = 0x13D;// LS
        internal const ushort BTN_THUMBR = 0x13E;// RS

        internal const ushort ABS_X = 0x00;     // 左摇杆 X
        internal const ushort ABS_Y = 0x01;     // 左摇杆 Y
        internal const ushort ABS_Z = 0x02;     // LT
        internal const ushort ABS_RX = 0x03;    // 右摇杆 X
        internal const ushort ABS_RY = 0x04;    // 右摇杆 Y
        internal const ushort ABS_RZ = 0x05;    // RT
        internal const ushort ABS_HAT0X = 0x10; // DPad X
        internal const ushort ABS_HAT0Y = 0x11; // DPad Y

        internal const ushort BUS_USB = 0x03;

        // ── ioctl 编号（_IOW/_IO('U', nr, type)）───────────────────
        internal const nuint UI_DEV_CREATE = 0x5501;
        internal const nuint UI_DEV_DESTROY = 0x5502;
        internal const nuint UI_DEV_SETUP = 0x405C5503;   // _IOW('U', 3, struct uinput_setup)
        internal const nuint UI_ABS_SETUP = 0x401C5504;   // _IOW('U', 4, struct uinput_abs_setup)
        internal const nuint UI_SET_EVBIT = 0x40045564;   // _IOW('U', 100, int)
        internal const nuint UI_SET_KEYBIT = 0x40045565;  // _IOW('U', 101, int)
        internal const nuint UI_SET_ABSBIT = 0x40045567;  // _IOW('U', 103, int)

        internal const string UinputDevicePath = "/dev/uinput";

        // ── libc ──────────────────────────────────────────────────
        [DllImport("libc", SetLastError = true, EntryPoint = "open")]
        internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags);

        [DllImport("libc", SetLastError = true, EntryPoint = "close")]
        internal static extern int Close(int fd);

        [DllImport("libc", SetLastError = true, EntryPoint = "write")]
        internal static extern IntPtr Write(int fd, byte[] buffer, nuint count);

        /// <summary>传值型 ioctl（第三参数是整数值，如 UI_SET_EVBIT 的事件码）。</summary>
        [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
        internal static extern int IoctlValue(int fd, nuint request, nint value);

        /// <summary>传指针型 ioctl（第三参数是结构体指针，如 UI_DEV_SETUP）。</summary>
        [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
        internal static extern int IoctlPtr(int fd, nuint request, ref UinputSetup setup);

        [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
        internal static extern int IoctlAbs(int fd, nuint request, ref UinputAbsSetup setup);

        /// <summary>
        /// input_event：timeval（两个 C long，宽度 = 指针宽度）+ type/code/value。
        /// 64 位上 24 字节，32 位上 16 字节 —— IntPtr 字段自动对齐两种 ABI。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct InputEvent
        {
            public IntPtr TvSec;
            public IntPtr TvUsec;
            public ushort Type;
            public ushort Code;
            public int Value;

            public static InputEvent Key(ushort code, int value) => new()
            {
                Type = EV_KEY,
                Code = code,
                Value = value
            };

            public static InputEvent Abs(ushort code, int value) => new()
            {
                Type = EV_ABS,
                Code = code,
                Value = value
            };

            public static InputEvent Sync() => new()
            {
                Type = EV_SYN,
                Code = SYN_REPORT,
                Value = 0
            };
        }

        /// <summary>struct uinput_setup（linux/uinput.h）：input_id + name[80] + ff_effects_max。</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct UinputSetup
        {
            public ushort Bustype;
            public ushort Vendor;
            public ushort Product;
            public ushort Version;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
            public byte[] Name;

            public uint FfEffectsMax;
        }

        /// <summary>struct uinput_abs_setup：code + 对齐填充 + input_absinfo。</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct UinputAbsSetup
        {
            public ushort Code;
            public ushort Padding;
            public InputAbsInfo AbsInfo;
        }

        /// <summary>struct input_absinfo。</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct InputAbsInfo
        {
            public int Value;
            public int Minimum;
            public int Maximum;
            public int Fuzz;
            public int Flat;
            public int Resolution;
        }
    }
}
