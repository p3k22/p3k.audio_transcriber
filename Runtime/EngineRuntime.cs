using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Transcription;

namespace p3k.audio_transcriber.Runtime
{
    /// <summary>Resolves the effective ASR engine and owns any whisper server process spawned by this app.</summary>
    internal sealed class EngineRuntime : IDisposable
    {
        private WhisperServerProcess? _whisperServer;

        public EngineMode ResolveMode(ref EngineConfig engine)
        {
            if (string.Equals(engine.Type, "streaming", StringComparison.OrdinalIgnoreCase))
                return EngineMode.Streaming;
            if (!engine.NeedsWhisperServer) return EngineMode.Parakeet;

            EngineMode requested = engine.IsCompare ? EngineMode.Compare : EngineMode.Whisper;

            if (EnsureWhisperAvailable(ref engine)) return requested;

            if (engine.FallbackToParakeet)
            {
                Console.Error.WriteLine(
                    $"whisper-server unavailable at {engine.WhisperServerUrl}; falling back to Parakeet"
                    + (engine.IsCompare ? " (compare disabled - nothing to compare against)." : "."));
                return EngineMode.Parakeet;
            }

            Console.Error.WriteLine(
                $"whisper-server unavailable at {engine.WhisperServerUrl} and fallback disabled; "
                + "whisper segments will fail until it is up.");
            return requested;
        }

        public static string DescribeMode(EngineMode mode, EngineConfig engine) => mode switch
        {
            EngineMode.Whisper => $"whisper ({engine.WhisperServerUrl})",
            EngineMode.Compare => $"compare parakeet vs whisper ({engine.WhisperServerUrl})",
            EngineMode.Streaming => "streaming (online zipformer, no VAD)",
            _ => "parakeet",
        };

        private bool EnsureWhisperAvailable(ref EngineConfig engine)
        {
            if (WhisperServerTranscriber.IsReachable(engine.WhisperServerUrl))
            {
                Console.Error.WriteLine($"Using existing whisper-server at {engine.WhisperServerUrl}.");
                return true;
            }

            EngineConfig? provisioned = WhisperProvisioner.Ensure(engine);
            if (provisioned is null)
            {
                Console.Error.WriteLine("Could not provision the whisper server (binary/model unavailable).");
                return false;
            }
            engine = provisioned;

            Console.Error.WriteLine($"Launching whisper-server ({engine.ServerExePath}) ...");
            _whisperServer = WhisperServerProcess.StartAndWait(engine);
            if (_whisperServer is not null)
            {
                Console.Error.WriteLine($"whisper-server ready at {engine.WhisperServerUrl}.");
                return true;
            }

            Console.Error.WriteLine("whisper-server launch failed.");
            return false;
        }

        public void Dispose()
        {
            _whisperServer?.Dispose();
            _whisperServer = null;
        }
    }
}
