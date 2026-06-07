using System.IO.Compression;

namespace p3k.audio_transcriber.Net
{
    /// <summary>
    /// Small HTTP fetch helper used for runtime provisioning: download a file (with
    /// progress, atomic .part rename) and download-then-extract a zip. Kept separate
    /// from the precision-aware model manifest so the whisper binary/model
    /// provisioning can reuse the same transfer code.
    /// </summary>
    internal static class HttpDownload
    {
        private static readonly HttpClient Http = CreateHttp();

        /// <summary>
        /// Download <paramref name="url"/> to <paramref name="destPath"/>. Streams to a
        /// .part file and renames on success so a half-finished download never looks
        /// complete. Returns false (and cleans up) on any failure.
        /// </summary>
        public static bool DownloadFile(string url, string destPath, string displayName)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                string tmp = destPath + ".part";

                using (HttpResponseMessage resp = Http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    resp.EnsureSuccessStatusCode();
                    long? total = resp.Content.Headers.ContentLength;

                    using Stream src = resp.Content.ReadAsStream();
                    using (FileStream dst = File.Create(tmp))
                    {
                        var buffer = new byte[1 << 20];   // 1 MB
                        long read = 0;
                        long lastShown = -1;
                        int n;
                        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            dst.Write(buffer, 0, n);
                            read += n;
                            long mb = read / (1 << 20);
                            if (mb != lastShown)
                            {
                                lastShown = mb;
                                string pct = total is > 0 ? $"{read * 100 / total.Value}%" : "?";
                                Console.Error.Write($"\r  {displayName}  {pct}  ({mb} MB)        ");
                            }
                        }
                    }
                }

                Console.Error.WriteLine();
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(tmp, destPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  download failed: {url}");
                Console.Error.WriteLine($"  {ex.Message}");
                try { if (File.Exists(destPath + ".part")) File.Delete(destPath + ".part"); } catch { /* ignore */ }
                return false;
            }
        }

        /// <summary>
        /// Download a zip to a temp file and extract it into <paramref name="destDir"/>
        /// (created if needed, existing entries overwritten). Returns false on failure.
        /// </summary>
        public static bool DownloadAndExtractZip(string url, string destDir, string displayName)
        {
            string tmpZip = Path.Combine(Path.GetTempPath(),
                $"p3k-{Path.GetFileNameWithoutExtension(destDir)}-{Guid.NewGuid():N}.zip");
            try
            {
                if (!DownloadFile(url, tmpZip, displayName))
                    return false;

                Directory.CreateDirectory(destDir);
                Console.Error.WriteLine($"  extracting {displayName} -> {destDir} ...");
                ZipFile.ExtractToDirectory(tmpZip, destDir, overwriteFiles: true);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  extract failed: {displayName}");
                Console.Error.WriteLine($"  {ex.Message}");
                return false;
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { /* ignore */ }
            }
        }

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("p3k.audio_transcriber/1.0");
            return http;
        }
    }
}
