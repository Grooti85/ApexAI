# ApexAI

Windows desktop race dashboard and optional live overlay for Assetto Corsa Competizione (ACC).

**Product:** ApexAI
**Repository:** [Grooti85/ApexAI](https://github.com/Grooti85/apexai)

ApexAI does not drive the car, inject input, or automate gameplay. The main window is a dashboard for live session status, saved sessions, lap reports, and one evidence-based next mission. A separate compact, draggable, always-on-top overlay is optional.

## Requirements and build

- Windows 10/11
- .NET 8 SDK with Windows Desktop targeting support
- Visual Studio 2022 with the `.NET desktop development` workload, or the Windows .NET SDK

```powershell
dotnet restore ApexAI.sln
dotnet build ApexAI.sln --configuration Release
dotnet test tests/ApexAI.Core.Tests/ApexAI.Core.Tests.csproj --configuration Release
dotnet run --project src/ApexAI.Wpf/ApexAI.Wpf.csproj
```

The self-contained Windows release is `ApexAI-win-x64.zip`; it does not require a separately installed .NET runtime. Early releases may trigger Windows SmartScreen because they are not code-signed.

## ACC live telemetry setup

ApexAI uses ACC's **binary UDP broadcasting interface**, not arbitrary JSON or a generic UDP telemetry bridge. ACC must be configured to accept a local broadcasting client:

1. In ACC's `broadcasting.json` (normally `%USERPROFILE%\Documents\Assetto Corsa Competizione\Config\broadcasting.json`), enable the UDP listener and choose `updListenerPort`, `connectionPassword`, and `commandPassword`. The listener port defaults to `9000`.
2. In ApexAI **Settings**, enter the same port and passwords. The passwords are protected for the current Windows user with DPAPI; they are not written to `settings.json`.
3. Start ACC and enter a session. ApexAI registers as a version-4 broadcasting client, requests the entry list and track data, then reads the focused car's realtime packets.

The client retries registration while waiting for ACC. The dashboard displays **LIVE ACC** only after it receives valid binary ACC packets for the focused car. **Use demo data** is an explicit, visibly labelled mock mode; demo data is never saved as a real session.

### Data and report scope

The supported ACC broadcast packets supply session type/phase, focused car, track name, lap count/progress, speed, pit-lane location, last-lap validity/time, and best lap time. ApexAI stores completed timed laps locally in `%LOCALAPPDATA%\ApexAI\sessions.json`. A completed-session report derives the session best, improvement against the best recorded valid lap on the same track (when history exists), lap-time standard deviation, and a lap-validity summary. The next mission is selected only from those measurements; its reason is shown with it.

The standard ACC broadcasting interface does **not** supply fuel, tyre temperatures, steering/brake traces, or corner-by-corner telemetry. ApexAI leaves those values unavailable and does not generate advice about them. Sessions without enough valid laps are explicitly reported as a baseline rather than assigned invented comparisons. Session records are local and are not uploaded.

## Scope

AI mentor workflows, an AI provider settings surface, a skill tree, and a practice lab are not part of this initial coaching slice.

## Troubleshooting

- **Waiting for ACC:** verify that ACC's UDP broadcasting is enabled, the configured `updListenerPort` matches ApexAI's port, and both passwords match exactly. Allow ApexAI through Windows Firewall if prompted.
- **Registration/password error:** check the connection and command passwords in both ACC `broadcasting.json` and ApexAI settings.
- **No live updates in the dashboard:** start or join an ACC session. Demo mode remains explicitly marked and is not a substitute for ACC connectivity.
- **Session history unavailable:** check write access to `%LOCALAPPDATA%\ApexAI`; malformed history is reported instead of silently replaced.
- **Build fails with SDK not found:** install the .NET 8 SDK, not only the runtime. WPF builds need Windows Desktop targeting support.

## Architecture

`AccUdpTelemetryStream` performs the ACC broadcasting handshake and binary packet parsing behind `ITelemetryStream`. The telemetry model keeps unavailable ACC fields nullable. `SessionRecorder` persists only ACC-sourced sessions and laps; `SessionAnalysis` calculates reports and selects the next mission from completed-lap evidence. The WPF dashboard and optional overlay both consume those same snapshots. CI builds/tests on Windows and packages a self-contained `win-x64` archive.
