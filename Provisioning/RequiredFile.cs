namespace p3k.audio_transcriber.Provisioning
{
    /// <summary>One model file the app needs, plus where to fetch it if absent.</summary>
    internal sealed record RequiredFile(string Name, string Path, string Url, long MinBytes);
}
