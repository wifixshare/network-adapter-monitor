namespace NetworkCardMonitor.Models;

internal sealed class NetworkAdapterInfo
{
    public NetworkAdapterInfo(
        string id,
        string name,
        string description,
        string adapterType,
        string status,
        bool isConnected,
        long speedBitsPerSecond,
        string speed,
        double receiveBytesPerSecond,
        double sendBytesPerSecond,
        double totalBytesPerSecond,
        string realTimeSpeed,
        string ipv4,
        string ipv6,
        string gateway,
        string macAddress)
    {
        Id = id;
        Name = name;
        Description = description;
        AdapterType = adapterType;
        Status = status;
        IsConnected = isConnected;
        SpeedBitsPerSecond = speedBitsPerSecond;
        Speed = speed;
        ReceiveBytesPerSecond = receiveBytesPerSecond;
        SendBytesPerSecond = sendBytesPerSecond;
        TotalBytesPerSecond = totalBytesPerSecond;
        RealTimeSpeed = realTimeSpeed;
        IPv4 = ipv4;
        IPv6 = ipv6;
        Gateway = gateway;
        MacAddress = macAddress;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string DisplayName => !string.IsNullOrWhiteSpace(Description)
        ? Description
        : !string.IsNullOrWhiteSpace(Name) ? Name : "未命名网卡";
    public string AdapterType { get; }
    public string Status { get; }
    public bool IsConnected { get; }
    public long SpeedBitsPerSecond { get; }
    public string Speed { get; }
    public double ReceiveBytesPerSecond { get; }
    public double SendBytesPerSecond { get; }
    public double TotalBytesPerSecond { get; }
    public string RealTimeSpeed { get; }
    public string IPv4 { get; }
    public string IPv6 { get; }
    public string Gateway { get; }
    public string MacAddress { get; }
}

// END_OF_SOURCE_FILE
