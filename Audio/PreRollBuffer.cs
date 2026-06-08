namespace p3k.audio_transcriber.Audio
{
    /// <summary>
    /// Fixed-size ring holding the most recent audio fed to a single VAD, indexed by
    /// the same absolute sample position the VAD reports as <c>SpeechSegment.Start</c>.
    ///
    /// Silero marks speech-start only after the speech probability has climbed past the
    /// threshold, and the segment it returns begins at that point — so a soft sentence
    /// opening (a breathy "h", "s", "th", or a volume ramp) is clipped. This buffer lets
    /// us recover the few hundred ms just before <c>Start</c> and splice it back on, so
    /// the recogniser sees the real beginning of the utterance.
    ///
    /// Fed and read from a single worker thread per source, so no locking is needed.
    /// </summary>
    internal sealed class PreRollBuffer
    {
        private readonly float[] _ring;
        private readonly int _capacity;
        private readonly int _padSamples;
        private long _written;   // total samples ever appended == absolute index of next write

        public PreRollBuffer(int capacity, int padSamples)
        {
            _padSamples = Math.Max(padSamples, 0);
            // The ring must outlast the oldest sample a segment can reference (the start
            // of a buffered utterance), plus the pre-roll we want to read before it.
            _capacity = Math.Max(capacity, _padSamples + 1);
            _ring = new float[_capacity];
        }

        /// <summary>Append a block in the same order, and at the same time, it is fed to the VAD.</summary>
        public void Append(float[] block)
        {
            int n = block.Length;
            if (n >= _capacity)
            {
                // Block bigger than the whole ring (shouldn't happen for ~100 ms reads):
                // keep only its tail.
                Array.Copy(block, n - _capacity, _ring, 0, _capacity);
                _written += n;
                return;
            }
            int pos = (int)(_written % _capacity);
            int first = Math.Min(n, _capacity - pos);
            Array.Copy(block, 0, _ring, pos, first);
            if (n > first)
                Array.Copy(block, first, _ring, 0, n - first);
            _written += n;
        }

        /// <summary>
        /// Prepend up to padSamples of audio captured immediately before
        /// <paramref name="speechStart"/> onto <paramref name="segment"/>. Returns the
        /// segment unchanged when no pre-roll is available (speech started at sample 0,
        /// or the lead-in has already rolled out of the ring).
        /// </summary>
        public float[] Prepend(int speechStart, float[] segment)
        {
            long from = speechStart - _padSamples;
            long oldest = _written - _capacity;   // earliest sample still in the ring
            if (from < oldest) from = oldest;
            if (from < 0) from = 0;

            int padLen = (int)(speechStart - from);
            if (padLen <= 0) return segment;

            var combined = new float[padLen + segment.Length];
            for (int i = 0; i < padLen; i++)
                combined[i] = _ring[(int)((from + i) % _capacity)];
            Array.Copy(segment, 0, combined, padLen, segment.Length);
            return combined;
        }
    }
}
