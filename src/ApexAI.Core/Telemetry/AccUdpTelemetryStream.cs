using System.Net.Sockets;

namespace ApexAI.Core.Telemetry;

public sealed class AccUdpTelemetryStream : ITelemetryStream
{
    private readonly int _port;
    private readonly string _connectionPassword;
    private readonly string _commandPassword;
    private UdpClient? _client;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveTask;
    private AccSessionPacket? _session;
    private AccTrackPacket? _track;
    private AccCarPacket? _focusedCar;
    private bool _registered;
    private int _connectionId;
    private Guid _streamInstanceId = Guid.NewGuid();
    private DateTimeOffset _lastPacketAt;

    public AccUdpTelemetryStream(int port = 9000, string connectionPassword = "", string commandPassword = "")
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _connectionPassword = connectionPassword;
        _commandPassword = commandPassword;
    }

    public event EventHandler<TelemetrySnapshot>? SnapshotReceived;
    public bool IsRunning => _receiveTask is { IsCompleted: false };
    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        var client = new UdpClient();
        try
        {
            client.Connect("127.0.0.1", _port);
            _client = client;
            _cancellation = new CancellationTokenSource();
            _session = null;
            _track = null;
            _focusedCar = null;
            _streamInstanceId = Guid.NewGuid();
            _registered = false;
            _lastPacketAt = DateTimeOffset.MinValue;
            LastError = null;
            _receiveTask = Task.Run(() => ReceiveLoopAsync(client, _cancellation.Token));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _client?.Dispose();
        _client = null;
        _receiveTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose() => Stop();

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        await SendRegistrationAsync(client, cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var result = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                await ProcessPacketAsync(client, result.Buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!_registered || DateTimeOffset.UtcNow - _lastPacketAt > TimeSpan.FromSeconds(5))
                {
                    if (_registered)
                    {
                        LastError = "ACC broadcast stopped responding; retrying registration.";
                        _registered = false;
                        _session = null;
                        _track = null;
                        _focusedCar = null;
                    }
                    await SendRegistrationAsync(client, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
            catch (SocketException exception) when (!cancellationToken.IsCancellationRequested)
            {
                LastError = $"ACC UDP connection interrupted: {exception.Message}";
                _registered = false;
                _session = null;
                _track = null;
                _focusedCar = null;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                await SendRegistrationAsync(client, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SendRegistrationAsync(UdpClient client, CancellationToken cancellationToken)
    {
        var request = AccBroadcastingProtocol.CreateRegistrationRequest(
            "ApexAI", _connectionPassword, 250, _commandPassword);
        await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessPacketAsync(UdpClient client, byte[] payload, CancellationToken cancellationToken)
    {
        _lastPacketAt = DateTimeOffset.UtcNow;
        if (AccBroadcastingProtocol.TryParseRegistration(payload, out var registration))
        {
            _registered = registration.Success;
            _connectionId = registration.ConnectionId;
            LastError = registration.Success ? null : $"ACC registration failed: {registration.Error}";
            if (registration.Success)
            {
                await client.SendAsync(AccBroadcastingProtocol.CreateRequest(
                    AccBroadcastingProtocol.RequestEntryList, _connectionId), cancellationToken).ConfigureAwait(false);
                await client.SendAsync(AccBroadcastingProtocol.CreateRequest(
                    AccBroadcastingProtocol.RequestTrackData, _connectionId), cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (AccBroadcastingProtocol.TryParseSession(payload, out var sessionUpdate))
        {
            if (_session?.FocusedCarId != sessionUpdate.FocusedCarId)
                _focusedCar = null;
            _session = sessionUpdate;
            PublishFocusedCar();
            return;
        }

        if (AccBroadcastingProtocol.TryParseTrack(payload, out var trackUpdate))
        {
            _track = trackUpdate;
            PublishFocusedCar();
            return;
        }

        if (!AccBroadcastingProtocol.TryParseCar(payload, out var car) ||
            _session is not { } currentSession || car.CarId != currentSession.FocusedCarId)
            return;

        _focusedCar = car;
        PublishFocusedCar();
    }

    private void PublishFocusedCar()
    {
        if (_session is not { } currentSession || _focusedCar is not { } car ||
            car.CarId != currentSession.FocusedCarId) return;
        var currentTrack = _track;
        var hasCompletedLap = car.CompletedLaps > 0 && car.LastLapTimeMs > 0;
        SnapshotReceived?.Invoke(this, new TelemetrySnapshot(
            DateTimeOffset.UtcNow,
            AccBroadcastingProtocol.MapPhase(currentSession.Phase, currentSession.SessionType),
            car.CompletedLaps + (currentSession.Phase == 5 ? 1 : 0),
            Math.Clamp(car.SplinePosition, 0, 1),
            car.SpeedKph,
            null,
            null,
            null,
            null,
            null,
            car.CarLocation is 2 or 3 or 4,
            true,
            TelemetrySource.AccBroadcasting,
            $"{_streamInstanceId}:{_connectionId}:{currentSession.SessionIndex}",
            currentTrack?.Name,
            AccBroadcastingProtocol.MapSessionType(currentSession.SessionType),
            hasCompletedLap ? car.LastLapTimeMs : null,
            car.BestLapTimeMs > 0 ? car.BestLapTimeMs : null,
            hasCompletedLap && !car.LastLapInvalid && car.LastLapValidForBest,
            hasCompletedLap && car.LastLapOut,
            hasCompletedLap && car.LastLapIn));
    }
}
