using System.Collections.Concurrent;
using HanVGamepad.Driver.Abstractions;
using HanVGamepad.Server.Logging;

namespace HanVGamepad.Server.Slots
{
    /// <summary>
    /// 槽位与虚拟手柄管理。
    /// 平台无关：通过 IGamepadBackend 创建 IVirtualPad，输入以 PadReport 全量报告提交
    /// （协议消息是增量的，由本类维护输入状态）。
    /// 槽位语义：4 槽常驻设备、TryClaimSlot 四级优先策略、断线只清 Owner、
    /// 设备码（clientId）/ IP 记忆认回。
    /// </summary>
    public class GamepadManager : IDisposable
    {
        private readonly IGamepadBackend _backend;
        private readonly ConcurrentDictionary<string, GamepadSettings> _controllers = new();

        // 设备槽位常驻：设备在首次认领时创建，断开只清 Owner，Dispose 才销毁。
        // 数量按平台定：Windows 受 XInput 限制是 4，便携平台是 GamepadLimits.PortableMaxPads。
        private readonly GamepadSlot[] _slots;
        private readonly object _indexLock = new object();

        // 系统里真实手柄的数量（不含本程序创建的）。由宿主定时调用 UpdateFromProbe 维护。
        private int _externalPads;

        // 真实手柄数量「增加」时的待确认值：XInput 枚举在设备刚建/刚拆的瞬间会抖。
        private int _pendingExternalPads = -1;

        /// <summary>本实例的槽位数量（Windows 4，便携平台 16）。</summary>
        public int SlotCount => _slots.Length;

        private static GamepadSlot[] CreateSlots(int count)
        {
            var slots = new GamepadSlot[count];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new GamepadSlot();
            }

            return slots;
        }

        private sealed class GamepadSlot
        {
            public IVirtualPad? Device;

            /// <summary>该槽位当前设备的类型；设备建好之后才有值，空槽为 null。</summary>
            public TypeController? Type;

            /// <summary>手机端自报的设备名（浏览器 User-Agent 解析出来的机型）；空槽为 null。</summary>
            public string? DeviceName;

            /// <summary>手机端的局域网 IP（认领人地址，用于「靠 IP 认回槽位」）；空槽为 null。</summary>
            public string? RemoteIp;

            public string? OwnerConnectionId;

            // 「上一次是谁坐在这个槽位上」。断开不清，重连才能认回同一台设备。
            // 只在本次进程里记（每次运行单独记录），服务重启就没了。
            // ClientId 是手机端 localStorage 里持久化的设备码，比 IP 稳；IP 是拿不到设备码时的退路。
            public string? ClientId;
        }

        public event Action<string, byte, byte>? OnVibrationReceived;

        /// <summary>槽位占用 / 释放 / 设备类型变化后触发，宿主据此广播 SlotChanged。在锁外触发。</summary>
        public event Action? OnSlotsChanged;

        /// <summary>玩家拿到槽位号时立刻通知宿主。</summary>
        public event Action<string, int>? OnControllerIdAssigned;

