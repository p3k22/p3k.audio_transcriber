using p3k.audio_transcriber.Audio;
using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Transcription;
using p3k.audio_transcriber.Vad;

namespace p3k.audio_transcriber.Runtime
{
    internal static class RuntimeValidator
    {
        public static bool Validate(AppConfig config, bool needParakeet, bool needStreaming = false)
        {
            bool ok = true;
            ok &= WavInFactory.ValidateWavInConfig(config.Audio);
            if (needParakeet)
                ok &= ParakeetFactory.ValidateParakeetConfig(config.Parakeet);
            if (needStreaming)
                ok &= StreamingFactory.ValidateStreamingConfig(config.Streaming);
            // VAD is not used in streaming mode (endpoint detection is built into the model).
            if (!needStreaming)
                ok &= VadFactory.ValidateVadConfig(config.Vad);
            if (config.TcpSource.Enabled)
            {
                ok &= ValidateTcpSourceConfig(config.TcpSource);
                if (!needStreaming && config.TcpSource.Vad is not null)
                    ok &= VadFactory.ValidateVadConfig(config.TcpSource.Vad);
            }
            if (config.Loopback.Enabled)
            {
                if (string.IsNullOrWhiteSpace(config.Loopback.Label))
                {
                    Console.Error.WriteLine("Loopback Label must be set.");
                    ok = false;
                }
                if (!needStreaming && config.Loopback.Vad is not null)
                    ok &= VadFactory.ValidateVadConfig(config.Loopback.Vad);
            }
            ok &= ValidateEngineConfig(config.Engine);
            return ok;
        }

        private static bool ValidateEngineConfig(EngineConfig c)
        {
            if (!c.NeedsWhisperServer) return true;

            if (!Uri.TryCreate(c.WhisperServerUrl, UriKind.Absolute, out _))
            {
                Console.Error.WriteLine($"Engine WhisperServerUrl is not a valid URL: {c.WhisperServerUrl}");
                return false;
            }
            if (c.ServerThreads <= 0)
            {
                Console.Error.WriteLine("Engine ServerThreads must be positive.");
                return false;
            }
            return true;
        }

        private static bool ValidateTcpSourceConfig(TcpSourceConfig c)
        {
            if (string.IsNullOrWhiteSpace(c.Host))
            {
                Console.Error.WriteLine("TcpSource Host must be set.");
                return false;
            }
            if (c.Port is < 1 or > 65535)
            {
                Console.Error.WriteLine("TcpSource Port must be 1..65535.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(c.Label))
            {
                Console.Error.WriteLine("TcpSource Label must be set.");
                return false;
            }
            return true;
        }
    }
}
