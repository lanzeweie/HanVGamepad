using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HanVGamepad.Desktop.Services
{
    /// <summary>
    /// 一个本机 IPv4 地址及其网卡类型。Kind 是稳定键（"wifi" / "ethernet" / "other"），
    /// 显示文案由界面按语言自己映射（Strings.IpLabel），这里不塞任何语言字面量。
    /// </summary>
    public sealed record LocalAddress(string Ip, string Kind, string Label);

    public static class NetworkHelper
    {
        /// <summary>获取所有本机 IPv4 地址，Wi-Fi 网卡排在前面，附带网卡类型。</summary>
        public static List<LocalAddress> GetAllLocalIPv4Addresses()
        {
            List<LocalAddress> addresses = new List<LocalAddress>();

            var sortedInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(item =>
                    item.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                    item.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains("802.11", StringComparison.OrdinalIgnoreCase)
                );

            foreach (NetworkInterface item in sortedInterfaces)
            {
                (string kind, string label) = MapInterfaceKind(item.NetworkInterfaceType);
                IPInterfaceProperties adapterProperties = item.GetIPProperties();

                foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
                {
                    // 只要真实 IPv4，忽略回环。
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                    {
                        string ipAddress = ip.Address.ToString();

                        if (!addresses.Any(existing => existing.Ip == ipAddress))
                        {
                            addresses.Add(new LocalAddress(ipAddress, kind, label));
                        }
                    }
                }
            }

            return addresses;
        }

        private static (string kind, string label) MapInterfaceKind(NetworkInterfaceType interfaceType) => interfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => ("wifi", "Wi-Fi"),
            NetworkInterfaceType.Ethernet => ("ethernet", "以太网"),
            _ => ("other", "其它")
        };
    }
}
