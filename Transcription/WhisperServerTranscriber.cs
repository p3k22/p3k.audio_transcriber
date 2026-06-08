using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using p3k.audio_transcriber.Audio;

namespace p3k.audio_transcriber.Transcription
{
    /// <summary>
    /// Decodes each segment on an external whisper.cpp server (GPU-accelerated via
    /// Vulkan on this machine). The segment is encoded to a WAV and POSTed to the
    /// server's /inference endpoint; the JSON reply's "text" field is the transcript.
    ///
    /// The transcriber does not manage the server's lifetime — it must already be
    /// running. Calls are synchronous (the caller is the source's worker thread, and
    /// decoding a segment is meant to block until its text is ready). A failed request
    /// returns "" rather than throwing, so a transient server hiccup drops one segment
    /// instead of tearing down the source.
    /// </summary>
    internal sealed class WhisperServerTranscriber : ISegmentTranscriber
    {
        private readonly HttpClient _http;
        private readonly string _inferenceUrl;

        public WhisperServerTranscriber(string baseUrl)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _inferenceUrl = baseUrl.TrimEnd('/') + "/inference";
        }

        public IReadOnlyList<TranscriptResult> Transcribe(float[] samples, int sampleRate) =>
            new[] { new TranscriptResult(null, Decode(samples, sampleRate)) };

        private string Decode(float[] samples, int sampleRate)
        {
            try
            {
                byte[] wav = WavBytes.FromMono(samples, sampleRate);

                using var form = new MultipartFormDataContent();
                var audio = new ByteArrayContent(wav);
                audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(audio, "file", "segment.wav");
                form.Add(new StringContent("json"), "response_format");
                form.Add(new StringContent("en"), "language");

                using HttpResponseMessage resp =
                    _http.PostAsync(_inferenceUrl, form).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("text", out JsonElement t)
                    ? (t.GetString() ?? string.Empty).Trim()
                    : string.Empty;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"whisper-server request failed: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// True if a whisper.cpp server answers at <paramref name="baseUrl"/>. Any HTTP
        /// response (even an error status) counts as reachable; only a connection failure
        /// means "down". Used once at startup to decide whether to use it or fall back.
        /// </summary>
        public static bool IsReachable(string baseUrl)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using HttpResponseMessage resp =
                    http.GetAsync(baseUrl.TrimEnd('/') + "/").GetAwaiter().GetResult();
                return true;   // connected and got a response of some kind
            }
            catch
            {
                return false;  // nothing listening / refused
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
