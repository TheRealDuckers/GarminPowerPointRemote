using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace PptRemoteServer;

internal enum ServerState
{
    Stopped,
    Starting,
    Advertising,
    Error
}

/// <summary>
/// BLE peripheral exposing one writable "command" characteristic. Every write must be a
/// PIN-signed packet (see Auth.cs). Events fire on background threads.
/// </summary>
internal sealed class GattServer
{
    public event Action<ServerState, string>? StateChanged;
    public event Action<bool, string>? WatchConnectionChanged;
    public event Action<byte>? CommandReceived;
    public event Action<AuthResult>? CommandRejected;
    public event Action<uint>? CounterAdvanced;
    public event Action<string>? Message;

    private readonly AuthState _auth;
    private GattServiceProvider? _provider;
    private GattLocalCharacteristic? _characteristic;
    private GattSession? _session;
    private string? _sessionId;
    private volatile bool _stopping;
    private System.Threading.Timer? _watchdog;
    private long _lastSeenTicks;
    private long _lastRejectTicks;
    private static readonly TimeSpan WatchTimeout = TimeSpan.FromSeconds(30);

    public GattServer(AuthState auth)
    {
        _auth = auth;
    }

    public async Task StartAsync()
    {
        _stopping = false;
        Report(ServerState.Starting, "Setting up Bluetooth...");

        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
            {
                Report(ServerState.Error, "No Bluetooth adapter found.");
                return;
            }
            if (!adapter.IsPeripheralRoleSupported)
            {
                Report(ServerState.Error, "This Bluetooth adapter does not support the BLE peripheral role.");
                return;
            }

            var created = await GattServiceProvider.CreateAsync(Protocol.ServiceUuid);
            if (created.Error != BluetoothError.Success)
            {
                Report(ServerState.Error, $"Could not create the GATT service: {created.Error}");
                return;
            }
            _provider = created.ServiceProvider;

            var parameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties =
                    GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
                WriteProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = "PowerPoint command"
            };
            var charResult = await _provider.Service.CreateCharacteristicAsync(
                Protocol.CommandCharacteristicUuid, parameters);
            if (charResult.Error != BluetoothError.Success)
            {
                Report(ServerState.Error, $"Could not create the characteristic: {charResult.Error}");
                return;
            }
            _characteristic = charResult.Characteristic;
            _characteristic.WriteRequested += OnWriteRequested;

            _provider.AdvertisementStatusChanged += OnAdvertisementStatusChanged;
            StartAdvertising();

            // The watch sends a signed heartbeat every 10 s; silence for 30 s means it is gone.
            // The same timer saves the packet counter to disk.
            _watchdog ??= new System.Threading.Timer(_ => CheckWatchdog(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Report(ServerState.Error, ex.Message);
        }
    }

    public void Stop()
    {
        _stopping = true;
        _watchdog?.Dispose();
        _watchdog = null;
        FlushCounter();
        try
        {
            _provider?.StopAdvertising();
        }
        catch
        {
            // Already stopped.
        }
        if (_session is not null)
        {
            _session.SessionStatusChanged -= OnSessionStatusChanged;
        }
        Report(ServerState.Stopped, "Stopped");
    }

    private void StartAdvertising()
    {
        _provider?.StartAdvertising(new GattServiceProviderAdvertisingParameters
        {
            IsConnectable = true,
            IsDiscoverable = true
        });
    }

    private async void OnAdvertisementStatusChanged(
        GattServiceProvider sender, GattServiceProviderAdvertisementStatusChangedEventArgs args)
    {
        var status = sender.AdvertisementStatus;
        Message?.Invoke($"Advertising status: {status}");

        if (status is GattServiceProviderAdvertisementStatus.Stopped
                   or GattServiceProviderAdvertisementStatus.Aborted)
        {
            if (_stopping)
            {
                return;
            }

            // Keep advertising so the watch can reconnect after a drop.
            // Don't flip the UI to "Starting" if a watch is still connected.
            if (_sessionId is null)
            {
                Report(ServerState.Starting, "Advertising stopped; restarting...");
            }
            await Task.Delay(2000).Wait(); if (_stopping) return;;
            if (!_stopping)
            {
                StartAdvertising();
            }
        }
        else if (status != GattServiceProviderAdvertisementStatus.Created)
        {
            Report(ServerState.Advertising, "Waiting for the watch");
        }
    }

    private async void OnWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null)
            {
                return;  // client already disconnected
            }

            var buffer = request.Value;
            var data = new byte[buffer.Length];
            if (buffer.Length > 0)
            {
                DataReader.FromBuffer(buffer).ReadBytes(data);
            }

            // Only PIN-signed packets get through; everything else is refused.
            var result = _auth.Check(data, out var command);
            if (result != AuthResult.Ok)
            {
                NoteRejected(result);
                if (request.Option == GattWriteOption.WriteWithResponse)
                {
                    request.RespondWithProtocolError(GattProtocolError.InsufficientAuthorization);
                }
                return;
            }

            Interlocked.Exchange(ref _lastSeenTicks, DateTime.UtcNow.Ticks);
            TrackSession(args.Session);

            // Acknowledge first so the watch's write completes quickly.
            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }

            CommandReceived?.Invoke(command);
        }
        catch (Exception ex)
        {
            Message?.Invoke($"Write handling failed: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    // Tells the UI about a rejected packet, at most once every 5 seconds.
    private void NoteRejected(AuthResult result)
    {
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _lastRejectTicks) < TimeSpan.TicksPerSecond * 5)
        {
            return;
        }
        Interlocked.Exchange(ref _lastRejectTicks, now);
        CommandRejected?.Invoke(result);
    }

    private void TrackSession(GattSession? session)
    {
        if (session is null)
        {
            return;
        }
        var id = session.DeviceId.Id;
        if (id == _sessionId)
        {
            return;
        }

        if (_session is not null)
        {
            _session.SessionStatusChanged -= OnSessionStatusChanged;
        }
        _session = session;
        _sessionId = id;
        session.SessionStatusChanged += OnSessionStatusChanged;
        WatchConnectionChanged?.Invoke(true, FriendlyId(id));
    }

    private void OnSessionStatusChanged(GattSession sender, GattSessionStatusChangedEventArgs args)
    {
        bool active = args.Status == GattSessionStatus.Active;
        if (!active)
        {
            _sessionId = null;  // so the next signed write registers the session again
        }
        WatchConnectionChanged?.Invoke(active, FriendlyId(sender.DeviceId.Id));
    }

    private void CheckWatchdog()
    {
        FlushCounter();

        var id = _sessionId;
        if (id is null)
        {
            return;
        }
        var idle = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastSeenTicks));
        if (idle > WatchTimeout)
        {
            _sessionId = null;  // the next signed command registers the watch again
            WatchConnectionChanged?.Invoke(false, FriendlyId(id));
        }
    }

    private void FlushCounter()
    {
        if (_auth.TryTakeDirty(out var counter))
        {
            CounterAdvanced?.Invoke(counter);
        }
    }

    private static string FriendlyId(string id)
    {
        var match = Regex.Match(id, @"([0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5})$");
        return match.Success ? match.Value.ToUpperInvariant() : id;
    }

    private void Report(ServerState state, string detail) => StateChanged?.Invoke(state, detail);
}
