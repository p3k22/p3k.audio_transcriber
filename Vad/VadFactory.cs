using p3k.audio_transcriber.Audio;
using p3k.audio_transcriber.Configs;
using SherpaOnnx;

namespace p3k.audio_transcriber.Vad
{
    internal static class VadFactory
    {
        internal static bool ValidateVadConfig(VadConfig vadConfig)
        {
            if (string.IsNullOrEmpty(vadConfig.Model))
            {
                Console.Error.WriteLine("VAD model path is required.");
                return false;
            }
            if (!File.Exists(vadConfig.ResolvedModel))
            {
                Console.Error.WriteLine($"VAD model file not found: {vadConfig.ResolvedModel}");
                return false;
            }
            if (vadConfig.Threshold < 0 || vadConfig.Threshold > 1)
            {
                Console.Error.WriteLine("VAD threshold must be between 0 and 1.");
                return false;
            }
            if (vadConfig.MinSilenceDuration < 0)
            {
                Console.Error.WriteLine("Minimum silence duration must be non-negative.");
                return false;
            }
            if (vadConfig.MinSpeechDuration < 0)
            {
                Console.Error.WriteLine("Minimum speech duration must be non-negative.");
                return false;
            }
            if (vadConfig.MaxSpeechDuration <= 0)
            {
                Console.Error.WriteLine("Maximum speech duration must be positive.");
                return false;
            }
            if (vadConfig.PreRollDuration < 0)
            {
                Console.Error.WriteLine("Pre-roll duration must be non-negative.");
                return false;
            }
            if (vadConfig.BufferSizeInSeconds <= 0)
            {
                Console.Error.WriteLine("Buffer size in seconds must be positive.");
                return false;
            }
            if (vadConfig.WindowSize <= 0)
            {
                Console.Error.WriteLine("Window size must be positive.");
                return false;
            }
            if (vadConfig.NumThreads <= 0)
            {
                Console.Error.WriteLine("Number of threads must be positive.");
                return false;
            }
            return true;
        }

        internal static VoiceActivityDetector CreateVAD(VadConfig vadConfig, int sampleRate)
        {
            var vadModelConfig = new VadModelConfig();
            vadModelConfig.SileroVad.Model = vadConfig.ResolvedModel;
            vadModelConfig.SileroVad.Threshold = vadConfig.Threshold;
            vadModelConfig.SileroVad.MinSilenceDuration = vadConfig.MinSilenceDuration;
            vadModelConfig.SileroVad.MinSpeechDuration = vadConfig.MinSpeechDuration;
            vadModelConfig.SileroVad.MaxSpeechDuration = vadConfig.MaxSpeechDuration;
            vadModelConfig.SileroVad.WindowSize = vadConfig.WindowSize;
            vadModelConfig.SampleRate = sampleRate;
            vadModelConfig.NumThreads = vadConfig.NumThreads;
            vadModelConfig.Provider = vadConfig.Provider;
            return new VoiceActivityDetector(vadModelConfig, bufferSizeInSeconds: vadConfig.BufferSizeInSeconds);
        }

        /// <summary>
        /// Build the pre-roll ring for a source, or null when pre-roll is disabled.
        /// Sized to span the VAD's own buffer (the oldest sample a segment can start at)
        /// plus the lead-in we splice back on, so the pre-roll is always still resident
        /// when a segment is emitted.
        /// </summary>
        internal static PreRollBuffer? CreatePreRoll(VadConfig vadConfig, int sampleRate)
        {
            if (vadConfig.PreRollDuration <= 0) return null;
            int padSamples = (int)(vadConfig.PreRollDuration * sampleRate);
            int capacity = (int)Math.Ceiling((vadConfig.BufferSizeInSeconds + vadConfig.PreRollDuration) * sampleRate);
            return new PreRollBuffer(capacity, padSamples);
        }
    }
}
