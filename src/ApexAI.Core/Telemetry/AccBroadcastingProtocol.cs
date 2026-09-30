using System.Buffers.Binary;
using System.Text;

namespace ApexAI.Core.Telemetry;

public enum AccBroadcastingMessageType : byte
{
    RegistrationResult = 1,
    RealtimeUpdate = 2,
    RealtimeCarUpdate = 3,
    TrackData = 5
}

public sealed record AccSessionPacket(ushort SessionIndex, byte SessionType, byte Phase, int FocusedCarId);
public sealed record AccTrackPacket(string Name, int Id);
public sealed record AccCarPacket(
    ushort CarId,
    byte CarLocation,
    ushort SpeedKph,
    float SplinePosition,
    ushort CompletedLaps,
    int LastLapTimeMs,
    bool LastLapInvalid,
    bool LastLapValidForBest,
    bool LastLapOut,
    bool LastLapIn,
    int BestLapTimeMs);
public sealed record AccRegistrationPacket(int ConnectionId, bool Success, string Error);

public static class AccBroadcastingProtocol
{
    public const byte ProtocolVersion = 4;
    public const int RegisterCommandApplication = 1;
    public const int RequestEntryList = 10;
    public const int RequestTrackData = 11;

    public static byte[] CreateRegistrationRequest(
        string displayName,
        string connectionPassword,
        int updateIntervalMilliseconds,
        string commandPassword)
    {
        if (updateIntervalMilliseconds is < 50 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(updateIntervalMilliseconds));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)RegisterCommandApplication);
        writer.Write(ProtocolVersion);
        WriteString(writer, displayName);
        WriteString(writer, connectionPassword);
        writer.Write(updateIntervalMilliseconds);
        WriteString(writer, commandPassword);
        return stream.ToArray();
    }

    public static byte[] CreateRequest(int messageType, int connectionId)
    {
        if (messageType is not (RequestEntryList or RequestTrackData))
            throw new ArgumentOutOfRangeException(nameof(messageType));
        var packet = new byte[5];
        packet[0] = (byte)messageType;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(1), connectionId);
        return packet;
    }

    public static bool TryParseSession(ReadOnlySpan<byte> payload, out AccSessionPacket packet)
    {
        packet = default!;
        if (payload.Length == 0 || payload[0] != (byte)AccBroadcastingMessageType.RealtimeUpdate)
            return false;
        try
        {
            using var reader = OpenReader(payload);
            _ = reader.ReadByte();
            _ = reader.ReadUInt16();
            var sessionIndex = reader.ReadUInt16();
            var sessionType = reader.ReadByte();
            var phase = reader.ReadByte();
            _ = reader.ReadSingle();
            _ = reader.ReadSingle();
            var focusedCarId = reader.ReadInt32();
            _ = ReadString(reader);
            _ = ReadString(reader);
            _ = ReadString(reader);
            var replay = reader.ReadByte();
            if (replay != 0)
            {
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
            }
            _ = reader.ReadSingle();
            _ = reader.ReadSByte();
            _ = reader.ReadSByte();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            SkipLap(reader);
            packet = new AccSessionPacket(sessionIndex, sessionType, phase, focusedCarId);
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    public static bool TryParseCar(ReadOnlySpan<byte> payload, out AccCarPacket packet)
    {
        packet = default!;
        if (payload.Length == 0 || payload[0] != (byte)AccBroadcastingMessageType.RealtimeCarUpdate)
            return false;
        try
        {
            using var reader = OpenReader(payload);
            _ = reader.ReadByte();
            var carId = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadByte();
            _ = reader.ReadSByte();
            _ = reader.ReadSingle();
            _ = reader.ReadSingle();
            _ = reader.ReadSingle();
            var location = reader.ReadByte();
            var speedKph = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            var splinePosition = reader.ReadSingle();
            var laps = reader.ReadUInt16();
            _ = reader.ReadInt32();
            var bestLap = ReadLap(reader);
            var lastLap = ReadLap(reader);
            _ = ReadLap(reader);
            packet = new AccCarPacket(carId, location, speedKph, splinePosition, laps,
                lastLap.TimeMs, lastLap.Invalid, lastLap.ValidForBest, lastLap.Out, lastLap.In, bestLap.TimeMs);
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    public static bool TryParseRegistration(ReadOnlySpan<byte> payload, out AccRegistrationPacket packet)
    {
        packet = default!;
        if (payload.Length == 0 || payload[0] != (byte)AccBroadcastingMessageType.RegistrationResult)
            return false;
        try
        {
            using var reader = OpenReader(payload);
            _ = reader.ReadByte();
            var connectionId = reader.ReadInt32();
            var success = reader.ReadSByte() == 1;
            _ = reader.ReadSByte();
            packet = new AccRegistrationPacket(connectionId, success, ReadString(reader));
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    public static bool TryParseTrack(ReadOnlySpan<byte> payload, out AccTrackPacket packet)
    {
        packet = default!;
        if (payload.Length == 0 || payload[0] != (byte)AccBroadcastingMessageType.TrackData)
            return false;
        try
        {
            using var reader = OpenReader(payload);
            _ = reader.ReadByte();
            _ = reader.ReadInt32();
            var name = ReadString(reader);
            var id = reader.ReadInt32();
            _ = reader.ReadInt32();
            packet = new AccTrackPacket(name, id);
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    public static SessionPhase MapPhase(byte phase, byte sessionType) => phase switch
    {
        6 or 7 or 8 => SessionPhase.Finished,
        5 when sessionType == 0 => SessionPhase.Practice,
        5 when sessionType is 4 or 9 => SessionPhase.Qualifying,
        5 when sessionType == 10 => SessionPhase.Race,
        5 => SessionPhase.Practice,
        _ => SessionPhase.Garage
    };

    public static string MapSessionType(byte sessionType) => sessionType switch
    {
        0 => "Practice",
        4 => "Qualifying",
        9 => "Superpole",
        10 => "Race",
        11 => "Hotlap",
        12 => "Hotstint",
        13 => "Hotlap superpole",
        14 => "Replay",
        _ => "Unknown"
    };

    private static BinaryReader OpenReader(ReadOnlySpan<byte> payload) =>
        new(new MemoryStream(payload.ToArray(), false), Encoding.UTF8, false);

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void SkipLap(BinaryReader reader) => _ = ReadLap(reader);

    private static (int TimeMs, bool Invalid, bool ValidForBest, bool Out, bool In) ReadLap(BinaryReader reader)
    {
        var time = reader.ReadInt32();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        var splitCount = reader.ReadByte();
        for (var i = 0; i < splitCount; i++) _ = reader.ReadInt32();
        var invalid = reader.ReadByte() != 0;
        var validForBest = reader.ReadByte() != 0;
        var outLap = reader.ReadByte() != 0;
        var inLap = reader.ReadByte() != 0;
        return (time, invalid, validForBest, outLap, inLap);
    }
}
