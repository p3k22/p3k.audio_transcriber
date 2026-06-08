namespace p3k.audio_transcriber.Configs
{
    internal sealed record RemoteLogConfig
    {
        /// <summary>Whether remote logging is enabled.</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>POST endpoint on the VPS. Blank = disabled.</summary>
        public string Url { get; init; } = "";

        // The Bearer token is a secret, so it is NOT stored here. It lives in the
        // P3K_REMOTE_LOG_TOKEN environment variable and is set/rotated through the
        // startup prompt (see Setup/RemoteLogSetup).
    }
}
