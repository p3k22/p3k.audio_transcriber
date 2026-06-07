using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Net;

namespace p3k.audio_transcriber.Transcription
{
    /// <summary>
    /// Makes the whisper.cpp server runnable on this machine without any manual setup:
    /// ensures the server binary (the right build for the selected GPU vendor) and the
    /// ggml model are present, downloading them on first use, and returns an
    /// <see cref="EngineConfig"/> with <see cref="EngineConfig.ServerExePath"/> /
    /// <see cref="EngineConfig.ServerModelPath"/> pointing at the resolved files.
    ///
    /// Builds are kept under <c>whisper/&lt;build&gt;/</c> next to the exe (one dir per
    /// vendor, so switching vendor fetches a fresh set instead of clobbering), and the
    /// shared model under <c>models/whisper/</c>. An explicit, existing path in config
    /// is honoured as-is (so a dev box pointing at a local build never re-downloads).
    /// </summary>
    internal static class WhisperProvisioner
    {
        // AMD = a Vulkan build of upstream whisper.cpp, unmodified (no official Vulkan
        // binary is published), mirrored as a GitHub release asset.
        // NVIDIA / CPU = official whisper.cpp release zips.
        private const string AmdVulkanZipUrl =
            "https://github.com/p3k22/whisper-vulkan-win-x64/releases/download/v1.8.6-vulkan/whisper-vulkan-win-x64.zip";
        private const string NvidiaCudaZipUrl =
            "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.6/whisper-cublas-12.4.0-bin-x64.zip";
        private const string CpuZipUrl =
            "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.6/whisper-bin-x64.zip";

        private const string ModelUrl =
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin";
        private const string ModelFileName = "ggml-large-v3-turbo.bin";
        private const long ModelMinBytes = 1_000_000_000;   // ~1.6 GB; guards against truncated downloads

        /// <summary>
        /// Ensure the server binary + model exist (downloading if absent) and return an
        /// engine config pointed at them. Returns null if anything could not be obtained.
        /// </summary>
        public static EngineConfig? Ensure(EngineConfig engine)
        {
            string? exePath = EnsureBinary(engine);
            if (exePath is null)
                return null;

            string? modelPath = EnsureModel(engine);
            if (modelPath is null)
                return null;

            return engine with { ServerExePath = exePath, ServerModelPath = modelPath };
        }

        /// <summary>Locate (or download + extract) whisper-server.exe for the selected build.</summary>
        private static string? EnsureBinary(EngineConfig engine)
        {
            // Honour an explicit, existing path first (dev box / manual install).
            if (!string.IsNullOrWhiteSpace(engine.ServerExePath))
            {
                string explicitPath = AppPaths.Resolve(engine.ServerExePath);
                if (File.Exists(explicitPath))
                    return explicitPath;
            }

            string? build = NormalizeBuild(engine.WhisperBuild);
            if (build is null)
            {
                Console.Error.WriteLine(
                    $"Whisper selected but no valid WhisperBuild (\"{engine.WhisperBuild}\"); expected amd | nvidia | cpu.");
                return null;
            }

            string dir = Path.Combine(AppContext.BaseDirectory, "whisper", build);

            string? exe = FindServerExe(dir);
            if (exe is not null)
                return exe;

            string url = build switch
            {
                "amd" => AmdVulkanZipUrl,
                "nvidia" => NvidiaCudaZipUrl,
                _ => CpuZipUrl,
            };

            Console.Error.WriteLine($"Whisper server ({build}) not present; downloading {url} ...");
            if (!HttpDownload.DownloadAndExtractZip(url, dir, $"whisper-{build}.zip"))
                return null;

            exe = FindServerExe(dir);
            if (exe is null)
                Console.Error.WriteLine($"  whisper-server.exe not found in extracted build at {dir}.");
            return exe;
        }

        /// <summary>Locate (or download) the ggml model.</summary>
        private static string? EnsureModel(EngineConfig engine)
        {
            string path;
            if (!string.IsNullOrWhiteSpace(engine.ServerModelPath))
            {
                string explicitPath = AppPaths.Resolve(engine.ServerModelPath);
                if (File.Exists(explicitPath))
                    return explicitPath;
                path = explicitPath;   // explicit but missing -> download to where it asked
            }
            else
            {
                path = Path.Combine(AppContext.BaseDirectory, "models", "whisper", ModelFileName);
            }

            if (File.Exists(path) && new FileInfo(path).Length >= ModelMinBytes)
                return path;

            Console.Error.WriteLine($"Whisper model not present; downloading {ModelFileName} (~1.6 GB) ...");
            if (!HttpDownload.DownloadFile(ModelUrl, path, ModelFileName))
                return null;

            if (!File.Exists(path) || new FileInfo(path).Length < ModelMinBytes)
            {
                Console.Error.WriteLine("  whisper model download looks incomplete.");
                return null;
            }
            return path;
        }

        /// <summary>The build zips name the server exe differently across releases; find whichever is there.</summary>
        private static string? FindServerExe(string dir)
        {
            if (!Directory.Exists(dir))
                return null;

            return Directory.EnumerateFiles(dir, "whisper-server.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(dir, "server.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(dir, "*server*.exe", SearchOption.AllDirectories).FirstOrDefault();
        }

        /// <summary>Map config text to a build folder/url key, or null if unrecognised.</summary>
        private static string? NormalizeBuild(string? value) =>
            (value?.Trim().ToLowerInvariant()) switch
            {
                "amd" or "vulkan" => "amd",
                "nvidia" or "cuda" or "cublas" => "nvidia",
                "cpu" => "cpu",
                _ => null,
            };
    }
}
