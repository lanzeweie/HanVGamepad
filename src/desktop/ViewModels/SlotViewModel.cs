using CommunityToolkit.Mvvm.ComponentModel;
using HanVGamepad.Desktop.Services;

namespace HanVGamepad.Desktop.ViewModels
{
    /// <summary>
    /// 槽位卡的展示模型。Slot 是 1-based 展示号（1..4），
    /// Type 表示「槽位上有没有常驻设备」而不是「有没有人连着」。
    /// </summary>
    public sealed partial class SlotViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _slot;

        [ObservableProperty]
        private bool _occupied;

        /// <summary>"xbox" / "ds4" / 空串。</summary>
        [ObservableProperty]
        private string _type = string.Empty;

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _remoteIp = string.Empty;

        /// <summary>0-based 槽位号；空槽 -1。</summary>
        [ObservableProperty]
        private int _controllerId = -1;

        [ObservableProperty]
        private string _language = Languages.Chinese;

        /// <summary>这个槽位还能不能用。真实手柄占掉的额度不算在虚拟槽位里。</summary>
        [ObservableProperty]
        private bool _isAvailable = true;

        /// <summary>有 Owner 才能断开。</summary>
        public bool CanDisconnect => Occupied;

        /// <summary>有常驻设备才能卸载。</summary>
        public bool CanRemove => Type.Length > 0;

        public string TitleText => Strings.T(Language, "slot.player", Slot);

        /// <summary>卡片左上角的两位槽位号（01..04）。</summary>
        public string IndexText => Slot.ToString("00");

        public bool IsFree => !Occupied;

        /// <summary>被系统里的真实手柄顶掉的槽位（Windows 4 路上限）。</summary>
        public bool IsReserved => !IsAvailable;

        /// <summary>可用且没人坐（含「等待设备重连」）：显示灰色手柄。</summary>
        public bool IsPlainFree => IsAvailable && !Occupied;

        /// <summary>右上角状态徽章：已连接 / 等待设备重连 / 空闲 / 系统占用。</summary>
        public string BadgeText => !IsAvailable
            ? Strings.T(Language, "slot.systemPad")
            : Occupied
                ? Strings.T(Language, "slot.connected")
                : Type.Length > 0
                    ? Strings.T(Language, "slot.waiting")
                    : Strings.T(Language, "slot.idle");

        /// <summary>卡片主体的一行说明：有连接时优先显示设备信息。</summary>
        public string DetailText => !IsAvailable
            ? Strings.T(Language, "slot.systemPadHint")
            : Occupied
                ? (MetaText.Length > 0 ? MetaText : Strings.T(Language, "slot.connected"))
                : Type.Length > 0
                    ? Strings.T(Language, "slot.waiting")
                    : Strings.T(Language, "slot.waitingDevice");

        public string TypeText => Type.Length > 0 ? Strings.TypeLabel(Language, Type) : string.Empty;

        public string MetaText
        {
            get
            {
                if (!Occupied)
                {
                    return string.Empty;
                }

                var parts = new List<string>();
                if (DeviceName.Length > 0)
                {
                    parts.Add(DeviceName);
                }

                if (RemoteIp.Length > 0)
                {
                    parts.Add(RemoteIp);
                }

                return string.Join(" · ", parts);
            }
        }

        public string DisconnectText => Strings.T(Language, "slot.disconnect");
        public string RemoveText => Strings.T(Language, "slot.delete");

        /// <summary>整卡刷新（状态或语言变化后调用）。</summary>
        public void RefreshTexts()
        {
            OnPropertyChanged(string.Empty);
        }
    }
}
