namespace HanVGamepad.Driver.Abstractions
{
    /// <summary>
    /// 系统里已经存在的手柄探测。用途只有一个：真实手柄占掉了 XInput 槽位，
    /// 虚拟手柄就得让出相应额度。
    /// 探测结果是「系统可见的 XInput 设备总数」，包含本程序自己创建的 Xbox 虚拟设备；
    /// 把这一部分减掉由 GamepadManager 负责，因为只有它知道当前建了几个。
    /// </summary>
    public interface IGamepadProbe
    {
        /// <summary>
        /// 当前系统可见的 XInput 手柄数（0 – <see cref="GamepadLimits.MaxPads"/>）。
        /// 平台不支持检测时返回 -1，调用方按「未知」处理，不做任何扣减。
        /// </summary>
        int CountXInputPads();
    }
}
