using System.Collections.Concurrent;
using SherpaOnnx;

namespace p3k.audio_transcriber.Audio
{
    /// <summary>
    /// One independent capture-to-VAD lane with two-thread design:
    ///   vad-{label}   — feeds audio into Silero VAD, enqueues completed segments
    ///   infer-{label} — drains the segment queue and calls the transcription callback
    ///
    /// For loopback sources (continuous audio with no reliable silence), the pipeline
    /// supports a soft/hard cut strategy via <paramref name="maxForcedCutSeconds"/>:
    ///   - Once that many samples have accumulated without a natural VAD cut, the
    ///     pipeline enters a grace window (50 % of the soft limit).
    ///   - During the grace window the VAD keeps running; if natural silence arrives
    ///     it cuts cleanly there (better word boundaries for Parakeet).
    ///   - If the grace window expires with no silence, a hard cut fires: the VAD is
    ///     flushed, whatever speech is buffered is submitted, and a fresh VAD instance
    ///     replaces the old one so state is clean for the next chunk.
    ///
    /// Mic and TCP sources pass maxForcedCutSeconds = 0 to disable the mechanism
    /// entirely and rely on natural VAD behaviour only.
    /// </summary>
    internal sealed class SourcePipeline : IDisposable
    {
        public string Label { get; }
        public BlockingCollection<float[]> Queue { get; }

        private VoiceActivityDetector _vad;
        private readonly Func<VoiceActivityDetector> _vadFactory;
        private readonly Action<float[], string> _onSegment;
        private readonly PreRollBuffer? _preRoll;
        private readonly int _maxForcedCutSamples;
        private readonly int _graceSamples;

        private readonly BlockingCollection<(float[] samples, string label)> _inferenceQueue = new();
        private Thread? _vadWorker;
        private Thread? _inferenceWorker;

        private int _samplesSinceLastCut;
        private bool _pastSoftLimit;
        private int _graceSamplesRemaining;

        public SourcePipeline(string label, VoiceActivityDetector vad,
                              Func<VoiceActivityDetector> vadFactory,
                              Action<float[], string> onSegment,
                              PreRollBuffer? preRoll = null,
                              float maxForcedCutSeconds = 0,
                              int sampleRate = 16000)
        {
            Label = label;
            _vad = vad;
            _vadFactory = vadFactory;
            _onSegment = onSegment;
            _preRoll = preRoll;
            _maxForcedCutSamples = maxForcedCutSeconds > 0 ? (int)(maxForcedCutSeconds * sampleRate) : 0;
            _graceSamples = (int)(_maxForcedCutSamples * 0.5f); // 50 % extra window to find natural silence
            Queue = new BlockingCollection<float[]>(boundedCapacity: 256);
        }

        public void Start()
        {
            _vadWorker = new Thread(VadLoop) { IsBackground = true, Name = $"vad-{Label}" };
            _inferenceWorker = new Thread(InferenceLoop) { IsBackground = true, Name = $"infer-{Label}" };
            _vadWorker.Start();
            _inferenceWorker.Start();
        }

        private void VadLoop()
        {
            foreach (float[] chunk in Queue.GetConsumingEnumerable())
            {
                _preRoll?.Append(chunk);
                _vad.AcceptWaveform(chunk);
                _samplesSinceLastCut += chunk.Length;

                if (DrainSegments())
                {
                    _samplesSinceLastCut = 0;
                    _pastSoftLimit = false;
                }
                else if (_maxForcedCutSamples > 0)
                {
                    if (!_pastSoftLimit && _samplesSinceLastCut >= _maxForcedCutSamples)
                    {
                        _pastSoftLimit = true;
                        _graceSamplesRemaining = _graceSamples;
                    }

                    if (_pastSoftLimit)
                    {
                        _graceSamplesRemaining -= chunk.Length;
                        if (_graceSamplesRemaining <= 0)
                            ForceCut();
                    }
                }
            }
            _vad.Flush();
            DrainSegments();
            _inferenceQueue.CompleteAdding();
        }

        private bool DrainSegments()
        {
            bool any = false;
            while (!_vad.IsEmpty())
            {
                SpeechSegment seg = _vad.Front();
                float[] samples = _preRoll != null
                    ? _preRoll.Prepend(seg.Start, seg.Samples)
                    : seg.Samples;
                _inferenceQueue.Add((samples, Label));
                _vad.Pop();
                any = true;
            }
            return any;
        }

        private void ForceCut()
        {
            _vad.Flush();
            DrainSegments();
            var old = _vad;
            _vad = _vadFactory();
            old.Dispose();
            _samplesSinceLastCut = 0;
            _pastSoftLimit = false;
        }

        private void InferenceLoop()
        {
            foreach (var (samples, label) in _inferenceQueue.GetConsumingEnumerable())
                _onSegment(samples, label);
        }

        public void Complete(int joinMs = 5000)
        {
            Queue.CompleteAdding();
            _vadWorker?.Join(joinMs);
            _inferenceWorker?.Join(joinMs);
        }

        public void Dispose()
        {
            _vad.Dispose();
            Queue.Dispose();
            _inferenceQueue.Dispose();
        }
    }
}
