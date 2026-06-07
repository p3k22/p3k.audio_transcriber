using System.Net.Http.Headers;
using System.Text;

namespace p3k.audio_transcriber.Net
{
    internal static class RemoteLogger
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public static void Post(string url, string token, string line)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(line, Encoding.UTF8, "text/plain")
                    };
                    if (!string.IsNullOrWhiteSpace(token))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    using HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        Console.Error.WriteLine($"[remote-log] {(int)resp.StatusCode} {resp.ReasonPhrase}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[remote-log] POST failed: {ex.Message}");
                }
            });
        }
    }
}
