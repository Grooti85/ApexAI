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

Choose **Set up ACC** in the dashboard to detect and update the existing
`broadcasting.json` (normally
`%USERPROFILE%\Documents\Assetto Corsa Competizione\Config\broadcasting.json`).
ApexAI preserves unrelated JSON fields, keeps a valid configured port and
password, and repairs only invalid/missing required values with port `9000`
and a securely generated connection password. A valid password already in the
ACC config is reused; if it is blank, ApexAI reuses its saved DPAPI password
or generates one. Non-empty ACC connection and command passwords are
protected locally with Windows DPAPI. ACC stores its required copy of these
passwords in `broadcasting.json` as plain text.

Before changing the file, ApexAI writes a timestamped backup beside it. If
`broadcasting.json` is missing, ApexAI will not create an incomplete,
version-specific ACC config: launch ACC once, close it, then use **Set up ACC**
again. When setup changes the config and ACC is already running, restart ACC
to apply it. ApexAI updates its listener and keeps retrying, so it does not
need to restart; start or join an ACC session to receive live data.

ApexAI shows **LIVE ACC** only after valid native ACC broadcast packets arrive.
**Use demo data** is an explicit, visibly labelled mock mode; demo data is
never saved as a real session.

### Data and report scope

The supported ACC broadcast packets supply session type/phase, focused car, track name, lap count/progress, speed, pit-lane location, last-lap validity/time, and best lap time. ApexAI stores completed timed laps locally in `%LOCALAPPDATA%\ApexAI\sessions.json`. A completed-session report derives the session best, improvement against the best recorded valid lap on the same track (when history exists), lap-time standard deviation, and a lap-validity summary. The next mission is selected only from those measurements; its reason is shown with it.

The standard ACC broadcasting interface does **not** supply fuel, tyre temperatures, steering/brake traces, or corner-by-corner telemetry. ApexAI leaves those values unavailable and does not generate advice about them. Sessions without enough valid laps are explicitly reported as a baseline rather than assigned invented comparisons. Session records are local and are not uploaded.

## AI Mentor

The **AI Mentor** is a real local chat integration and is **local-first by default**. On first launch, ApexAI offers an optional setup wizard that, only after your consent, downloads and runs the Ollama runtime and the Qwen 2.5 3B model. You do not need an account, API key, separate Ollama install, or hosted AI subscription. No model or installer is downloaded until you choose **Set up free local AI** and confirm consent. The installer comes from `ollama.com` over HTTPS; ApexAI checks its Windows Authenticode signature and Ollama publisher before running it. Ollama downloads the model from its registry over HTTPS and reports SHA-256 layer digests; model weights do not have a separate publisher signature.

The setup requires **64-bit Windows**, an x64 CPU with **AVX2**, at least **8 GB system RAM**, and **8 GB free disk space** (the model download is about 2 GB, with extra space for the runtime and temporary files). These conservative minimum checks are not a performance guarantee. CPU/GPU capability, memory available to other apps, and power/thermal limits affect speed; slower or unsupported machines may not provide a usable experience. The wizard shows progress, allows cancellation and retry, and explains common download, signature, runtime, and disk-space failures. Local model requests are restricted to the loopback Ollama service. ApexAI sends no model chat requests to a hosted provider in local mode and does not attach a stored hosted-provider key to local requests.

The mentor distinguishes setup-required, missing-model, ready, and error states. Personalized driving coaching requires recorded ACC session telemetry. Before a session is recorded, the mentor can only offer clearly labeled general guidance. Chat context uses compact, locally persisted ACC session summaries, lap times/validity, and latest-session analysis; unsupported telemetry is marked unavailable. Inference uses the model on your device. Verify AI-generated coaching before acting on it.

Hosted providers are an **optional advanced setting**. Select **OpenAI-compatible** in Settings to configure a provider, model, and key. Hosted requests may incur charges under that provider's pricing. The key is stored separately from `settings.json` and protected with Windows DPAPI for the current Windows user; ApexAI does not log it or include it in settings JSON. HTTP is accepted only for loopback endpoints; remote providers must use HTTPS. Select **Offline** to disable AI. Requests can be cancelled, and provider and timeout errors are shown rather than replaced with mock answers.

## Scope

A skill tree and a practice lab are not part of this release.

## Troubleshooting

- **Waiting for ACC:** verify that ACC's UDP broadcasting is enabled, the configured `updListenerPort` matches ApexAI's port, and both passwords match exactly. Allow ApexAI through Windows Firewall if prompted.
- **Registration/password error:** check the connection and command passwords in both ACC `broadcasting.json` and ApexAI settings.
- **No live updates in the dashboard:** start or join an ACC session. Demo mode remains explicitly marked and is not a substitute for ACC connectivity.
- **Session history unavailable:** check write access to `%LOCALAPPDATA%\ApexAI`; malformed history is reported instead of silently replaced.
- **Build fails with SDK not found:** install the .NET 8 SDK, not only the
  runtime. WPF builds require Windows Desktop targeting support.

## Architecture

`AccUdpTelemetryStream` performs the ACC broadcasting handshake and binary packet parsing behind `ITelemetryStream`. The telemetry model keeps unavailable ACC fields nullable. `SessionRecorder` persists only ACC-sourced sessions and laps; `SessionAnalysis` calculates reports and selects the next mission from completed-lap evidence. `MentorContextBuilder` creates a bounded factual context from persisted session records, and `AiMentorChatService` sends it to either the loopback local model or a configured optional hosted provider with cancellation and timeout handling. The WPF dashboard and optional overlay both consume the same telemetry snapshots. CI builds/tests on Windows and packages a self-contained `win-x64` archive.
