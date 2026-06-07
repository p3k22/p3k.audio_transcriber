namespace p3k.audio_transcriber.Configs
{
    internal sealed record RemoteLogConfig
    {
        /// <summary>POST endpoint on the VPS. Blank = disabled.</summary>
        public string Url { get; init; } = "";

        /// <summary>Bearer token sent as the Authorization header. Generate with: openssl rand -hex 32</summary>
        public string Token { get; init; } = "";
    }
}
