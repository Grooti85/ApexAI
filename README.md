# ApexAI
AI race engineer for Assetto Corsa Competizione.

## Windows app

The desktop app is a .NET 8 WPF executable (`ApexAI.exe`) in `src/ApexAI.Wpf`. It is always on
top, transparent, draggable, and safe to use alongside ACC. It never drives
the game, injects input, or automates driving.

## One-click install (GitHub Releases)

Tagged releases publish `ApexAI-win-x64.zip`. Download it from the **Releases**
section of this GitHub profile, extract it anywhere, and run `ApexAI.exe`. The package is
self-contained and does not require installing .NET. Windows SmartScreen may
ask for confirmation because early releases are not code-signed.

### Requirements

- Windows 10/11
- .NET 8 SDK (the WPF project requires the Windows Desktop SDK)
- Visual Studio 2022 with the `.NET desktop development` workload, or the
  Windows .NET SDK

### Build and run from source

```powershell
dotnet restore ApexAI.sln
dotnet build ApexAI.sln
dotnet test tests/ApexAI.Core.Tests/ApexAI.Core.Tests.csproj
dotnet run --project src/ApexAI.Wpf/ApexAI.Wpf.csproj
```

## ACC telemetry

The app starts its UDP listener before showing the overlay and listens for
newline-independent JSON UDP packets on port `9000` by
default. Configure an ACC telemetry bridge/plugin to send packets with these
fields: `phase`, `lapNumber`, `lapProgress`, `speedKph`, `fuelLiters`,
`fuelPerLapLiters`, `tyreTemperatureCelsius`, `isOffTrack`, `hasIncident`, and
`isInPitLane`. The parser validates required fields and automatically shows mock mode when no recent packet has arrived. This keeps the app usable while
ACC is closed and leaves the transport isolated behind `ITelemetryStream`.
Start ApexAI before or after ACC: no game restart is needed. Once the bridge
begins sending packets, the overlay switches to `ACC CONNECTED` automatically.

ACC's built-in shared memory format varies by version and third-party plugin.
The UDP boundary is deliberate: a native/shared-memory adapter can be added
without changing the race model, detector, or overlay.

## Engineer configuration

The default provider is deterministic and offline. It is the recommended
starting mode and requires no account or network access. The core also exposes
an OpenAI-compatible provider for a future settings UI/configuration layer:

- Provider: `Offline` or `OpenAiCompatible`
- Endpoint: OpenAI-compatible `/v1/chat/completions` URL
- Model: provider model name
- API key: stored locally using Windows DPAPI under the current Windows user;
  it is never written to `settings.json`

If the provider is unavailable, times out, has no key, or returns an error, the
deterministic engineer message is shown instead.

## Troubleshooting

- **Overlay says MOCK MODE:** confirm the telemetry bridge is sending UDP to
  `127.0.0.1:9000` and that Windows Firewall allows the app.
- **No AI response:** verify the API key and endpoint; offline fallback is
  expected and safe.
- **Window is in the way:** drag it by any empty area and use `Close`.
- **Build fails with SDK not found:** install the .NET 8 SDK, not only the
  runtime. WPF builds require Windows Desktop targeting support.

## Development architecture

`ITelemetryProvider`/`ITelemetryStream` isolate ACC transport, `RaceState` and
`RaceEventDetector` provide deterministic domain logic, and
`IEngineerMessageService` is the seam for AI. CI runs on Windows and tagged
releases produce a self-contained `win-x64` archive.
