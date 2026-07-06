using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkCardMonitor.Models;

namespace NetworkCardMonitor.Services;

internal static class NetworkAdapterService
{
    private static readonly Dictionary<string, TrafficCounter> TrafficCounters = new();

    public static IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        var currentIds = networkInterfaces.Select(adapter => adapter.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removedId in TrafficCounters.Keys.Where(id => !currentIds.Contains(id)).ToArray())
        {
            TrafficCounters.Remove(removedId);
        }

        return networkInterfaces
            .Select(CreateAdapterInfo)
            .OrderByDescending(adapter => adapter.IsConnected)
            .ThenBy(adapter => adapter.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static NetworkAdapterInfo CreateAdapterInfo(NetworkInterface adapter)
    {
        var properties = TryGetIPProperties(adapter);
        var addresses = properties?.UnicastAddresses
            .Where(address => !address.Address.IsIPv6LinkLocal)
            .ToArray() ?? [];

        var ipv4 = JoinAddresses(addresses, AddressFamily.InterNetwork);
        var ipv6 = JoinAddresses(addresses, AddressFamily.InterNetworkV6);
        var gateways = properties?.GatewayAddresses
            .Select(gateway => gateway.Address)
            .Where(address => !address.Equals(System.Net.IPAddress.Any) &&
                              !address.Equals(System.Net.IPAddress.IPv6Any))
            .Select(address => address.ToString())
            .Distinct()
            .ToArray() ?? [];

        var isConnected = adapter.OperationalStatus == OperationalStatus.Up;
        var traffic = GetTrafficSpeed(adapter);
        var linkSpeed = isConnected ? adapter.Speed : 0;

        return new NetworkAdapterInfo(
            adapter.Id,
            adapter.Name,
            adapter.Description,
            FormatAdapterType(adapter.NetworkInterfaceType),
            FormatStatus(adapter.OperationalStatus),
            isConnected,
            linkSpeed,
            isConnected ? FormatSpeed(linkSpeed) : "—",
            traffic.ReceiveBytesPerSecond,
            traffic.SendBytesPerSecond,
            traffic.ReceiveBytesPerSecond + traffic.SendBytesPerSecond,
            traffic.HasSample
                ? $"下载 {FormatDataRate(traffic.ReceiveBytesPerSecond)}  上传 {FormatDataRate(traffic.SendBytesPerSecond)}"
                : "正在采样…",
            string.IsNullOrEmpty(ipv4) ? "—" : ipv4,
            string.IsNullOrEmpty(ipv6) ? "—" : ipv6,
            gateways.Length == 0 ? "—" : string.Join(", ", gateways),
            FormatMacAddress(adapter.GetPhysicalAddress()));
    }

    private static TrafficSpeed GetTrafficSpeed(NetworkInterface adapter)
    {
        try
        {
            var statistics = adapter.GetIPv4Statistics();
            var now = Stopwatch.GetTimestamp();
            var current = new TrafficCounter(statistics.BytesReceived, statistics.BytesSent, now);

            if (!TrafficCounters.TryGetValue(adapter.Id, out var previous))
            {
                TrafficCounters[adapter.Id] = current;
                return TrafficSpeed.Empty;
            }

            TrafficCounters[adapter.Id] = current;
            var elapsedSeconds = (now - previous.Timestamp) / (double)Stopwatch.Frequency;
            if (elapsedSeconds <= 0 ||
                current.BytesReceived < previous.BytesReceived ||
                current.BytesSent < previous.BytesSent)
            {
                return TrafficSpeed.Empty;
            }

            return new TrafficSpeed(
                (current.BytesReceived - previous.BytesReceived) / elapsedSeconds,
                (current.BytesSent - previous.BytesSent) / elapsedSeconds,
                true);
        }
        catch (NetworkInformationException)
        {
            return TrafficSpeed.Empty;
        }
        catch (PlatformNotSupportedException)
        {
            return TrafficSpeed.Empty;
        }
        catch (NotSupportedException)
        {
            return TrafficSpeed.Empty;
        }
        catch (InvalidOperationException)
        {
            return TrafficSpeed.Empty;
        }
    }

    private static IPInterfaceProperties? TryGetIPProperties(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static string JoinAddresses(IEnumerable<UnicastIPAddressInformation> addresses, AddressFamily family)
    {
        return string.Join(", ", addresses
            .Where(address => address.Address.AddressFamily == family)
            .Select(address => address.Address.ToString())
            .Distinct());
    }

    internal static string FormatSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "未知";
        }

        const double kilo = 1_000d;
        const double mega = 1_000_000d;
        const double giga = 1_000_000_000d;

        return bitsPerSecond switch
        {
            >= 10_000_000_000 => $"{bitsPerSecond / giga:0.#} Gbps",
            >= 1_000_000_000 => $"{bitsPerSecond / giga:0.##} Gbps",
            >= 1_000_000 => $"{bitsPerSecond / mega:0.##} Mbps",
            _ => $"{bitsPerSecond / kilo:0.##} Kbps"
        };
    }

    internal static string FormatDataRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 1)
        {
            return "0 B/s";
        }

        const double kilo = 1024d;
        const double mega = kilo * 1024d;
        const double giga = mega * 1024d;

        return bytesPerSecond switch
        {
            >= giga => $"{bytesPerSecond / giga:0.00} GB/s",
            >= mega => $"{bytesPerSecond / mega:0.00} MB/s",
            >= kilo => $"{bytesPerSecond / kilo:0.0} KB/s",
            _ => $"{bytesPerSecond:0} B/s"
        };
    }

    private static string FormatStatus(OperationalStatus status) => status switch
    {
        OperationalStatus.Up => "已连接",
        OperationalStatus.Down => "未连接",
        OperationalStatus.Testing => "正在测试",
        OperationalStatus.Dormant => "等待连接",
        OperationalStatus.NotPresent => "硬件不存在",
        OperationalStatus.LowerLayerDown => "线路断开",
        _ => "状态未知"
    };

    private static string FormatAdapterType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet => "以太网",
        NetworkInterfaceType.GigabitEthernet => "千兆以太网",
        NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT => "快速以太网",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ppp => "拨号 / VPN",
        NetworkInterfaceType.Tunnel => "隧道",
        _ => type.ToString()
    };

    private static string FormatMacAddress(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? "—" : string.Join("-", bytes.Select(value => value.ToString("X2")));
    }

    private readonly struct TrafficCounter
    {
        public TrafficCounter(long bytesReceived, long bytesSent, long timestamp)
        {
            BytesReceived = bytesReceived;
            BytesSent = bytesSent;
            Timestamp = timestamp;
        }

        public long BytesReceived { get; }
        public long BytesSent { get; }
        public long Timestamp { get; }
    }

    private readonly struct TrafficSpeed
    {
        public TrafficSpeed(double receiveBytesPerSecond, double sendBytesPerSecond, bool hasSample)
        {
            ReceiveBytesPerSecond = receiveBytesPerSecond;
            SendBytesPerSecond = sendBytesPerSecond;
            HasSample = hasSample;
        }

        public double ReceiveBytesPerSecond { get; }
        public double SendBytesPerSecond { get; }
        public bool HasSample { get; }
        public static TrafficSpeed Empty => new TrafficSpeed(0, 0, false);
    }
}

// END_OF_SOURCE_FILE
