namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Optional second audio source over a loopback TCP connection. When enabled,
    /// the app binds Host:Port and accepts one PCM producer at a time. The producer
    /// streams raw 16-bit mono PCM at the configured sample rate; the app knows
    /// nothing about where that PCM comes from.
    /// </summary>
    internal sealed record TcpSourceConfig
    {
        public bool Enabled { get; init; } = false;          // off => mic-only, exactly as before
        public string Host { get; init; } = "127.0.0.1";     // loopback only
        public int Port { get; init; } = 53123;
        public string Label { get; init; } = "phone";        // stdout tag for transcriptions from this source
        public bool Window { get; init; } = false;

        // Optional VAD override for this source. The phone arrives compressed/levelled
        // and needs a more sensitive VAD than the clean, close mic; null => reuse the
        // global Vad block so existing configs behave exactly as before.
        public VadConfig? Vad { get; init; } = null;
    }
}
