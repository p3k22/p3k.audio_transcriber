using System.Collections.Generic;
using SherpaOnnx;

namespace p3k.audio_transcriber.Transcription
{
    /// <summary>
    /// In-process recogniser backed by a sherpa-onnx Parakeet <see cref="OfflineRecognizer"/>.
    /// Owns the recogniser and disposes it. sherpa-onnx Decode is not concurrency-safe,
    /// but each source has its own instance and calls it from a single thread, so no lock.
    /// </summary>
    internal sealed class ParakeetTranscriber : ISegmentTranscriber
    {
        private readonly OfflineRecognizer _recognizer;

        public ParakeetTranscriber(OfflineRecognizer recognizer) => _recognizer = recognizer;

        public IReadOnlyList<TranscriptResult> Transcribe(float[] samples, int sampleRate)
        {
            using OfflineStream stream = _recognizer.CreateStream();
            stream.AcceptWaveform(sampleRate, samples);
            _recognizer.Decode(stream);
            return new[] { new TranscriptResult(null, stream.Result.Text.Trim()) };
        }

        public void Dispose() => _recognizer.Dispose();
    }
}
