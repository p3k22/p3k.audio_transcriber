namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Audio capture / sampling settings (the "samples" section of the config).
    /// SampleRate is the single source of truth handed to the Parakeet and VAD
    /// factories so every stage agrees on the rate.
    /// </summary>
    internal sealed record WavInEventConfig
    {
        public int SampleRate { get; init; } = 16000;         // Parakeet + Silero VAD expect 16000
        public int BitDepth { get; init; } = 16;              // 16-bit PCM (only 16 supported)
        public int Channels { get; init; } = 1;               // 1 = mono, 2 = stereo
        public int BufferMilliseconds { get; init; } = 100;   // capture buffer per callback
        public int DeviceNumber { get; init; } = 0;           // NAudio input device index (0 = default)
        public string Label { get; init; } = "mic";           // stdout tag for this source
        public bool Window { get; init; } = false;
    }
}
