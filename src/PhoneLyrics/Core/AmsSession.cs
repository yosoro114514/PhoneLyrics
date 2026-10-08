using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Microsoft.Win32;

namespace PhoneLyrics;

// Public Apple Media Service over an already paired Windows BLE connection.
// No periodic GATT reads: state is delivered by notifications; long attributes are read only when flagged truncated.
internal sealed class AmsSession : IAsyncDisposable
{
    private readonly TraceLog log;
    private readonly BluetoothLEDevice device;
    private readonly GattDeviceService service;
    private readonly GattSession connection;
    private readonly GattCharacteristic remote, update, attribute;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly object sync = new();
    private readonly Dictionary<(byte, byte), long> versions = new();
    private AmsSnapshot snapshot;
    private bool disposed;
    private long version;
    public event Action<AmsSnapshot>? Changed;
    public static double MonotonicSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public AmsSnapshot Snapshot { get { lock (sync) return snapshot; } }

    private AmsSession(TraceLog log, BluetoothLEDevice device, GattDeviceService service,
        GattSession connection, GattCharacteristic remote, GattCharacteristic update, GattCharacteristic attribute)
    {
        this.log = log; this.device = device; this.service = service; this.connection = connection;
        this.remote = remote; this.update = update; this.attribute = attribute;
        snapshot = new(device.Name, true, "", "", "", "", null, null, null, null, null, [], "等待手机媒体通知");
        update.ValueChanged += OnUpdate;
        remote.ValueChanged += OnCommands;
        device.ConnectionStatusChanged += OnConnection;
        device.GattServicesChanged += OnServicesChanged;
        SystemEvents.PowerModeChanged += OnPower;
    }

