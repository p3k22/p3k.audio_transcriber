namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Memory watchdog: caps the worker's footprint by recycling the transcription
    /// pipeline (the same Stop->Start used for config reloads) when the process's
    /// private/committed memory crosses a budget.
    ///
    /// sherpa-onnx / ONNX Runtime leak native memory that the managed GC cannot
    /// reclaim, so a long-running process climbs all day and only a full teardown
    /// frees it. Rebuilding the native models from scratch is the only reliable
    /// ceiling. The recycle waits for a quiet gap (no transcript emitted for
    /// <see cref="QuietSeconds"/>) so a live utterance isn't cut mid-sentence; if
    /// audio never goes quiet it force-recycles after <see cref="MaxWaitSeconds"/>
    /// so a constant-audio source (e.g. a TV on the mic) can't defeat the cap.
    /// </summary>
    internal sealed record WatchdogConfig
    {
        /// <summary>false = only log memory, never recycle. true = recycle on the budget.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>Resident memory (working set, the "RAM in use" number) in MB that triggers a
        /// recycle. Must sit above the model baseline (fp32 Parakeet's working set is ~4 GB) or it
        /// will recycle constantly.</summary>
        public int ThresholdMb { get; init; } = 6000;

        /// <summary>Seconds of no transcript output that count as a safe gap to recycle in.</summary>
        public float QuietSeconds { get; init; } = 3.0f;

        /// <summary>Cap on how long to wait for a quiet gap once over budget; force-recycle after this.</summary>
        public float MaxWaitSeconds { get; init; } = 120.0f;

        /// <summary>How often the watchdog samples memory / checks the budget.</summary>
        public float PollSeconds { get; init; } = 5.0f;

        /// <summary>How often a memory line (managed-heap vs process-private) is logged to stderr.</summary>
        public float LogIntervalSeconds { get; init; } = 60.0f;

        /// <summary>Minimum gap between two recycles, so a too-low threshold can't thrash-restart.</summary>
        public float MinRecycleSeconds { get; init; } = 60.0f;
    }
}
