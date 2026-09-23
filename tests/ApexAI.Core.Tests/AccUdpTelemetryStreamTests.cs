using System.Text;
using ApexAI.Core.Telemetry;

namespace ApexAI.Core.Tests;

public sealed class AccUdpTelemetryStreamTests
{
    [Fact]
    public void ParsesDocumentedTelemetryPacket()
    {
        const string json = """
            {"phase":"Race","lapNumber":7,"lapProgress":0.42,"speedKph":181.5,
             "fuelLiters":24.2,"fuelPerLapLiters":3.1,"tyreTemperatureCelsius":96,
             "isOffTrack":false,"hasIncident":false,"isInPitLane":false}
            """;
        Assert.True(AccUdpTelemetryStream.TryParse(Encoding.UTF8.GetBytes(json), out var snapshot));
        Assert.Equal(SessionPhase.Race, snapshot.Phase);
        Assert.Equal(7, snapshot.LapNumber);
        Assert.Equal(181.5, snapshot.SpeedKph);
        Assert.True(snapshot.IsConnected);
    }

    [Fact]
    public void RejectsMalformedOrIncompletePacket()
    {
        Assert.False(AccUdpTelemetryStream.TryParse(Encoding.UTF8.GetBytes("not-json"), out _));
        Assert.False(AccUdpTelemetryStream.TryParse(Encoding.UTF8.GetBytes("""{"phase":"Race"}"""), out _));
    }
}
