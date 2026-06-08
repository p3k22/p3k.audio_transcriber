using System.Text.Json;

namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Loads <see cref="AppConfig"/> from a JSON file. By default the file lives
    /// next to the executable, but an external host can point the app at any path
    /// (so it can own/expose the config). A default file is written if missing,
    /// so the app is runnable out of the box.
    /// </summary>
    internal static class ConfigLoader
    {
        public const string FileName = "config.json";

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,  // allow // comments
            AllowTrailingCommas = true,
        };

        /// <summary>Default config path: alongside the executable.</summary>
        public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);

        /// <summary>
        /// The documented default config written when no file exists. It is JSONC
        /// (the loader skips <c>//</c> comments) so every setting ships with the
        /// allowed values / units inline. Keep the values here in sync with the
        /// record defaults in the *Config types; the loader parses this string back
        /// on first run, so a typo here fails fast.
        /// </summary>
        private const string DefaultJsonc = """
        {
          // ===================================================================
          //  p3k audio transcriber - config.json
          //  '//' comments are allowed. The file is re-read on restart.
          // ===================================================================

          // Audio capture / input device.
          "Audio": {
            "SampleRate": 16000,        // Hz. Parakeet + Silero VAD require 16000 - don't change.
            "BitDepth": 16,             // bits/sample. Only 16-bit PCM is supported.
            "Channels": 1,              // 1 = mono, 2 = stereo. Use 1.
            "BufferMilliseconds": 100,  // capture buffer per callback (ms). Lower = less latency, more CPU.
            "DeviceNumber": 0,          // NAudio input device index. 0 = system default mic.
            "Label": "mic"              // tag prefixed to transcriptions from this source.
          },

          // Local Parakeet (sherpa-onnx) recogniser.
          "Parakeet": {
            "ParakeetDir": "models/parakeet",   // dir with encoder/decoder/joiner .onnx + tokens.txt (relative to exe).
            "Precision": "fp32",        // "fp32" = ~2.4 GB, higher accuracy | "int8" = ~630 MB, faster. (--fp32 flag overrides.)
            "FeatureDim": 80,           // model feature dimension; leave at 80.
            "ModelType": "nemo_transducer",  // sherpa-onnx model type for Parakeet TDT.
            "NumThreads": 6,            // CPU threads (Ryzen 5 5600X = 6 physical cores).
            "Provider": "cpu",          // ONNX execution provider. Parakeet runs on "cpu" here.
            "Debug": 0,                 // 1 = verbose sherpa-onnx logging.
            "DecodingMethod": "greedy_search"  // "greedy_search" (fast) | "modified_beam_search" (slower, sometimes more accurate).
          },

          // Silero VAD - decides where each utterance ends so one segment is transcribed at a time.
          "Vad": {
            "Model": "models/silero_vad.onnx",  // VAD model path (relative to exe).
            "Provider": "cpu",          // ONNX provider for the VAD.
            "Threshold": 0.5,           // speech-probability cutoff 0..1. Higher = less sensitive (may drop quiet/distant speech).
            "MinSilenceDuration": 0.6,  // seconds of pause that ends an utterance. Lower = snappier, risks cutting mid-sentence.
            "MinSpeechDuration": 0.25,  // seconds; speech shorter than this is ignored (rejects clicks/noise).
            "MaxSpeechDuration": 20.0,  // seconds; force-cut speech longer than this.
            "PreRollDuration": 0.3,     // seconds of audio kept before VAD onset, so sentence starts aren't clipped.
            "BufferSizeInSeconds": 30.0,// internal VAD ring-buffer length (seconds).
            "WindowSize": 512,          // Silero window in samples (512 @ 16 kHz); leave default.
            "NumThreads": 1             // CPU threads for the VAD (1 is plenty).
          },

          // Optional 2nd audio source over loopback TCP (e.g. the phone relay bridge).
          "TcpSource": {
            "Enabled": false,           // false = mic only. true = also accept PCM over TCP.
            "Host": "127.0.0.1",        // bind address; loopback only.
            "Port": 53123,              // TCP port to listen on.
            "Label": "phone",           // tag prefixed to transcriptions from this source.
            // VAD tuning for THIS source only. Phone audio arrives levelled/quieter, so it
            // ships more sensitive than the mic. Set this to null to reuse the main "Vad"
            // block instead. (A partial object does NOT inherit - omitted fields use code defaults.)
            "Vad": {
              "Model": "models/silero_vad.onnx",
              "Provider": "cpu",
              "Threshold": 0.35,        // lower than the mic (0.5) = more sensitive
              "MinSilenceDuration": 0.6,
              "MinSpeechDuration": 0.15,
              "MaxSpeechDuration": 20.0,
              "PreRollDuration": 0.2,
              "BufferSizeInSeconds": 30.0,
              "WindowSize": 512,
              "NumThreads": 1
            }
          },

          // Optional WASAPI loopback: captures whatever Windows is playing (TV, YouTube, calls, etc).
          "Loopback": {
            "Enabled": false,           // true = also transcribe system audio output.
            "Label": "pc",              // tag prefixed to transcriptions from this source.
            "DeviceIndex": -1,          // -1 = default playback device. 0, 1, 2... for a specific render device.
            // VAD tuned for mixed audio (speech + music/effects). Lower threshold catches dialogue
            // that scores lower probability when blended with background sound.
            // Set to null to reuse the main "Vad" block instead.
            "Vad": {
              "Model": "models/silero_vad.onnx",
              "Provider": "cpu",
              "Threshold": 0.1,         // lower than mic (0.5) - loopback audio is mixed with background
              "MinSilenceDuration": 0.2,
              "MinSpeechDuration": 0.1,
              "MaxSpeechDuration": 8.0,
              "PreRollDuration": 0.3,
              "BufferSizeInSeconds": 30.0,
              "WindowSize": 512,
              "NumThreads": 1
            }
          },

          // Remote transcription log - POST each utterance to a VPS endpoint over HTTPS.
          // Enable/Disable is stored here. The Bearer token is NOT stored here (it's a
          // secret): it lives in the P3K_REMOTE_LOG_TOKEN env var, set via the startup prompt.
          "RemoteLog": {
            "Enabled": false,  // true = post each transcription line to Url
            "Url": ""          // blank = never posts regardless of Enabled. e.g. "https://your-domain.com/log.php"
          },

          // Which recogniser decodes each VAD segment.
          "Engine": {
            "Type": "",                 // "" = ask on first run | "parakeet" (CPU) | "whisper" (whisper.cpp server) | "compare" (run BOTH, tag each line).
            "WhisperBuild": "",         // which whisper.cpp build to download and run (only used when Type is "whisper" or "compare"): "amd" = Vulkan GPU, "nvidia" = CUDA GPU, "cpu" = no GPU. Chosen on first run; edit it to switch builds and the new one downloads on the next start.
            "WhisperServerUrl": "http://127.0.0.1:8080",  // whisper.cpp server endpoint (used by "whisper" / "compare").
            "FallbackToParakeet": true, // if the whisper server can't be reached/provisioned, fall back to Parakeet so the app still runs.
            "ServerExePath": "",        // optional explicit whisper-server.exe path; blank = auto-download the WhisperBuild next to the exe.
            "ServerModelPath": "",      // optional explicit ggml model path; blank = auto-download ggml-large-v3-turbo.bin next to the exe.
            "ServerThreads": 6,         // CPU threads passed to whisper-server (-t).
            "ServerStartTimeoutSeconds": 60  // seconds to wait for the server to load the model + listen.
          },

          // Memory watchdog. sherpa-onnx / ONNX Runtime leak native memory the .NET GC can't
          // reclaim, so a long run climbs all day until restarted. This recycles the pipeline
          // (a clean Stop->Start that rebuilds the native models) when resident RAM crosses
          // ThresholdMb, waiting for a quiet gap so no utterance is cut. It also logs a memory
          // line to stderr every LogIntervalSeconds (managed-heap vs process-private).
          "Watchdog": {
            "Enabled": true,            // false = only log memory, never recycle.
            "ThresholdMb": 6000,        // resident RAM (working set) MB that triggers a recycle. Keep it ABOVE the
                                        // model baseline (fp32 Parakeet's working set is ~4 GB) or it recycles constantly.
            "QuietSeconds": 3.0,        // seconds of no transcript output that count as a safe gap to recycle in.
            "MaxWaitSeconds": 120.0,    // if audio never goes quiet, force-recycle after this many seconds over budget.
            "PollSeconds": 5.0,         // how often memory is sampled / the budget is checked.
            "LogIntervalSeconds": 60.0, // how often a [mem] line is written to stderr.
            "MinRecycleSeconds": 60.0   // minimum gap between recycles, so a too-low threshold can't thrash-restart.
          }
        }
        """;

        /// <summary>Resolve an override path (null/blank -> default).</summary>
        public static string Resolve(string? path) =>
            string.IsNullOrWhiteSpace(path) ? DefaultPath : Path.GetFullPath(path);

        /// <summary>Read + parse the config file (writing a default if absent).</summary>
        public static AppConfig Load(string? path = null)
        {
            string resolved = Resolve(path);

            if (!File.Exists(resolved))
            {
                Console.Error.WriteLine($"No config found - writing documented default to: {resolved}");
                File.WriteAllText(resolved, DefaultJsonc);
                // Parse the template back so the in-memory config always matches the
                // file we just wrote (and a typo in the template fails fast here).
                return JsonSerializer.Deserialize<AppConfig>(DefaultJsonc, Options)
                       ?? new AppConfig();
            }

            string json = File.ReadAllText(resolved);
            AppConfig? config = JsonSerializer.Deserialize<AppConfig>(json, Options);
            if (config is null)
                throw new InvalidDataException($"Config file parsed to null: {resolved}");

            Console.Error.WriteLine($"Loaded config: {resolved}");
            return config;
        }
    }
}
