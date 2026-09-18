using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Dihor.GameKit.Networking.Discovery.Lan;

public static class LanDiscoveryBroadcastAddressResolver
{
    public static IReadOnlyList<IPAddress> Resolve()
    {
        var addresses = new Dictionary<string, IPAddress>(StringComparer.Ordinal);
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask is null)
                    {
                        continue;
                    }

                    var broadcast = Calculate(address.Address, address.IPv4Mask);
                    addresses.TryAdd(broadcast.ToString(), broadcast);
                }
            }
        }
        catch (NetworkInformationException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }

        addresses.TryAdd(IPAddress.Broadcast.ToString(), IPAddress.Broadcast);
        return addresses.Values.ToArray();
    }

    public static IPAddress Calculate(IPAddress address, IPAddress subnetMask)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(subnetMask);
        var addressBytes = address.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
        {
            throw new ArgumentException("Broadcast calculation requires IPv4 addresses.");
        }

        var broadcast = new byte[4];
        for (var index = 0; index < broadcast.Length; index++)
        {
            broadcast[index] = (byte)(addressBytes[index] | ~maskBytes[index]);
        }

        return new IPAddress(broadcast);
    }
}
