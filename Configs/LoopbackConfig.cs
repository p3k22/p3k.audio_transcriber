namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Optional WASAPI loopback source: captures whatever Windows is currently
    /// playing out of the default (or a specified) render device — speakers,
    /// headphones, HDMI audio, etc. Feeds the same VAD + recogniser pipeline as
    /// the mic and TCP sources, so TV dialogue, YouTube, calls, etc. all get
    /// transcribed just like spoken mic input.
    ///
    /// DeviceIndex -1 = default playback device (recommended). Set to 0, 1, 2…
    /// to target a specific render device; available devices are listed on stderr
    /// at startup when loopback is enabled.
    ///
    /// Audio is automatically converted from the system's native format
    /// (typically 48 kHz stereo float) to the pipeline's sample rate (mono).
    /// </summary>
    internal sealed record LoopbackConfig
    {
        public bool Enabled { get; init; } = false;
        public string Label { get; init; } = "pc";
        public int DeviceIndex { get; init; } = -1;
        public bool Window { get; init; } = false;

        // Loopback audio (TV, streaming) has speech mixed with music/effects, so it scores
        // lower speech probability than clean mic input. Lower threshold and shorter silence
        // window keep it responsive. Set "Vad": null in config.json to fall back to global VAD.
        public VadConfig? Vad { get; init; } = new VadConfig
        {
            Threshold = 0.1f,
            MinSilenceDuration = 0.2f,
            MinSpeechDuration = 0.1f,
            MaxSpeechDuration = 6.0f,
            BufferSizeInSeconds = 60.0f,
        };
    }
}
