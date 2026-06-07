namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Resolves config paths. Relative paths (e.g. "models/...") are anchored to
    /// the executable's directory so the app is portable regardless of the
    /// current working directory.
    /// </summary>
    internal static class AppPaths
    {
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        }
    }
}
