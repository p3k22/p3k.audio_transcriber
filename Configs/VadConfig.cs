using System.Text.Json.Serialization;

namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Silero VAD settings. The VAD decides where each utterance ends so the
    /// offline recogniser can transcribe one segment at a time.
    /// </summary>
    internal sealed record VadConfig
    {
        public string Model { get; init; } = "models/silero_vad.onnx";
        public string Provider { get; init; } = "cpu";
        public float Threshold { get; init; } = 0.5f;
        public float MinSilenceDuration { get; init; } = 0.6f;   // pause that ends an utterance
        public float MinSpeechDuration { get; init; } = 0.25f;
        public float MaxSpeechDuration { get; init; } = 20.0f;   // force-cut very long speech
        public float PreRollDuration { get; init; } = 0.3f;      // audio kept before VAD onset, to unclip sentence starts
        public float BufferSizeInSeconds { get; init; } = 30.0f;
        public int WindowSize { get; init; } = 512;
        public int NumThreads { get; init; } = 1;

        // Absolute path to the VAD model; resolved relative to the exe.
        [JsonIgnore] public string ResolvedModel => AppPaths.Resolve(Model);
    }
}
