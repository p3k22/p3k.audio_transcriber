namespace p3k.audio_transcriber.Runtime
{
    /// <summary>Which recogniser(s) a source decodes with, resolved once at startup.</summary>
    internal enum EngineMode
    {
        Parakeet,
        Whisper,
        Compare,
        Streaming,
    }
}
