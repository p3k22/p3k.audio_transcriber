# p3k.audio_transcriber

Live speech-to-text for Windows. Transcribes up to three audio sources simultaneously —
local microphone, a second audio stream delivered over TCP, and WASAPI loopback (whatever
Windows is playing) — each tagged independently on stdout.

Four interchangeable recognisers:

- **Parakeet** — NVIDIA Parakeet TDT 0.6B v2 (offline, VAD-segmented) via
  [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx). CPU-only, no Python.
- **Whisper** — a local [whisper.cpp](https://github.com/ggml-org/whisper.cpp) server,
  GPU-accelerated (AMD/Intel via Vulkan, NVIDIA via CUDA) or CPU fallback.
- **Streaming** — online Zipformer RNN-T via sherpa-onnx. No VAD; the model emits
  words as they are spoken and closes a segment on trailing silence.
- **Compare** — Parakeet and Whisper run in parallel; each segment produces two tagged
  lines so you can evaluate accuracy side-by-side.

Voice activity is detected with **Silero VAD** (Parakeet / Whisper / Compare modes).
On first run the app asks which engine to use and downloads everything it needs — models,
and for Whisper the server binary for your GPU — so a fresh machine goes from clone to
running with no manual setup.

Built to run standalone or as a silent child process driven by another application.

---

## How it works

**VAD mode** (Parakeet / Whisper / Compare):

```
mic ──────────────► VAD ──► segment ──► transcriber ──► [mic] text
                                             ▲
TCP :53123 ───────► VAD ──► segment ─────────┤            [phone] text
                                             │
loopback ─────────► VAD ──► segment ─────────┘            [pc] text
```

Each source gets its own VAD lane and its own recogniser instance, so talkers never
blur into one utterance and all sources decode in parallel.

**Streaming mode** (Zipformer):

```
mic ──────────────► streaming pipeline ──► [mic~] partial ... [mic] final
TCP :53123 ───────► streaming pipeline ──► [phone~] partial ... [phone] final
loopback ─────────► streaming pipeline ──► [pc~] partial ... [pc] final
```

No VAD — the model handles endpoint detection via trailing-silence rules and emits
partial updates while you speak.

---

## First run

The first time you launch interactively, it prompts for engine, microphone, TCP source,
and PC audio loopback:

```
Choose a transcription engine:
  1) Parakeet   - local, CPU, offline (VAD-segmented)  (default)
  2) Whisper    - whisper.cpp server, GPU-accelerated
  3) Streaming  - local, CPU, word-by-word (online Zipformer)
  4) Compare    - run both, one tagged line per engine (needs the whisper server)
Engine [1-4]: 2

Which whisper build matches this PC's GPU? (default: cpu)
  1) AMD     - Vulkan build (also works on any Vulkan GPU)
  2) NVIDIA  - CUDA build (needs a CUDA 12 runtime)
  3) CPU     - no GPU / other; slower but runs anywhere
GPU [1-3]: 1

Choose microphone:
  1) Microphone Array (Realtek)  (current, default)
Mic [1-1]:

Enable TCP audio source? (127.0.0.1:53123)
  1) No   (current, default)
  2) Yes
TCP [1-2]:

Enable PC audio loopback? (captures system audio, tag 'pc')
  1) No   (current, default)
  2) Yes
Loopback [1-2]:
```

Your choices are written back into `config.json`. The app then downloads whatever is
missing and starts listening.

**Later launches** show "use last settings" as the default — press Enter to keep
everything, or pick another option to switch:

```
Choose a transcription engine:
  1) Use last settings (whisper / amd)  (default)
  2) Parakeet   - local, CPU, offline (VAD-segmented)
  3) Whisper    - whisper.cpp server, GPU-accelerated
  4) Streaming  - local, CPU, word-by-word (online Zipformer)
  5) Compare    - run both, one tagged line per engine (needs the whisper server)
Engine [1-5]:
```

When launched **headless** (stdin redirected, i.e. as a managed child process) the
prompt is skipped: the saved choice is used, or Parakeet if nothing is saved yet.

---

## Auto-provisioning

Everything is fetched on demand and cached next to the exe; nothing is fetched twice.

| Asset | Source | Size | When |
|-------|--------|------|------|
| Silero VAD | sherpa-onnx releases | small | always |
| Parakeet models | Hugging Face | ~630 MB (int8) / ~2.4 GB (fp32) | Parakeet or Compare engine |
| Streaming model (Zipformer int8) | Hugging Face | ~73 MB | Streaming engine |
| Whisper model `ggml-large-v3-turbo.bin` | Hugging Face | ~1.6 GB | Whisper or Compare engine |
| Whisper server (AMD/Vulkan) | GitHub release ([whisper-vulkan-win-x64](https://github.com/p3k22/whisper-vulkan-win-x64)) — unmodified upstream build, no official Vulkan binary exists | ~18 MB | Whisper + `WhisperBuild: "amd"` |
| Whisper server (NVIDIA/CUDA) | official whisper.cpp release (cuBLAS 12.4) | ~460 MB | Whisper + `WhisperBuild: "nvidia"` |
| Whisper server (CPU) | official whisper.cpp release | ~4 MB | Whisper + `WhisperBuild: "cpu"` |

Layout relative to the exe:

```
models/parakeet/        Parakeet model files
models/silero_vad.onnx  VAD model
models/streaming/       Zipformer model files
models/whisper/         ggml-large-v3-turbo.bin
whisper/amd/            whisper-server.exe + DLLs  (AMD Vulkan)
whisper/nvidia/         whisper-server.exe + DLLs  (NVIDIA CUDA)
whisper/cpu/            whisper-server.exe + DLLs  (CPU)
```

If the Whisper server can't be provisioned or reached and `FallbackToParakeet` is true,
the app falls back to Parakeet so it always comes up. If a whisper.cpp server is already
answering at `WhisperServerUrl`, the app uses it as-is and manages nothing.

---

## Requirements

- **Windows x64**
- **.NET 10 runtime** (or publish `--self-contained` to bundle it)
- A microphone
- Internet on first run for asset downloads
- For Whisper GPU builds: a Vulkan-capable GPU (AMD) or NVIDIA card with CUDA 12

---

## Build & run

```powershell
dotnet build
dotnet run

# or run the built exe directly
bin\Debug\net10.0\win-x64\p3k.audio_transcriber.exe
```

First run writes a default `config.json` next to the exe, prompts for settings,
downloads what's needed, then starts listening.

```
Loaded config: ...\config.json
Selected: whisper (amd).
Checking dependencies ...
  [ok]  silero_vad.onnx
Launching whisper-server (...\whisper\amd\whisper-server.exe) ...
whisper-server ready at http://127.0.0.1:8080.
Engine: whisper (http://127.0.0.1:8080) + Silero VAD ...
Mic: Microphone Array  (16000 Hz, 1 ch, tag 'mic')
Loopback: default playback device  (tag 'pc')
READY
[14:22:07] [mic] Hello world.
[14:22:09] [pc] And this is the TV in the background.
```

Press **Ctrl+C** to stop.

### Command-line flags

| Flag | Effect |
|------|--------|
| `arg[0]` (non-flag) | Path to `config.json` — overrides the default location next to the exe |
| `--fp32` | Force the fp32 Parakeet model regardless of the `Precision` config setting |

### Self-contained publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

---

## Configuration (`config.json`)

Lives next to the exe. Pass an explicit path as `arg[0]` to override. Written with
documented defaults on first run; `//` comments are allowed and the file is re-read on
`Restart()`. Relative paths are resolved from the exe's directory.

```jsonc
{
  "Audio": {
    "SampleRate": 16000,        // Parakeet + VAD require 16000 — don't change
    "BitDepth": 16,             // only 16-bit PCM is supported
    "Channels": 1,              // 1 = mono
    "BufferMilliseconds": 100,  // mic capture chunk size; lower = less latency, more CPU
    "DeviceNumber": 0,          // NAudio input device index (0 = system default)
    "Label": "mic",             // stdout tag for mic transcriptions
    "Window": false             // true = open a dedicated console window for this source
  },
  "Parakeet": {
    "ParakeetDir": "models/parakeet",  // folder holding the model files (relative to exe)
    "Precision": "fp32",        // "fp32" (~2.4 GB, higher accuracy) | "int8" (~630 MB, faster)
    "FeatureDim": 80,
    "ModelType": "nemo_transducer",
    "NumThreads": 6,            // inference threads — match your physical core count
    "Provider": "cpu",
    "Debug": 0,                 // 1 = verbose sherpa-onnx logging
    "DecodingMethod": "greedy_search"  // "greedy_search" (fast) | "modified_beam_search"
  },
  "Vad": {
    "Model": "models/silero_vad.onnx",
    "Provider": "cpu",
    "Threshold": 0.5,           // speech probability cutoff (0..1); higher = less sensitive
    "MinSilenceDuration": 0.6,  // seconds of silence that ends an utterance
    "MinSpeechDuration": 0.25,  // segments shorter than this are discarded (rejects clicks/noise)
    "MaxSpeechDuration": 20.0,  // force-cut anything longer than this
    "PreRollDuration": 0.3,     // seconds kept before VAD onset so sentence starts aren't clipped
    "BufferSizeInSeconds": 30.0,
    "WindowSize": 512,
    "NumThreads": 1
  },
  "Streaming": {
    "StreamingDir": "models/streaming", // Zipformer model files (relative to exe)
    "NumThreads": 6,
    "Provider": "cpu",
    "DecodingMethod": "modified_beam_search",
    "EnableEndpoint": 1,
    "Rule1MinTrailingSilence": 2.4,  // seconds of silence that always closes a segment
    "Rule2MinTrailingSilence": 1.2,  // seconds of silence for longer utterances (see Rule3)
    "Rule3MinUtteranceLength": 20.0  // minimum utterance length (s) before Rule2 kicks in
  },
  "TcpSource": {
    "Enabled": false,           // true = also accept PCM over TCP
    "Host": "127.0.0.1",        // loopback only
    "Port": 53123,
    "Label": "phone",           // stdout tag for transcriptions from this source
    "Window": false,            // true = open a dedicated console window for this source
    "Vad": {                    // VAD tuning for this source only; null = reuse the main Vad block
      "Threshold": 0.35,        // lower than the mic = more sensitive (phone audio arrives quieter)
      ...
    }
  },
  "Loopback": {
    "Enabled": false,           // true = also transcribe system audio output
    "Label": "pc",              // stdout tag for transcriptions from this source
    "DeviceIndex": -1,          // -1 = default playback device (auto-follows device changes)
                                // 0, 1, 2... = specific render device (listed on stderr at startup)
    "Window": false,            // true = open a dedicated console window for this source
    "Vad": {                    // VAD tuned for mixed audio (speech + music/effects)
      "Threshold": 0.1,         // much lower than mic — loopback is speech mixed with background
      "MinSilenceDuration": 0.2,
      "MinSpeechDuration": 0.1,
      "MaxSpeechDuration": 6.0,
      "BufferSizeInSeconds": 60.0
    }
  },
  "RemoteLog": {
    "Url": "",                  // blank = disabled. HTTPS endpoint to POST each transcription line to.
    "Token": ""                 // Bearer token sent as Authorization header. Generate: openssl rand -hex 32
  },
  "Engine": {
    "Type": "",                 // "" = ask on first run | "parakeet" | "whisper" | "streaming" | "compare"
    "WhisperBuild": "",         // "amd" (Vulkan) | "nvidia" (CUDA) | "cpu"
    "WhisperServerUrl": "http://127.0.0.1:8080",
    "FallbackToParakeet": true, // fall back to Parakeet if the whisper server can't be reached
    "ServerExePath": "",        // explicit whisper-server.exe path; blank = auto-download
    "ServerModelPath": "",      // explicit ggml model path; blank = auto-download
    "ServerThreads": 6,
    "ServerStartTimeoutSeconds": 60
  }
}
```

**Switching engine/build:** re-run interactively and pick from the menu, or edit
`Engine.Type` / `Engine.WhisperBuild` directly and restart.

**Storing models elsewhere:** `ParakeetDir`, `StreamingDir`, `ServerExePath`, and
`ServerModelPath` accept absolute paths. If the path already exists it is used as-is and
nothing is downloaded.

All settings are validated on startup. Invalid values are reported on stderr and the
app exits with code `1`.

---

## WASAPI loopback source

When `Loopback.Enabled` is `true`, the app captures whatever Windows is playing through
the render device — speakers, headphones, HDMI audio, virtual cables — and runs it
through the same VAD + transcriber pipeline as the mic.

**Default device (`DeviceIndex: -1`):** A background thread polls the Windows default
render device every 300 ms. If it changes (e.g. you switch in Volume2 or Sound settings),
the capture is torn down and reopened on the new device automatically. You will see on
stderr:

```
Loopback: default device changed, restarting...
Loopback: restarted on new default playback device.
```

**Specific device (`DeviceIndex: 0, 1, 2…`):** Available render devices are listed on
stderr at startup. The capture stays on that device regardless of default device changes.

Audio is resampled from the system's native format (typically 48 kHz stereo float) to the
pipeline sample rate (16 kHz mono) automatically.

The loopback VAD defaults are tuned for mixed audio: the threshold is much lower (0.1 vs
0.5 for mic) because speech probability is diluted when blended with music or effects.
Set `"Vad": null` to inherit the main `Vad` block instead.

---

## TCP audio source

When `TcpSource.Enabled` is `true`, the app opens a loopback TCP listener on `Host:Port`
and accepts one producer at a time. The producer must stream raw **16-bit little-endian
mono PCM** at the same sample rate as `Audio.SampleRate` (default 16000 Hz). The app
knows nothing about what sends the PCM.

The producer can connect, disconnect, and reconnect at any time without affecting the mic
or loopback pipelines. Give this source its own VAD tuning via `TcpSource.Vad`
(null = reuse the main `Vad` block).

---

## Display windows

Any source can open a dedicated console window by setting `"Window": true` in its config
block (`Audio.Window`, `TcpSource.Window`, `Loopback.Window`). The main process spawns a
copy of itself with `--display` and pipes lines to it over a named pipe. Each window has
its own cursor state, so partial-update overwriting (streaming mode) works cleanly even
when multiple sources are active.

---

## Remote logging

Set `RemoteLog.Url` to an HTTPS endpoint and each transcription line is fire-and-forget
POSTed as `text/plain` with a Bearer token in the `Authorization` header. Failures are
logged to stderr and never block transcription.

Generate a token — any high-entropy secret works; a 32-byte hex string is plenty:

```bash
openssl rand -hex 32
```

Put the **same** value in two places: `RemoteLog.Token` in `config.json` (client side) and the
`YOUR_TOKEN` check in `log.php` (server side). Leaving `RemoteLog.Url` blank disables the
feature entirely.

VPS side (`log.php`):

```php
<?php
if ($_SERVER['HTTP_AUTHORIZATION'] !== 'Bearer YOUR_TOKEN') { http_response_code(403); exit; }
file_put_contents('/var/log/transcriptions.log', file_get_contents('php://input') . "\n", FILE_APPEND);
http_response_code(204);
```

Watch the log live:

```bash
tail -f /var/log/transcriptions.log
```

---

## I/O contract

| Channel    | Direction | Content |
|------------|-----------|---------|
| `arg[0]`   | in        | optional path to `config.json` |
| `--fp32`   | in        | force fp32 Parakeet model regardless of config |
| **stdout** | out       | transcriptions only — `[HH:mm:ss] [label] text` |
| **stderr** | out       | status/diagnostics; `READY` line once listening |
| **stdin**  | in        | `quit` or close pipe → graceful shutdown |
| exit code  | out       | `0` = clean exit, `1` = failed to start |

In streaming mode, partial updates are tagged `[label~]` and the final committed line
uses `[label]`. Remote logging only posts final lines.

---

## Embedding in another app

Run as a silent child process: redirect streams, wait for `READY` on stderr, then read
transcription lines from stdout. With stdin redirected, the engine picker is skipped —
the saved `Engine.Type` is used, defaulting to Parakeet if unset.

```csharp
var psi = new ProcessStartInfo
{
    FileName               = @"...\p3k.audio_transcriber.exe",
    Arguments              = $"\"{configPath}\"",
    UseShellExecute        = false,
    CreateNoWindow         = true,
    RedirectStandardOutput = true,
    RedirectStandardError  = true,
    RedirectStandardInput  = true,
};

var proc = Process.Start(psi)!;
proc.OutputDataReceived += (_, e) => { if (e.Data is not null) OnTranscript(e.Data); };
proc.ErrorDataReceived  += (_, e) =>
{
    if (e.Data == "READY") OnReady();
    else if (e.Data is not null) OnLog(e.Data);
};
proc.BeginOutputReadLine();
proc.BeginErrorReadLine();

// stop gracefully:
proc.StandardInput.WriteLine("quit");
proc.WaitForExit(4000);
```

> On first run `READY` can be minutes away while models are downloaded. Always wait
> for it before treating the process as live.

---

## Project layout

```
Program.cs                   Entry point: start, wait for Ctrl+C / "quit", stop.
                             Also handles --display <pipe> <title> for child console windows.
Runtime/
  TranscriptionSystem.cs     Runtime entry point: Start() / Stop() / Restart().
  EngineRuntime.cs           Resolves Parakeet/Whisper/Streaming/Compare and owns spawned whisper-server.
  SourceRuntime.cs           Wires mic/TCP/loopback lanes to VAD or streaming pipelines and transcribers.
  RuntimeValidator.cs        Validates loaded config before sources start.
  TranscriptEmitter.cs       Formats stdout lines, posts optional remote logs, manages display windows.
Provisioning/
  ModelManifest.cs           Lists Parakeet + VAD + Streaming model files required by the selected mode.
  DependencyProvisioner.cs   Detects and downloads missing runtime model files.
Net/
  HttpDownload.cs            File and zip download/extract helper.
  RemoteLogger.cs            Fire-and-forget HTTP POST of transcription lines.
Configs/
  AppConfig.cs               Root config record.
  WavInEventConfig.cs        Mic capture settings.
  ParakeetConfig.cs          Recogniser settings; derives model file paths from ParakeetDir.
  StreamingConfig.cs         Online Zipformer settings; derives model file paths from StreamingDir.
  VadConfig.cs               Silero VAD settings.
  TcpSourceConfig.cs         TCP audio source settings (host, port, per-source VAD).
  LoopbackConfig.cs          WASAPI loopback settings (device index, per-source VAD).
  EngineConfig.cs            Engine selection + whisper server/build settings.
  RemoteLogConfig.cs         Remote log endpoint + Bearer token.
  ConfigLoader.cs            Reads/writes config.json.
  AppPaths.cs                Resolves relative paths from the exe directory.
Setup/
  EngineSelection.cs         Interactive startup picker; persists choices to config.json.
Audio/
  WavInFactory.cs            Validates config and builds the NAudio mic capture.
  WasapiLoopbackSource.cs    WASAPI loopback capture; polls for default device changes and auto-restarts.
  SourcePipeline.cs          Per-source queue + VAD worker thread + forced-cut strategy for loopback.
  StreamingPipeline.cs       Per-source streaming pipeline (no VAD); word-by-word partial + final output.
  PcmTcpSource.cs            Loopback TCP listener; turns PCM bytes into float frames.
  PreRollBuffer.cs           Keeps audio before VAD onset so sentence starts aren't clipped.
  WavBytes.cs                Encodes a segment as WAV bytes for the whisper server.
Display/
  SharedConsole.cs           Thread-safe console writer; coordinates partial-update overwriting across sources.
  ConsoleWindow.cs           Spawns a child console window and pipes lines to it over a named pipe.
Vad/
  VadFactory.cs              Validates config and builds the VoiceActivityDetector.
Transcription/
  ISegmentTranscriber.cs     Engine interface.
  ParakeetFactory.cs         Validates config and builds the OfflineRecognizer.
  ParakeetTranscriber.cs     In-process Parakeet recogniser.
  WhisperServerTranscriber.cs  POSTs each segment to the whisper.cpp server.
  ComparisonTranscriber.cs   Runs both engines in parallel and tags each line.
  WhisperProvisioner.cs      Downloads the whisper binary + model on first use.
  WhisperServerProcess.cs    Spawns and owns the whisper-server child process.
  StreamingFactory.cs        Validates config and builds the streaming Zipformer recogniser.
  JobObject.cs               Windows job object so the server dies with this process.
```

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `Failed to start` | Read stderr — it names the invalid config field or missing dependency |
| Download fails | Check connectivity to `huggingface.co`, `github.com`; delete any `.part` files and rerun |
| Whisper falls back to Parakeet | stderr says why (server unreachable, binary/model missing, or `WhisperBuild` blank) |
| Want to change engine or GPU build | Re-run interactively or edit `Engine.Type` / `Engine.WhisperBuild` and restart |
| Wrong microphone | Re-run interactively and pick from the mic list, or set `Audio.DeviceNumber` directly |
| Cuts off too early | Raise `Vad.MinSilenceDuration` (default 0.6 s) |
| Waits too long to transcribe | Lower `Vad.MinSilenceDuration` |
| TCP source produces no output | Check the producer is connecting to the correct `TcpSource.Port` and sending 16-bit mono PCM at 16000 Hz |
| Loopback produces no output | Confirm `Loopback.Enabled: true`; check stderr for the device name logged at startup |
| Loopback stopped after switching audio device | Should auto-restart within ~300 ms; check stderr for `Loopback: restarted` |
| Loopback on wrong device | Set `DeviceIndex` to the render device index shown on stderr at startup, or leave at -1 to follow the Windows default |
| Remote log not writing | Check stderr for `[remote-log]` errors; confirm the PHP process user owns the log file |
