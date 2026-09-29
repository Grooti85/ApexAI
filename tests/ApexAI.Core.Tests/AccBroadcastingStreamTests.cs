using System.Net;
using System.Net.Sockets;
using System.Text;
using ApexAI.Core.Telemetry;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class AccBroadcastingStreamTests
{
    [Fact]
    public async Task RegistersWithAccRequestsMetadataAndEmitsFocusedCarAsLive()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receivedSnapshot = new TaskCompletionSource<TelemetrySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedFinishedSnapshot = new TaskCompletionSource<TelemetrySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updateCount = 0;
        using var stream = new AccUdpTelemetryStream(port);
        stream.SnapshotReceived += (_, snapshot) =>
        {
            if (Interlocked.Increment(ref updateCount) == 1)
                receivedSnapshot.TrySetResult(snapshot);
            else
                receivedFinishedSnapshot.TrySetResult(snapshot);
        };
        stream.Start();

        var registrationRequest = await server.ReceiveAsync(timeout.Token);
        Assert.Equal((byte)1, registrationRequest.Buffer[0]);
        Assert.Equal(AccBroadcastingProtocol.ProtocolVersion, registrationRequest.Buffer[1]);
        await server.SendAsync(RegistrationResult(42), registrationRequest.RemoteEndPoint);

        var entryListRequest = await server.ReceiveAsync(timeout.Token);
        var trackRequest = await server.ReceiveAsync(timeout.Token);
        Assert.Equal(AccBroadcastingProtocol.RequestEntryList, entryListRequest.Buffer[0]);
        Assert.Equal(AccBroadcastingProtocol.RequestTrackData, trackRequest.Buffer[0]);

        await server.SendAsync(SessionPacket(9, 10, 5, 3), entryListRequest.RemoteEndPoint);
        await server.SendAsync(TrackPacket("Monza Circuit", 3), entryListRequest.RemoteEndPoint);
        await server.SendAsync(CarPacket(2, 222, .4f, 2, 102500), entryListRequest.RemoteEndPoint);
        await server.SendAsync(CarPacket(3, 185, .4f, 2, 102500), entryListRequest.RemoteEndPoint);
        var snapshot = await receivedSnapshot.Task.WaitAsync(timeout.Token);

        Assert.Equal(TelemetrySource.AccBroadcasting, snapshot.Source);
        Assert.Equal("Monza Circuit", snapshot.TrackName);
        Assert.Equal(SessionPhase.Race, snapshot.Phase);
        Assert.Equal(185, snapshot.SpeedKph);
        Assert.Equal(102500, snapshot.LastLapTimeMs);
        Assert.Null(snapshot.FuelLiters);
        Assert.Null(snapshot.TyreTemperatureCelsius);

        await server.SendAsync(SessionPacket(9, 10, 6, 3), entryListRequest.RemoteEndPoint);
        var finishedSnapshot = await receivedFinishedSnapshot.Task.WaitAsync(timeout.Token);
        Assert.Equal(SessionPhase.Finished, finishedSnapshot.Phase);
    }

    private static byte[] RegistrationResult(int connectionId)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)1);
        writer.Write(connectionId);
        writer.Write((sbyte)1);
        writer.Write((sbyte)0);
        WriteString(writer, string.Empty);
        return stream.ToArray();
    }

    private static byte[] SessionPacket(ushort index, byte type, byte phase, int focusedCar)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)2);
        writer.Write((ushort)0);
        writer.Write(index);
        writer.Write(type);
        writer.Write(phase);
        writer.Write(1000f);
        writer.Write(60000f);
        writer.Write(focusedCar);
        WriteString(writer, string.Empty);
        WriteString(writer, string.Empty);
        WriteString(writer, string.Empty);
        writer.Write((byte)0);
        writer.Write(12f);
        writer.Write((sbyte)20);
        writer.Write((sbyte)25);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        WriteLap(writer, 95000);
        return stream.ToArray();
    }

    private static byte[] TrackPacket(string name, int id)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)5);
        writer.Write(42);
        WriteString(writer, name);
        writer.Write(id);
        writer.Write(5793);
        return stream.ToArray();
    }

    private static byte[] CarPacket(ushort id, ushort speed, float spline, ushort laps, int lastLapMs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)3);
        writer.Write(id);
        writer.Write((ushort)0);
        writer.Write((byte)1);
        writer.Write((sbyte)4);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write((byte)1);
        writer.Write(speed);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write(spline);
        writer.Write(laps);
        writer.Write(0);
        WriteLap(writer, 93000);
        WriteLap(writer, lastLapMs);
        WriteLap(writer, 60000);
        return stream.ToArray();
    }

    private static void WriteLap(BinaryWriter writer, int timeMs)
    {
        writer.Write(timeMs);
        writer.Write((ushort)3);
        writer.Write((ushort)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)0);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
}
