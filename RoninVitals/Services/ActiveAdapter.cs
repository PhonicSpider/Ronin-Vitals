using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RoninVitals.Services;

/// <summary>Finds the network adapter that is actually carrying this PC's traffic, and reads its address and gateway.</summary>
internal static class ActiveAdapter
{
    private static readonly string[] VirtualAdapterKeywords = ["Virtual", "Hyper-V", "VMware", "VirtualBox", "Bluetooth", "TAP-Windows"];

    /// <summary>
    /// The connected adapter that has a default route out, preferring real adapters over virtual ones, then the fastest.
    /// Link speed alone is not enough: a Hyper-V virtual switch reports 10 Gbps with no gateway.
    /// </summary>
    public static NetworkInterface? Find() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                              && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(HasIPv4Gateway)
            .ThenBy(IsVirtual)
            .ThenByDescending(adapter => adapter.Speed)
            .FirstOrDefault();

    public static IPAddress? FindIPv4Gateway(NetworkInterface? adapter) =>
        adapter?.GetIPProperties().GatewayAddresses
            .Select(gateway => gateway.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any));

    public static IPAddress? FindIPv4Address(NetworkInterface adapter) =>
        adapter.GetIPProperties().UnicastAddresses
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);

    private static bool HasIPv4Gateway(NetworkInterface adapter) => FindIPv4Gateway(adapter) is not null;

    private static bool IsVirtual(NetworkInterface adapter) =>
        VirtualAdapterKeywords.Any(keyword => adapter.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
