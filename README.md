# p3k.audio_transcriber

Live multi-source speech-to-text for Windows.

Capture, transcribe, and tag up to three independent audio sources simultaneously:

- Microphone input
- TCP audio streams
- System audio (WASAPI loopback)

Supports multiple recognition engines, automatic model provisioning, GPU-accelerated Whisper, and fully headless operation for integration into larger applications.

---

## Features

### Audio Sources

- Microphone capture via NAudio
- TCP PCM audio ingestion
- WASAPI loopback capture of system audio
- Independent pipelines per source
- Source-specific labels and configuration

### Recognition Engines

- **Parakeet** — offline NVIDIA Parakeet TDT 0.6B v2
- **Whisper** — local whisper.cpp server
- **Streaming** — online Zipformer RNN-T
- **Compare** — Parakeet and Whisper side-by-side

### Runtime

- Automatic model downloads
- Automatic dependency provisioning
- No Python required
- CPU-only or GPU-accelerated operation
- Standalone executable or embedded child process
- Optional remote transcription logging

---

## Architecture

### VAD-Based Modes

Parakeet, Whisper, and Compare modes use Silero VAD to segment speech before transcription.

```text
mic ----------> VAD ----> segment ----> recogniser ----> [mic] text
tcp ----------> VAD ----> segment ----> recogniser ----> [phone] text
loopback -----> VAD ----> segment ----> recogniser ----> [pc] text
```

Each source maintains an independent processing lane, ensuring simultaneous speakers never interfere with one another.

### Streaming Mode

```text
mic ----------> streaming pipeline ----> partial ----> final
tcp ----------> streaming pipeline ----> partial ----> final
loopback -----> streaming pipeline ----> partial ----> final
```

Streaming mode emits partial updates while speech is occurring and automatically finalises utterances when endpoint rules are satisfied.

---

## Quick Start

### Build

```powershell
dotnet build
```

### Run

```powershell
dotnet run
```

On first launch the application:

1. Prompts for engine selection.
2. Prompts for audio source configuration.
3. Downloads required models and runtime assets.
4. Starts transcription.

---

## Automatic Provisioning

Required assets are downloaded automatically and cached locally.

Provisioning includes:

- Silero VAD
- Parakeet models
- Zipformer streaming models
- Whisper models
- whisper.cpp server binaries

Assets are only downloaded once.

---

## Requirements

- Windows x64
- .NET 10 Runtime
- Internet connection for first-time provisioning
- Microphone input device

Optional:

- Vulkan-capable GPU for AMD acceleration
- CUDA 12 compatible NVIDIA GPU

---

## Audio Sources

### Microphone

Captures speech directly from an NAudio input device.

### TCP Source

Accepts a single producer streaming:

- 16-bit PCM
- Little-endian
- Mono
- 16000 Hz

### Loopback

Captures system playback audio using WASAPI loopback.

Supports:

- Speakers
- Headphones
- HDMI audio
- Virtual audio devices

---

## Configuration

Configuration is stored in `config.json` alongside the executable.

Major configuration groups:

| Section | Purpose |
|----------|----------|
| Audio | Microphone capture |
| Parakeet | Offline recogniser |
| Vad | Voice activity detection |
| Streaming | Zipformer streaming |
| TcpSource | TCP audio ingestion |
| Loopback | System audio capture |
| RemoteLog | Remote transcription forwarding |
| Engine | Engine selection and Whisper server settings |

All settings are validated during startup.

Invalid configuration values terminate startup with exit code `1` and a diagnostic message on stderr.

---

## Remote Logging

Transcriptions can be forwarded to a remote HTTPS endpoint.

Features:

- Bearer-token authentication
- Fire-and-forget delivery
- Non-blocking operation
- Runtime configuration

Environment variable:

```text
P3K_REMOTE_LOG_TOKEN
```

Client-side setup is available from the interactive engine picker:

```text
Remote Log - Off/On (configure)

  1) Enable / Disable
  2) Set URL
  3) Set Token
  4) Erase Token
  5) Go back
```

`RemoteLog.Enabled` and `RemoteLog.Url` are saved in `config.json`. The bearer token is
saved in the per-user `P3K_REMOTE_LOG_TOKEN` environment variable, not in `config.json`.
The URL and token editors use Enter to confirm and Escape to cancel.

### Server Script

`Setup/remote-logging.php` is a minimal receiver that users can copy to their HTTPS
server. Replace `<YOUR_TOKEN_HERE>` with the same token saved in
`P3K_REMOTE_LOG_TOKEN` on the transcriber machine.

Example Linux deployment:

```bash
sudo mkdir -p /var/www/p3k-remote-log
sudo cp Setup/remote-logging.php /var/www/p3k-remote-log/remote-logging.php
sudo chown -R www-data:www-data /var/www/p3k-remote-log

sudo touch /var/log/transcriptions.log
sudo chown www-data:www-data /var/log/transcriptions.log
sudo chmod 640 /var/log/transcriptions.log
```

The transcriber should point `RemoteLog.Url` at:

```text
https://your-domain.example/remote-logging.php
```

Use an existing HTTPS-capable PHP host for the endpoint before enabling remote logging
from the app. Watch received lines with:

```bash
sudo tail -f /var/log/transcriptions.log
```

---

## Embedding

The application is designed to operate as a child process.

Typical workflow:

1. Launch process.
2. Wait for `READY` on stderr.
3. Read transcriptions from stdout.
4. Send `quit` to stdin for graceful shutdown.

Output format:

```text
[HH:mm:ss] [label] text
```

Streaming partials:

```text
[label~]
```

Streaming finals:

```text
[label]
```

---

## Project Structure

```text
Runtime/         Runtime orchestration
Audio/           Audio capture and processing
Transcription/   Recognition engines
Vad/             Silero VAD integration
Provisioning/    Dependency downloads
Net/             HTTP and remote logging
Configs/         Configuration records
Setup/           Interactive setup and menus
  remote-logging.php  Copy-to-server PHP receiver for RemoteLog
Display/         Console display windows
```

---

## Troubleshooting

| Problem | Resolution |
|----------|----------|
| Startup failure | Review stderr diagnostics |
| Missing downloads | Verify connectivity to GitHub and Hugging Face |
| Whisper unavailable | Check server provisioning and configuration |
| Incorrect microphone | Reconfigure Audio.DeviceNumber |
| Early segmentation | Increase Vad.MinSilenceDuration |
| Delayed transcription | Reduce Vad.MinSilenceDuration |
| TCP source silent | Verify PCM format and port |
| Loopback silent | Verify Loopback.Enabled and selected device |
| Remote logging inactive | Verify RemoteLog.Enabled, RemoteLog.Url, P3K_REMOTE_LOG_TOKEN, HTTPS connectivity, and server-side PHP/log permissions |

---

## Integration Scenarios

Typical use cases include:

- Voice assistants
- Live captioning
- Accessibility tools
- Game integrations
- Voice-controlled applications
- Meeting transcription
- Remote monitoring systems
- Audio analytics pipelines

---

## License

See repository licensing information.
