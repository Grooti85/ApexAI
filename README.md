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

The **AI Mentor** is a real chat-completions integration, not a set of canned responses. It is **offline by default**. To use an online provider, open **Settings**, select **OpenAI-compatible**, enter its chat-completions endpoint and model, and provide the API key. For example, OpenAI's endpoint is `https://api.openai.com/v1/chat/completions`; use a model enabled for your account. The API key is stored separately from `settings.json` and protected with Windows DPAPI for the current Windows user. ApexAI does not log the key or include it in settings JSON.

Hosted AI requires the user's provider key and may incur usage charges under that provider's pricing. In each chat request ApexAI sends the question, recent conversation turns, and a compact context made from the locally persisted ACC session summaries, recorded lap times/validity, and analysis of the newest session. That context explicitly marks unsupported telemetry as unavailable. The AI provider receives that context; session history is otherwise kept local. The mentor is instructed not to invent telemetry or claim generic suggestions are derived from recorded data. Verify AI-generated coaching before acting on it.

For a no-key, free local option, install [Ollama](https://ollama.com/), download a model such as `qwen2.5` with `ollama pull qwen2.5`, then configure **OpenAI-compatible** with endpoint `http://localhost:11434/v1/chat/completions`, model `qwen2.5`, and a blank key. HTTP is accepted only for loopback local endpoints; non-local providers must use HTTPS. Local model quality and response speed depend on your hardware. Requests can be cancelled in the chat UI, and endpoint, provider, and timeout errors are shown rather than replaced with mock answers.

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

`AccUdpTelemetryStream` performs the ACC broadcasting handshake and binary packet parsing behind `ITelemetryStream`. The telemetry model keeps unavailable ACC fields nullable. `SessionRecorder` persists only ACC-sourced sessions and laps; `SessionAnalysis` calculates reports and selects the next mission from completed-lap evidence. `MentorContextBuilder` creates a bounded factual context from persisted session records, and `AiMentorChatService` sends it to a configured OpenAI-compatible endpoint with cancellation and timeout handling. The WPF dashboard and optional overlay both consume the same telemetry snapshots. CI builds/tests on Windows and packages a self-contained `win-x64` archive.
