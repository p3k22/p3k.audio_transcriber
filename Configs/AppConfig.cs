namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Root configuration: the whole config.json deserialises into this, then the
    /// three sub-configs are handed to the factories. Add new sections here.
    /// </summary>
    internal sealed record AppConfig
    {
        public WavInEventConfig Audio { get; init; } = new();
        public ParakeetConfig Parakeet { get; init; } = new();
        public VadConfig Vad { get; init; } = new();
        public StreamingConfig Streaming { get; init; } = new();
        public TcpSourceConfig TcpSource { get; init; } = new();
        public LoopbackConfig Loopback { get; init; } = new();
        public EngineConfig Engine { get; init; } = new();
        public RemoteLogConfig RemoteLog { get; init; } = new();
    }
}