        // 协议按钮名 → 位掩码（LT/RT 是扳机，单独处理，不出现在此表）。
        private static readonly Dictionary<string, PadButtons> ButtonMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "A", PadButtons.A }, { "B", PadButtons.B }, { "X", PadButtons.X }, { "Y", PadButtons.Y },
            { "Up", PadButtons.DPadUp }, { "Down", PadButtons.DPadDown },
            { "Left", PadButtons.DPadLeft }, { "Right", PadButtons.DPadRight },
            { "Start", PadButtons.Start }, { "Back", PadButtons.Back },
            { "LB", PadButtons.LeftShoulder }, { "RB", PadButtons.RightShoulder },
            { "LS", PadButtons.LeftThumb }, { "RS", PadButtons.RightThumb },
            { "Xbox", PadButtons.Guide }, { "Touchpad", PadButtons.Touchpad }
        };

        public GamepadManager(IGamepadBackend backend, int? capacity = null)
        {
            _backend = backend;

            // 传更大的值也没用：Windows 侧的 XInput 读不到第 5 个，所以统一压到便携上限。
            int count = Math.Clamp(capacity ?? GamepadLimits.ForCurrentPlatform, 1, GamepadLimits.PortableMaxPads);
            _slots = CreateSlots(count);
        }

        /// <summary>
        /// 首次认领槽位时按客户端类型创建设备；设备从此常驻，直到 Dispose。
        /// 振动回调不捕获 connectionId：设备会被多个连接先后复用，必须按槽位当前 Owner 转发。
        /// </summary>
        private IVirtualPad CreateGamepad(int slotIndex, TypeController type)
        {
            IVirtualPad pad = _backend.CreatePad(type.ToPadType());
            pad.VibrationReceived += (largeMotor, smallMotor) => OnVibrationForSlot(slotIndex, largeMotor, smallMotor);
            return pad;
        }

        private void OnVibrationForSlot(int slotIndex, byte largeMotor, byte smallMotor)
        {
            string? owner;
            lock (_indexLock)
            {
                owner = _slots[slotIndex].OwnerConnectionId;
            }

            if (owner == null)
            {
                return;
            }

            if (_controllers.TryGetValue(owner, out GamepadSettings? settings) && settings.EnableVibration)
            {
                OnVibrationReceived?.Invoke(owner, largeMotor, smallMotor);
            }
        }

        /// <summary>
        /// 把常驻设备的中性状态重置一遍：设备断开后仍然活着，必须清掉上一位玩家留下的按键/摇杆状态，
        /// 否则游戏里会一直卡着最后按下的那一下。异常一律吞掉（设备可能正被 Dispose）。
        /// </summary>
        private void ResetNeutral(int slotIndex)
        {
            try
            {
                IVirtualPad? device;
                lock (_indexLock)
                {
                    device = _slots[slotIndex].Device;
                }

                if (device == null)
                {
                    return;
                }

                device.ResetNeutral();
            }
            catch
            {
                // 重置失败不能影响断开/接管流程。
            }
        }

        private void RaiseSlotsChanged()
        {
            OnSlotsChanged?.Invoke();
        }

        /// <summary>
        /// 占用槽位。必须在 WebRTC 协商之前调用：占不到就直接拒，别让客户端进入协商。
        /// 指定槽位：必须空闲（用户自己选的，优先尊重）；
        /// 不指定：设备码命中 → IP 命中 → 从未被分配过的干净槽 → 最小空闲。
        /// 设备类型由客户端决定（controllerType），服务端只照办。
        /// 失败时 reason 是稳定码，客户端负责翻译：slot_invalid / slot_taken / no_free_slot / device_create_failed。
        /// </summary>
        public bool TryClaimSlot(
            string connectionId,
            int? requestedSlot,
            string? clientId,
            string? remoteIp,
            string? deviceName,
            TypeController controllerType,
            bool enableVib,
            int sensitivity,
            out int slotIndex,
            out string reason)
        {
            slotIndex = -1;
            reason = string.Empty;

            bool claimed = false;

            lock (_indexLock)
            {
                // 同一连接重复 SendOffer：沿用已经拿到的槽位，不占第二个。
                if (_controllers.TryGetValue(connectionId, out GamepadSettings? existing))
                {
                    slotIndex = existing.ControllerIndex;
                    return true;
                }

                int assigned = -1;

                if (requestedSlot.HasValue)
                {
                    int wanted = requestedSlot.Value;
                    if (wanted < 0 || wanted >= _slots.Length)
                    {
                        reason = "slot_invalid";
                        return false;
                    }

                    GamepadSlot target = _slots[wanted];
                    if (target.OwnerConnectionId != null)
                    {
                        reason = "slot_taken";
                        return false;
                    }

                    assigned = wanted;
                }
                else
                {
                    // 第一、二级：认回「上次就是我」的空闲槽（设备码优先，IP 兜底）。
                    assigned = FindOwnedFreeSlot(clientId, remoteIp);

                    // 第三级：给新用户一个从未被分配过的干净槽位，不去占别人留下的位置。
                    if (assigned == -1)
                    {
                        for (int i = 0; i < _slots.Length; i++)
                        {
                            if (IsFree(i) && string.IsNullOrEmpty(_slots[i].ClientId) && string.IsNullOrEmpty(_slots[i].RemoteIp))
                            {
                                assigned = i;
                                break;
                            }
                        }
                    }

                    // 第四级：所有槽位都被人用过，才去占曾用过但已空闲的位。
                    if (assigned == -1)
                    {
                        for (int i = 0; i < _slots.Length; i++)
                        {
                            if (IsFree(i))
                            {
                                assigned = i;
                                break;
                            }
                        }
                    }

                    if (assigned == -1)
                    {
                        reason = "no_free_slot";
                        return false;
                    }
                }

                GamepadSlot slot = _slots[assigned];

                // 设备常驻：首次认领时按客户端选的类型建设备，之后同一类型直接复用。
                // 类型变了（换台手机连进来、或同一台手机换了类型重连）就在原槽位拆旧建新 ——
                // 必须先拆再建：ViGEm 同时挂着的虚拟设备有限，旧设备还占着就建新的会凑出第 5 个 target。
                if (slot.Device != null && slot.Type != controllerType)
                {
                    IVirtualPad? previous = slot.Device;
                    slot.Device = null;
                    slot.Type = null;
                    DisposeDevice(previous);
                }

                if (slot.Device == null)
                {
                    // 额度不够就别建：4 个槽先让给系统里的真实手柄，虚拟手柄只能占剩下的。
                    if (CountResidentLocked() >= SlotCount - _externalPads)
                    {
                        reason = "no_free_slot";
                        return false;
                    }

                    try
                    {
                        slot.Device = CreateGamepad(assigned, controllerType);
                        slot.Type = controllerType;
                    }
                    catch (Exception ex)
                    {
                        // 建不出来就把槽位留干净，回稳定码，别让异常穿出 hub。
                        ServerLog.Error($"槽位 {assigned} 创建虚拟手柄失败", ex);
                        reason = "device_create_failed";
                        return false;
                    }
                }

                slot.OwnerConnectionId = connectionId;

                // 记下这次是谁坐的：设备码优先，IP 一起记，老客户端没设备码时靠 IP 认人。
                if (!string.IsNullOrEmpty(clientId))
                {
                    slot.ClientId = clientId;
                }

                if (!string.IsNullOrEmpty(remoteIp))
                {
                    slot.RemoteIp = remoteIp;
                }

                if (!string.IsNullOrEmpty(deviceName))
                {
                    slot.DeviceName = deviceName;
                }

                _controllers[connectionId] = new GamepadSettings(
                    enableVib, sensitivity, controllerType, slot.Device, assigned);

                slotIndex = assigned;
                claimed = true;
            }

            if (!claimed)
            {
                return false;
            }

            // 无论新建还是复用，接手前都必须是干净状态。
            ResetNeutral(slotIndex);

            OnControllerIdAssigned?.Invoke(connectionId, slotIndex);
            ServerLog.Info($"分配槽位 {slotIndex} 给连接 {connectionId}");
            RaiseSlotsChanged();
            return true;
        }

        /// <summary>空闲 = 没人占用。</summary>
        private bool IsFree(int slot)
        {
            return _slots[slot].OwnerConnectionId == null;
        }

        /// <summary>
        /// 找回「上次是我」的空闲槽：先按设备码，再按 IP。只认本次运行期内的记录。
        /// 调用方必须已持有 _indexLock。
        /// </summary>
        private int FindOwnedFreeSlot(string? clientId, string? remoteIp)
        {
            if (!string.IsNullOrEmpty(clientId))
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (IsFree(i) && string.Equals(_slots[i].ClientId, clientId, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            if (!string.IsNullOrEmpty(remoteIp))
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (IsFree(i) && string.Equals(_slots[i].RemoteIp, remoteIp, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// 只读查询：这个连接「上次坐的是哪个槽」。手机端用它把上次的槽位标出来，方便一键接回去。
        /// 没有记录或那个槽已被占 → null。
        /// </summary>
        public int? GetRecommendedSlot(string? clientId, string? remoteIp)
        {
            lock (_indexLock)
            {
                int slot = FindOwnedFreeSlot(clientId, remoteIp);
                return slot == -1 ? null : slot;
            }
        }

        /// <summary>
        /// 释放占用权（踢出与正常断开都走这里）：设备留在槽位上（不 Disconnect、不 Dispose），
        /// 只把 Owner 置空并归零，重连的人拿回这个空槽就还是同一台设备、同一个编号。
        /// </summary>
        public void DisconnectGamepad(string connectionId)
        {
            int slotIndex = -1;

            lock (_indexLock)
            {
                if (_controllers.TryRemove(connectionId, out GamepadSettings? settings))
                {
                    int index = settings.ControllerIndex;
                    if (index >= 0 && index < _slots.Length && _slots[index].OwnerConnectionId == connectionId)
                    {
                        _slots[index].OwnerConnectionId = null;
                        slotIndex = index;
                    }
                }
            }

            if (slotIndex == -1)
            {
                return;
            }

            ResetNeutral(slotIndex);
            ServerLog.Info($"槽位 {slotIndex} 已释放（连接 {connectionId}）");
            RaiseSlotsChanged();
        }

        /// <summary>
        /// 卸载槽位上常驻的虚拟设备（管理面「卸载」用）。
        /// 只在槽位空闲时允许：有人坐着的时候删设备，等于把人家正在用的手柄拔掉。
        /// 认人信息（ClientId / RemoteIp / DeviceName）不动 —— 删的是设备，不是这个槽位的记忆，
        /// 同一个手机下次连进来仍然认回同一个槽号，设备按它这次选的类型重建。
        /// </summary>
        public bool RemoveDevice(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length)
            {
                return false;
            }

            IVirtualPad? device;

            lock (_indexLock)
            {
                GamepadSlot slot = _slots[slotIndex];
                if (slot.OwnerConnectionId != null || slot.Device == null)
                {
                    return false;
                }

                device = slot.Device;
                slot.Device = null;
                slot.Type = null;
            }

            // Disconnect/Dispose 要等驱动回话，放到锁外做，别卡住别的槽位认领。
            DisposeDevice(device);
            ServerLog.Info($"槽位 {slotIndex} 的虚拟设备已卸载");
            RaiseSlotsChanged();
            return true;
        }

        /// <summary>系统里真实手柄的数量（已扣掉本程序创建的虚拟设备）。</summary>
        public int ExternalPadCount
        {
            get
            {
                lock (_indexLock)
                {
                    return _externalPads;
                }
            }
        }

        /// <summary>还能开几个虚拟手柄：槽位上限 - 真实手柄数，最小 0。</summary>
        public int Capacity
        {
            get
            {
                lock (_indexLock)
                {
                    return Math.Max(0, SlotCount - _externalPads);
                }
            }
        }

        /// <summary>当前常驻的虚拟设备数量（含空闲槽位上的设备）。</summary>
        public int ResidentDeviceCount()
        {
            lock (_indexLock)
            {
                return CountResidentLocked();
            }
        }

        /// <summary>常驻设备里 Xbox360 类型（会占 XInput 槽位）的数量。</summary>
        public int ResidentXboxPadCount()
        {
            lock (_indexLock)
            {
                return CountResidentXboxPadCountLocked();
            }
        }

        /// <summary>
        /// 宿主定时把 XInput 探测结果送进来（这个数包含本程序自己创建的 Xbox 虚拟设备）。
        /// 内部扣掉自己建的那部分得到真实手柄数。
        /// 真实手柄「增加」必须连续两拍一致才采用：设备刚创建/销毁的一两百毫秒里 XInput 枚举会抖，
        /// 误判会把玩家正在用的虚拟手柄额度收走。
        /// </summary>
        public void UpdateFromProbe(int xinputPadCount)
        {
            if (xinputPadCount < 0)
            {
                // 平台不支持检测：不做扣减。
                return;
            }

            List<IVirtualPad>? reclaimed = null;

            lock (_indexLock)
            {
                int real = Math.Clamp(xinputPadCount - CountResidentXboxPadCountLocked(), 0, SlotCount);

                if (real > _externalPads)
                {
                    if (_pendingExternalPads != real)
                    {
                        _pendingExternalPads = real;
                        return;
                    }
                }
                else
                {
                    _pendingExternalPads = -1;
                }

                if (real == _externalPads)
                {
                    return;
                }

                _pendingExternalPads = -1;
                _externalPads = real;

                // 额度变小：把「空闲且没人在用」的常驻设备收回来，从最高槽位开始收。
                int capacity = SlotCount - _externalPads;
                while (CountResidentLocked() > capacity)
                {
                    int index = FindReclaimableSlotLocked();
                    if (index < 0)
                    {
                        // 剩下的都有人在用：等他们断开，不硬抢。
                        break;
                    }

                    reclaimed ??= new List<IVirtualPad>();
                    reclaimed.Add(_slots[index].Device!);
                    _slots[index].Device = null;
                    _slots[index].Type = null;
                }
            }

            if (reclaimed == null)
            {
                return;
            }

            // 设备回收要等驱动回话，放到锁外做。
            foreach (IVirtualPad device in reclaimed)
            {
                DisposeDevice(device);
            }

            ServerLog.Info($"真实手柄占用增加，已回收 {reclaimed.Count} 个空闲虚拟手柄");
            RaiseSlotsChanged();
        }

        /// <summary>调用方必须已持有 _indexLock。</summary>
        private int CountResidentLocked()
        {
            int count = 0;
            foreach (GamepadSlot slot in _slots)
            {
                if (slot.Device != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>调用方必须已持有 _indexLock。</summary>
        private int CountResidentXboxPadCountLocked()
        {
            int count = 0;
            foreach (GamepadSlot slot in _slots)
            {
                if (slot.Device != null && slot.Type == TypeController.Xbox360)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>可回收的槽位：没人坐着且挂着设备。调用方必须已持有 _indexLock。</summary>
        private int FindReclaimableSlotLocked()
        {
            for (int i = _slots.Length - 1; i >= 0; i--)
            {
                if (_slots[i].OwnerConnectionId == null && _slots[i].Device != null)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>推给手机端的槽位投影（不含 connectionId）。</summary>
        public IReadOnlyList<SlotInfo> GetSlotInfos()
        {
            lock (_indexLock)
            {
                var infos = new List<SlotInfo>(_slots.Length);
                for (int slot = 0; slot < _slots.Length; slot++)
                {
                    infos.Add(new SlotInfo(
                        slot,
                        _slots[slot].OwnerConnectionId != null));
                }

                return infos;
            }
        }

        private static void DisposeDevice(IVirtualPad? device)
        {
            if (device == null)
            {
                return;
            }

            try
            {
                device.Dispose();
            }
            catch
            {
                // 旧设备回收失败不影响新设备接管。
            }
        }

        /// <summary>
        /// 按键消息（协议 B:{button}:{0|1}）。更新输入状态后整包提交。
        /// LT/RT 是扳机（0/255），其余映射到位掩码。
        /// </summary>
        public void OnButtonReceive(string connectionId, string buttonName, bool isPressed)
        {
            if (!_controllers.TryGetValue(connectionId, out GamepadSettings? settings))
            {
                return;
            }

            lock (_indexLock)
            {
                PadReport current = settings.Input;
                PadReport next;

                if (buttonName == "LT")
                {
                    next = current with { LeftTrigger = (byte)(isPressed ? 255 : 0) };
                }
                else if (buttonName == "RT")
                {
                    next = current with { RightTrigger = (byte)(isPressed ? 255 : 0) };
                }
                else if (ButtonMap.TryGetValue(buttonName, out PadButtons flag))
                {
                    uint buttons = isPressed
                        ? current.Buttons | (uint)flag
                        : current.Buttons & ~(uint)flag;
                    next = current with { Buttons = buttons };
                }
                else
                {
                    // 未知按钮名：丢弃这一条，保持上一帧。
                    return;
                }

                settings.Input = next;
                settings.Gamepad.Submit(next);
            }
        }

        /// <summary>摇杆消息（协议 J:{stick}:{x}:{y}）。灵敏度按 sensitivity/100 缩放后整包提交。</summary>
        public void OnJoystickMove(string connectionId, string stickName, short xValue, short yValue)
        {
            if (!_controllers.TryGetValue(connectionId, out GamepadSettings? settings))
            {
                return;
            }

            lock (_indexLock)
            {
                // 灵敏度按 sensitivity/100 缩放（10–200%，100% 为原值），再夹回轴值域。
                short x = ScaleAxis(xValue, settings.JoystickSensitivity);
                short y = ScaleAxis(yValue, settings.JoystickSensitivity);

                PadReport current = settings.Input;
                PadReport next;

                if (stickName == "Left")
                {
                    next = current with { LeftX = x, LeftY = y };
                }
                else if (stickName == "Right")
                {
                    next = current with { RightX = x, RightY = y };
                }
                else
                {
                    return;
                }

                settings.Input = next;
                settings.Gamepad.Submit(next);
            }
        }

        /// <summary>Xbox 轴值域是 -32768..32767；缩放后必须夹回来，否则强转 short 会绕回反方向。</summary>
        private static short ScaleAxis(short value, int sensitivity)
        {
            int percent = sensitivity <= 0 ? 100 : sensitivity;
            if (percent == 100)
            {
                return value;
            }

            double scaled = value * (percent / 100.0);
            if (scaled > short.MaxValue)
            {
                return short.MaxValue;
            }

            if (scaled < short.MinValue)
            {
                return short.MinValue;
            }

            return (short)Math.Round(scaled);
        }

        public int GetControllerId(string connectionId)
        {
            if (_controllers.TryGetValue(connectionId, out GamepadSettings? settings))
            {
                return settings.ControllerIndex;
            }

            return -1;
        }

        /// <summary>界面用：连接对应的手柄类型字符串。</summary>
        public string GetControllerTypeString(string connectionId)
        {
            if (_controllers.TryGetValue(connectionId, out GamepadSettings? settings))
            {
                return settings.ControllerType.ToString();
            }

            return "Unknown";
        }

        /// <summary>
        /// 只读快照：固定 4 条，Slot = 槽位下标（0-based，等于 XInput 槽位）。
        /// Occupied = 当前有 Owner；ControllerType 是该槽位当前设备的实际类型，空槽为 null。
        /// </summary>
        public IReadOnlyList<GamepadSnapshot> GetSnapshot()
        {
            var snapshot = new List<GamepadSnapshot>(_slots.Length);
            lock (_indexLock)
            {
                for (int slot = 0; slot < _slots.Length; slot++)
                {
                    GamepadSlot item = _slots[slot];
                    snapshot.Add(new GamepadSnapshot(
                        slot,
                        item.Type?.ToString(),
                        item.OwnerConnectionId != null,
                        item.OwnerConnectionId,
                        item.DeviceName,
                        item.RemoteIp));
                }
            }

            return snapshot;
        }

        public void UpdateSettingLive(string connectionId, string settingName, string value)
        {
            if (!_controllers.TryGetValue(connectionId, out GamepadSettings? settings))
            {
                return;
            }

            if (settingName == "VibrationEnabled")
            {
                if (bool.TryParse(value, out bool enabled))
                {
                    settings.EnableVibration = enabled;
                }
            }
            else if (settingName == "JoystickSensitivity")
            {
                // 客户端滑块是 10–200%，边界在这里收口，别让脏值把轴值缩放到离谱。
                if (int.TryParse(value, out int sensitivity))
                {
                    settings.JoystickSensitivity = Math.Clamp(sensitivity, 10, 200);
                }
            }
        }

        public void Dispose()
        {
            // 设备是常驻的，只有进程收尾时在这里真正销毁。
            foreach (var slot in _slots)
            {
                DisposeDevice(slot.Device);
                slot.Device = null;
            }

            _backend.Dispose();
        }
    }
}
