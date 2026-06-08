using p3k.audio_transcriber.Configs;
using SherpaOnnx;

namespace p3k.audio_transcriber.Transcription
{
    internal static class StreamingFactory
    {
        internal static bool ValidateStreamingConfig(StreamingConfig c)
        {
            foreach (var (label, path) in new[]
                     {
                         ("encoder", c.Encoder), ("decoder", c.Decoder),
                         ("joiner", c.Joiner), ("tokens", c.Tokens),
                     })
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    Console.Error.WriteLine($"Streaming {label} not found: {path}");
                    return false;
                }
            }
            if (c.FeatureDim <= 0)
            {
                Console.Error.WriteLine("Streaming FeatureDim must be positive.");
                return false;
            }
            if (c.NumThreads <= 0)
            {
                Console.Error.WriteLine("Streaming NumThreads must be positive.");
                return false;
            }
            return true;
        }

        internal static OnlineRecognizer CreateRecognizer(StreamingConfig c, int sampleRate)
        {
            var config = new OnlineRecognizerConfig();
            config.FeatConfig.SampleRate = sampleRate;
            config.FeatConfig.FeatureDim = c.FeatureDim;
            config.ModelConfig.Transducer.Encoder = c.Encoder;
            config.ModelConfig.Transducer.Decoder = c.Decoder;
            config.ModelConfig.Transducer.Joiner = c.Joiner;
            config.ModelConfig.Tokens = c.Tokens;
            config.ModelConfig.NumThreads = c.NumThreads;
            config.ModelConfig.Provider = c.Provider;
            config.ModelConfig.Debug = c.Debug;
            config.DecodingMethod = c.DecodingMethod;
            config.EnableEndpoint = c.EnableEndpoint;
            config.Rule1MinTrailingSilence = c.Rule1MinTrailingSilence;
            config.Rule2MinTrailingSilence = c.Rule2MinTrailingSilence;
            config.Rule3MinUtteranceLength = c.Rule3MinUtteranceLength;
            return new OnlineRecognizer(config);
        }
    }
}
