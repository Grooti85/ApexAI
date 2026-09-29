using System.Text;
using ApexAI.Core.Telemetry;
using Xunit;

namespace ApexAI.Core.Tests;

public sealed class AccBroadcastingProtocolTests
{
    [Fact]
    public void BuildsAccRegistrationHandshakeWithProtocolVersionAndCredentials()
    {
        var packet = AccBroadcastingProtocol.CreateRegistrationRequest("ApexAI", "connection", 250, "command");
        using var reader = new BinaryReader(new MemoryStream(packet));

        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(4, reader.ReadByte());
        Assert.Equal("ApexAI", ReadString(reader));
        Assert.Equal("connection", ReadString(reader));
        Assert.Equal(250, reader.ReadInt32());
        Assert.Equal("command", ReadString(reader));
        Assert.Equal(0, reader.BaseStream.Position - reader.BaseStream.Length);
    }

    [Fact]
    public void ParsesSessionAndLocalCarPacketsFromAccBroadcastFormat()
    {
        var sessionBytes = CreateSessionPacket(sessionIndex: 12, sessionType: 10, phase: 5, focusedCarId: 3);
        Assert.True(AccBroadcastingProtocol.TryParseSession(sessionBytes, out var session));
        Assert.Equal((ushort)12, session.SessionIndex);
        Assert.Equal(3, session.FocusedCarId);
        Assert.Equal(SessionPhase.Race, AccBroadcastingProtocol.MapPhase(session.Phase, session.SessionType));

        var carBytes = CreateCarPacket(carId: 3, speedKph: 197, spline: 0.62f, laps: 4, lastLapMs: 104250);
        Assert.True(AccBroadcastingProtocol.TryParseCar(carBytes, out var car));
        Assert.Equal((ushort)3, car.CarId);
        Assert.Equal((ushort)197, car.SpeedKph);
        Assert.Equal(0.62f, car.SplinePosition);
        Assert.Equal((ushort)4, car.CompletedLaps);
        Assert.Equal(104250, car.LastLapTimeMs);
        Assert.False(car.LastLapInvalid);
    }

    [Fact]
    public void ParsesRegistrationAndTrackResponsesAndRejectsJson()
    {
        using var registration = new MemoryStream();
        using (var writer = new BinaryWriter(registration, Encoding.UTF8, true))
        {
            writer.Write((byte)1);
            writer.Write(42);
            writer.Write((sbyte)1);
            writer.Write((sbyte)0);
            WriteString(writer, string.Empty);
        }
        Assert.True(AccBroadcastingProtocol.TryParseRegistration(registration.ToArray(), out var result));
        Assert.True(result.Success);
        Assert.Equal(42, result.ConnectionId);

        using var trackStream = new MemoryStream();
        using (var writer = new BinaryWriter(trackStream, Encoding.UTF8, true))
        {
            writer.Write((byte)5);
            writer.Write(42);
            WriteString(writer, "Monza Circuit");
            writer.Write(3);
            writer.Write(5793);
        }
        Assert.True(AccBroadcastingProtocol.TryParseTrack(trackStream.ToArray(), out var track));
        Assert.Equal("Monza Circuit", track.Name);
        Assert.Equal(3, track.Id);
        Assert.False(AccBroadcastingProtocol.TryParseCar(
            Encoding.UTF8.GetBytes("""{"phase":"Race","lapNumber":4}"""), out _));
    }

    private static byte[] CreateSessionPacket(ushort sessionIndex, byte sessionType, byte phase, int focusedCarId)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)2);
        writer.Write((ushort)0);
        writer.Write(sessionIndex);
        writer.Write(sessionType);
        writer.Write(phase);
        writer.Write(1000f);
        writer.Write(60000f);
        writer.Write(focusedCarId);
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

    private static byte[] CreateCarPacket(ushort carId, ushort speedKph, float spline, ushort laps, int lastLapMs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)3);
        writer.Write(carId);
        writer.Write((ushort)0);
        writer.Write((byte)1);
        writer.Write((sbyte)4);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write((byte)1);
        writer.Write(speedKph);
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

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
}
