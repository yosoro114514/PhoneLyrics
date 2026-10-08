using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace PhoneLyrics;

internal static class AmsDiscovery
{
    public static async Task DiscoverAsync(TraceLog log, string? expectedDevice)
    {
        var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true))
            .AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        var matches = expectedDevice == null ? paired.ToArray() : paired.Where(d => d.Name == expectedDevice).ToArray();
        log.Write("amsPairedDiscovery", new { expectedDevice, pairedLeCount = paired.Count,
            exactNameMatches = matches.Length, newPairingAttempted = false });
        if (matches.Length != 1) throw new InvalidOperationException("请用 --device-name 指定唯一的已配对 iPhone。");
        using var device = await BluetoothLEDevice.FromIdAsync(matches[0].Id).AsTask().WaitAsync(TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("无法访问已配对 BLE 设备。");
        var result = await device.GetGattServicesForUuidAsync(AmsUuids.Service, BluetoothCacheMode.Uncached)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(20));
        log.Write("amsServices", new { name = device.Name, connection = device.ConnectionStatus.ToString(),
            status = result.Status.ToString(), result.ProtocolError, count = result.Services.Count });
        try
        {
            if (result.Status != GattCommunicationStatus.Success || result.Services.Count != 1)
                throw new InvalidOperationException($"AMS 服务不可用：{result.Status} / count={result.Services.Count}");
            var service = result.Services[0];
            var opened = await service.OpenAsync(GattSharingMode.SharedReadAndWrite).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            log.Write("amsOpenStatus", new { status = opened.ToString() });
            if (opened is not GattOpenStatus.Success and not GattOpenStatus.AlreadyOpened)
                throw new InvalidOperationException($"AMS 打开失败：{opened}");
            var characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            if (characteristics.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"AMS 特征发现失败：{characteristics.Status}");
            log.Write("amsCharacteristics", new { status = characteristics.Status.ToString(),
                characteristics = characteristics.Characteristics.Select(c => new { uuid = c.Uuid, properties = c.CharacteristicProperties.ToString() }).ToArray() });
            Console.WriteLine($"AMS: {result.Status}; connection={device.ConnectionStatus}");
            foreach (var characteristic in characteristics.Characteristics)
                Console.WriteLine($"{characteristic.Uuid} {characteristic.CharacteristicProperties}");
        }
        finally { foreach (var service in result.Services) service.Dispose(); }
    }
}
