using System.Runtime.InteropServices;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver.Windows
{
    /// <summary>
    /// Windows 真实手柄探测：XInput 的 4 个用户槽位逐个查有没有设备。
    /// XInput 本身分不清「真手柄」和「ViGEm 虚拟设备」，两边都算连接成功；
    /// 扣掉本程序自己创建的那部分由 GamepadManager 做。
    /// 探测失败一律返回 -1（未知），不参与扣减 —— 宁可多给虚拟手柄，也不要误判把玩家的槽位收走。
    /// </summary>
    internal sealed class XInputGamepadProbe : IGamepadProbe
    {
        private const uint ErrorSuccess = 0;

        // XInputGetState 挂在 xinput1_4.dll（Win8+）；老系统只有 xinput1_3.dll。
        private readonly bool _useLegacyDll;

        public XInputGamepadProbe()
        {
            try
            {
                XInputGetState14(0, out _);
            }
            catch (DllNotFoundException)
            {
                _useLegacyDll = true;
            }
            catch (EntryPointNotFoundException)
            {
                _useLegacyDll = true;
            }
        }

        public int CountXInputPads()
        {
            int count = 0;

            for (uint index = 0; index < GamepadLimits.WindowsMaxPads; index++)
            {
                try
                {
                    uint result = _useLegacyDll
                        ? XInputGetState13(index, out _)
                        : XInputGetState14(index, out _);

                    if (result == ErrorSuccess)
                    {
                        count++;
                    }
                }
                catch (DllNotFoundException)
                {
                    return -1;
                }
                catch (EntryPointNotFoundException)
                {
                    return -1;
                }
            }

            return count;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, out XInputState pState);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState13(uint dwUserIndex, out XInputState pState);

        // 字段顺序必须与 XINPUT_STATE 完全一致：XInput 按内存布局回填，多一个字段就会读脏。
        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }
    }
}
