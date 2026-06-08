using System.Collections.Generic;
using System.Threading.Tasks;

namespace p3k.audio_transcriber.Transcription
{
    /// <summary>
    /// Runs two recognisers on the same VAD segment so their outputs can be compared
    /// side by side. Each segment yields one tagged line per engine (empty results are
    /// dropped by the caller), e.g.
    /// <code>
    /// [12:00:00] [mic/parakeet] the quick brown fox
    /// [12:00:00] [mic/whisper]  the quick brown fox
    /// </code>
    ///
    /// Parakeet decodes in-process on the CPU while whisper is a GPU/HTTP round-trip,
    /// so the two overlap: whisper is kicked off on a worker thread and parakeet runs
    /// on the calling thread, then both are joined. Latency in this mode is therefore
    /// max(parakeet, whisper) and is NOT representative of either engine run alone —
    /// it exists to judge transcript quality, not speed.
    /// </summary>
    internal sealed class ComparisonTranscriber : ISegmentTranscriber
    {
        private readonly ISegmentTranscriber _parakeet;
        private readonly ISegmentTranscriber _whisper;

        public ComparisonTranscriber(ISegmentTranscriber parakeet, ISegmentTranscriber whisper)
        {
            _parakeet = parakeet;
            _whisper = whisper;
        }

        public IReadOnlyList<TranscriptResult> Transcribe(float[] samples, int sampleRate)
        {
            Task<IReadOnlyList<TranscriptResult>> whisperTask =
                Task.Run(() => _whisper.Transcribe(samples, sampleRate));
            IReadOnlyList<TranscriptResult> parakeet = _parakeet.Transcribe(samples, sampleRate);
            IReadOnlyList<TranscriptResult> whisper = whisperTask.GetAwaiter().GetResult();

            var lines = new List<TranscriptResult>(2);
            foreach (TranscriptResult r in parakeet) lines.Add(r with { Engine = "parakeet" });
            foreach (TranscriptResult r in whisper) lines.Add(r with { Engine = "whisper" });
            return lines;
        }

        public void Dispose()
        {
            _parakeet.Dispose();
            _whisper.Dispose();
        }
    }
}
