using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Net;

namespace p3k.audio_transcriber.Provisioning
{
    /// <summary>Ensures runtime model files are present, downloading missing files on first use.</summary>
    internal static class DependencyProvisioner
    {
        public static bool Ensure(AppConfig config, bool needParakeet, bool needStreaming = false, bool autoDownload = true)
        {
            var required = ModelManifest.Required(config, needParakeet, needStreaming);
            var missing = required.Where(f => !IsPresent(f)).ToList();

            if (missing.Count == 0)
                return true;

            foreach (var f in missing)
                Console.Error.WriteLine($"  [missing] {f.Path}");

            if (!autoDownload)
            {
                Console.Error.WriteLine("Missing model files. Download them to the paths above from:");
                foreach (var f in missing)
                    Console.Error.WriteLine($"  {f.Url}");
                return false;
            }

            string approx = needParakeet
                ? (config.Parakeet.IsFp32 ? "~2.4 GB" : "~630 MB")
                : needStreaming ? "~73 MB" : "small";
            Console.Error.WriteLine($"Downloading {missing.Count} file(s) ({approx} on a clean install) ...");
            foreach (var f in missing)
            {
                if (!HttpDownload.DownloadFile(f.Url, f.Path, f.Name))
                    return false;

                if (!IsPresent(f))
                {
                    Console.Error.WriteLine($"  download incomplete: {f.Name}");
                    return false;
                }

                Console.Error.WriteLine($"  [done]    {f.Name}");
            }

            return true;
        }

        private static bool IsPresent(RequiredFile f) =>
            File.Exists(f.Path) && new FileInfo(f.Path).Length >= f.MinBytes;
    }
}