    public static async Task<AmsSession> ConnectAsync(TraceLog log, string? expectedDevice = null)
    {
        var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        var candidates = expectedDevice == null ? paired.ToArray() : paired.Where(d => d.Name == expectedDevice).ToArray();
        // Without an explicit device, choose only if the paired BLE set is unambiguous. Never use Phone Link for discovery.
        if (candidates.Length != 1) throw new InvalidOperationException("请用 --device-name 指定唯一的已配对 iPhone；不会自动选择多个蓝牙设备。");
        BluetoothLEDevice? device = null; GattDeviceService? service = null; GattSession? connection = null;
        AmsSession? session = null;
        try
        {
            device = await BluetoothLEDevice.FromIdAsync(candidates[0].Id).AsTask().WaitAsync(TimeSpan.FromSeconds(15))
                ?? throw new InvalidOperationException("无法访问已配对 BLE 设备。");
            connection = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            connection.MaintainConnection = true;
            var services = await device.GetGattServicesForUuidAsync(AmsUuids.Service, BluetoothCacheMode.Uncached).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count != 1)
            {
                foreach (var item in services.Services) item.Dispose();
                throw new InvalidOperationException($"AMS 服务不可用：{services.Status} / {services.ProtocolError}");
            }
            service = services.Services[0];
            var opened = await service.OpenAsync(GattSharingMode.SharedReadAndWrite).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            if (opened is not GattOpenStatus.Success and not GattOpenStatus.AlreadyOpened)
                throw new InvalidOperationException($"AMS 打开失败：{opened}");
            var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            if (chars.Status != GattCommunicationStatus.Success) throw new InvalidOperationException($"AMS 特征发现失败：{chars.Status}");
            GattCharacteristic Find(Guid uuid) => chars.Characteristics.Single(c => c.Uuid == uuid);
            session = new(log, device, service, connection, Find(AmsUuids.RemoteCommand), Find(AmsUuids.EntityUpdate), Find(AmsUuids.EntityAttribute));
            await session.SubscribeAsync(session.remote);
            await session.SubscribeAsync(session.update);
            // Register queue/track before player so the initial playback anchor follows initial track metadata.
            foreach (var interest in new byte[][] { [1, 0, 1, 2, 3], [2, 0, 1, 2, 3], [0, 0, 1, 2] })
                await WriteAsync(session.update, interest);
            // On a Windows connection shared with an earlier client, unchanged attributes can lack an initial notification.
            // Fill missing fields ONCE at connection time, then rely exclusively on notifications.
            foreach (var pair in new (byte Entity, byte Attribute)[] { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (2, 3), (0, 1) })
            {
                lock (session.sync)
                {
                    if (session.versions.ContainsKey(pair)) continue;
                    session.versions[pair] = 0;
                }
                await session.ReadFullAsync(pair.Entity, pair.Attribute, 0, initial: true);
            }
            log.Write("amsConnected", new { device = device.Name, service = AmsUuids.Service, eventDriven = true, periodicGattReads = 0,
                metadataViaPhoneLink = false, coldStartIndependenceVerified = false,
                deviceSelection = expectedDevice == null ? "OnlyPairedBleDevice" : "ExactDeviceName" });
            return session;
        }
        catch
        {
            if (session != null) await session.DisposeAsync();
            else { connection?.Dispose(); service?.Dispose(); device?.Dispose(); }
            throw;
        }
    }

    private async Task SubscribeAsync(GattCharacteristic characteristic)
    {
        var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        log.Write("amsSubscription", new { characteristic = characteristic.Uuid, status = result.Status.ToString(), result.ProtocolError });
        if (result.Status != GattCommunicationStatus.Success) throw new InvalidOperationException($"AMS 通知订阅失败：{result.Status} / {result.ProtocolError}");
    }

    private static async Task WriteAsync(GattCharacteristic characteristic, byte[] data)
    {
        var result = await characteristic.WriteValueWithResultAsync(data.AsBuffer(), GattWriteOption.WriteWithResponse).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        if (result.Status != GattCommunicationStatus.Success) throw new InvalidOperationException($"AMS 写入失败：{result.Status} / {result.ProtocolError}");
    }

    private void OnCommands(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var commands = args.CharacteristicValue.ToArray().Select(x => (int)x).ToArray();
        lock (sync) { if (disposed) return; snapshot = snapshot with { SupportedCommands = commands }; }
        log.Write("amsSupportedCommands", new { commands });
        Publish();
    }

    private void OnUpdate(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var received = DateTimeOffset.UtcNow;
        var monotonic = MonotonicSeconds;
        var bytes = args.CharacteristicValue.ToArray();
        if (bytes.Length < 3) { log.Write("amsMalformedPacket", new { length = bytes.Length }); return; }
        var entity = bytes[0]; var attr = bytes[1]; var flags = bytes[2];
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        long current;
        lock (sync)
        {
            if (disposed) return;
            current = ++version; versions[(entity, attr)] = current;
            Apply(entity, attr, text, (flags & 1) != 0, monotonic, received);
        }
        log.Write("amsEntityUpdate", new { entity, attribute = attr, flags, value = text, receivedUtc = received, receivedMonotonicSeconds = monotonic });
        Publish();
        if ((flags & 1) != 0) _ = ReadFullAsync(entity, attr, current);
    }

    private void Apply(byte entity, byte attr, string text, bool truncated, double monotonic, DateTimeOffset received) =>
        snapshot = AmsProtocol.Apply(snapshot, entity, attr, text, truncated, monotonic, received);

    private async Task ReadFullAsync(byte entity, byte attr, long requestedVersion, bool initial = false)
    {
        var acquired = false;
        try
        {
            await writeGate.WaitAsync(stop.Token); acquired = true;
            lock (sync) { if (disposed || versions[(entity, attr)] != requestedVersion) return; }
            await WriteAsync(attribute, [entity, attr]);
            var result = await attribute.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            if (result.Status == GattCommunicationStatus.ProtocolError && result.ProtocolError == 0xA2)
            { log.Write("amsAttributeUnavailable", new { entity, attribute = attr, reason = "AbsentAttribute", oneTimeBootstrap = initial }); return; }
            if (result.Status != GattCommunicationStatus.Success) throw new InvalidOperationException($"AMS 完整字段读取失败：{result.Status} / {result.ProtocolError}");
            var text = Encoding.UTF8.GetString(result.Value.ToArray());
            lock (sync)
            {
                if (disposed || versions[(entity, attr)] != requestedVersion) return;
                Apply(entity, attr, text, false, MonotonicSeconds, DateTimeOffset.UtcNow);
            }
            log.Write(initial ? "amsInitialAttribute" : "amsFullAttribute", new { entity, attribute = attr, value = text,
                triggeredByTruncation = !initial, oneTimeBootstrap = initial });
            Publish();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!disposed) log.Write("amsFullAttributeFailed", new { entity, attribute = attr, error = ex.Message }); }
        finally { if (acquired) writeGate.Release(); }
    }

    private void OnConnection(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) Invalidate("蓝牙连接已断开，请重新连接");
    }
    private void OnServicesChanged(BluetoothLEDevice sender, object args) => Invalidate("手机 GATT 服务已改变，请重新连接");
    private void OnPower(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) Invalidate("电脑休眠，位置锚点已失效；恢复后请重新连接");
    }
    private void Invalidate(string reason)
    {
        lock (sync)
        {
            if (disposed) return;
            snapshot = snapshot with { Connected = false, Playback = null, AnchorMonotonicSeconds = null, AnchorReceivedUtc = null, SupportedCommands = [], UnavailableReason = reason };
        }
        log.Write("amsInvalidated", new { reason }); Publish();
    }
    private void Publish()
    {
        var current = Snapshot;
        log.Write("amsState", current);
        Changed?.Invoke(current);
    }

    public async Task SendAsync(string action, string expectedPlayer, bool allowUnknownCapabilities = false)
    {
        var command = AmsCommands.Id(action);
        await writeGate.WaitAsync(stop.Token);
        try
        {
            var state = Snapshot;
            if (disposed || !state.Connected || string.IsNullOrWhiteSpace(expectedPlayer) || state.Player != expectedPlayer ||
                (!state.SupportedCommands.Contains(command) && (!allowUnknownCapabilities || state.SupportedCommands.Length > 0)))
                throw new InvalidOperationException("AMS 来源已变化、已断开或未报告支持此操作；请核对来源后重试。");
            log.Write("amsControlRequest", new { action, device = state.Device, player = state.Player,
                capabilities = state.SupportedCommands.Length == 0 ? "UnknownExplicitDiagnostic" : "Advertised" });
            await WriteAsync(remote, [command]);
            log.Write("amsControlResult", new { action, delivered = true, actualPhoneEffect = "NotObserved" });
        }
        finally { writeGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        stop.Cancel();
        update.ValueChanged -= OnUpdate; remote.ValueChanged -= OnCommands;
        device.ConnectionStatusChanged -= OnConnection; device.GattServicesChanged -= OnServicesChanged;
        SystemEvents.PowerModeChanged -= OnPower;
        // Do not disable the shared physical CCCD: Phone Link or another client may still own a subscription.
        if (await writeGate.WaitAsync(TimeSpan.FromSeconds(16))) writeGate.Release();
        connection.Dispose(); service.Dispose(); device.Dispose();
        log.Write("amsDisposed", new { periodicGattReads = 0 });
    }
}
