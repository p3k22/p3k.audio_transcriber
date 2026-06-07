namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Which recogniser the pipeline decodes each VAD segment with.
    ///
    /// "parakeet" runs the local sherpa-onnx Parakeet model in-process. "whisper"
    /// instead POSTs each segment to a whisper.cpp server (GPU-accelerated). "compare"
    /// runs BOTH on every segment and prints one tagged line per engine, for
    /// side-by-side quality comparison (needs the whisper server, same as "whisper").
    /// An empty <see cref="Type"/> means "not chosen yet" and triggers the first-run
    /// console picker.
    ///
    /// Server lifetime &amp; provisioning: for "whisper"/"compare", if nothing is already
    /// answering at <see cref="WhisperServerUrl"/>, the app provisions the server
    /// itself — downloading the build for <see cref="WhisperBuild"/> (amd/nvidia/cpu)
    /// plus the ggml model into folders next to the exe when absent (see
    /// WhisperProvisioner) — then spawns it and kills it on shutdown via a job object,
    /// so it never orphans. If a server is already up, the app just uses it and leaves
    /// it alone. If the server can't be reached/provisioned and
    /// <see cref="FallbackToParakeet"/> is true, the pipeline falls back to Parakeet so
    /// it always comes up. An explicit, existing <see cref="ServerExePath"/> /
    /// <see cref="ServerModelPath"/> is honoured as-is and skips the download.
    /// </summary>
    internal sealed record EngineConfig
    {
        public string Type { get; init; } = "";                               // "" = ask on first run | "parakeet" | "whisper" | "compare"
        public string WhisperBuild { get; init; } = "";                       // "amd" (Vulkan) | "nvidia" (CUDA) | "cpu"
        public string WhisperServerUrl { get; init; } = "http://127.0.0.1:8080";
        public bool FallbackToParakeet { get; init; } = true;

        // Optional explicit paths; blank = auto-download a portable build/model next to the exe.
        public string ServerExePath { get; init; } = "";                      // path to whisper-server.exe
        public string ServerModelPath { get; init; } = "";                    // path to the ggml model
        public int ServerThreads { get; init; } = 6;
        public int ServerStartTimeoutSeconds { get; init; } = 60;             // wait for it to load + listen

        public bool IsWhisper =>
            string.Equals(Type, "whisper", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Run both engines on every segment for side-by-side comparison.</summary>
        public bool IsCompare =>
            string.Equals(Type, "compare", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this mode needs the whisper server (either "whisper" or "compare").</summary>
        public bool NeedsWhisperServer => IsWhisper || IsCompare;
    }
}
