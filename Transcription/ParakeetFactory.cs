using p3k.audio_transcriber.Configs;
using SherpaOnnx;

namespace p3k.audio_transcriber.Transcription
{
    internal static class ParakeetFactory
    {
        /// <summary>Validate paths + parameters before building the recogniser.</summary>
        internal static bool ValidateParakeetConfig(ParakeetConfig c)
        {
            foreach (var (label, path) in new[]
                     {
                         ("encoder", c.Encoder), ("decoder", c.Decoder),
                         ("joiner", c.Joiner), ("tokens", c.Tokens),
                     })
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    Console.Error.WriteLine($"Parakeet {label} not found: {path}");
                    return false;
                }
            }
            if (c.FeatureDim <= 0)
            {
                Console.Error.WriteLine("Parakeet FeatureDim must be positive.");
                return false;
            }
            if (c.NumThreads <= 0)
            {
                Console.Error.WriteLine("Parakeet NumThreads must be positive.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(c.ModelType))
            {
                Console.Error.WriteLine("Parakeet ModelType is required.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(c.DecodingMethod))
            {
                Console.Error.WriteLine("Parakeet DecodingMethod is required.");
                return false;
            }
            return true;
        }

        internal static OfflineRecognizer CreateParakeet(ParakeetConfig c, int sampleRate)
        {
            var recognizerConfig = new OfflineRecognizerConfig();
            recognizerConfig.FeatConfig.SampleRate = sampleRate;
            recognizerConfig.FeatConfig.FeatureDim = c.FeatureDim;
            recognizerConfig.ModelConfig.Transducer.Encoder = c.Encoder;
            recognizerConfig.ModelConfig.Transducer.Decoder = c.Decoder;
            recognizerConfig.ModelConfig.Transducer.Joiner = c.Joiner;
            recognizerConfig.ModelConfig.Tokens = c.Tokens;
            recognizerConfig.ModelConfig.ModelType = c.ModelType;
            recognizerConfig.ModelConfig.NumThreads = c.NumThreads;
            recognizerConfig.ModelConfig.Provider = c.Provider;
            recognizerConfig.ModelConfig.Debug = c.Debug;
            recognizerConfig.DecodingMethod = c.DecodingMethod;
            return new OfflineRecognizer(recognizerConfig);
        }
    }
}
