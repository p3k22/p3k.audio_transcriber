using System.Collections.Generic;

namespace p3k.audio_transcriber.Transcription
{
    /// <summary>
    /// One transcript line produced for a segment. <see cref="Engine"/> is the sub-tag
    /// identifying which recogniser produced it (e.g. "parakeet"/"whisper" in compare
    /// mode), or null for a single-engine source where no sub-tag is wanted.
    /// </summary>
    internal readonly record struct TranscriptResult(string? Engine, string Text);

    /// <summary>
    /// Decodes one finished VAD speech segment (mono float samples at the pipeline
    /// sample rate) into one or more transcript lines. A single-engine source returns
    /// exactly one line (with a null <see cref="TranscriptResult.Engine"/>); compare
    /// mode returns one line per engine. One instance is owned per audio source and
    /// called only from that source's single worker thread, so implementations need
    /// not be thread-safe across sources.
    /// </summary>
    internal interface ISegmentTranscriber : System.IDisposable
    {
        /// <summary>Transcribe <paramref name="samples"/>; lines with empty text are dropped by the caller.</summary>
        IReadOnlyList<TranscriptResult> Transcribe(float[] samples, int sampleRate);
    }
}
