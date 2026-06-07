using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Transcription;

namespace p3k.audio_transcriber.Runtime
{
    internal static class RuntimeTranscriberFactory
    {
        public static ISegmentTranscriber Create(EngineMode mode, AppConfig config, int sampleRate) => mode switch
        {
            EngineMode.Whisper => new WhisperServerTranscriber(config.Engine.WhisperServerUrl),
            EngineMode.Compare => new ComparisonTranscriber(
                new ParakeetTranscriber(ParakeetFactory.CreateParakeet(config.Parakeet, sampleRate)),
                new WhisperServerTranscriber(config.Engine.WhisperServerUrl)),
            _ => new ParakeetTranscriber(ParakeetFactory.CreateParakeet(config.Parakeet, sampleRate)),
        };
    }
}
