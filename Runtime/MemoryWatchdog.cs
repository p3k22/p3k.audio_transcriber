using System.Diagnostics;
using p3k.audio_transcriber.Configs;

namespace p3k.audio_transcriber.Runtime
{
    /// <summary>
    /// Polls this process's own resident memory (working set — the "in use" RAM the
    /// user watches) and recycles the transcription pipeline when it crosses the
    /// configured budget, so the native
    /// memory leaked by sherpa-onnx / ONNX Runtime (which the managed GC can't free)
    /// can't grow without bound. The recycle is <see cref="TranscriptionSystem.Restart"/>
    /// — a full Stop->Start that disposes and rebuilds the native models, dropping
    /// memory back to the baseline.
    ///
    /// To avoid cutting a live utterance, a recycle only fires after a quiet gap
    /// (no transcript emitted for QuietSeconds). If audio is continuous and never
    /// goes quiet, it force-recycles after MaxWaitSeconds so a constant-audio source
    /// can't defeat the cap. A MinRecycleSeconds cooldown prevents thrashing if the
    /// threshold is set too low.
    ///
    /// Independently of recycling, it logs one line per LogIntervalSeconds reporting
    /// managed-heap vs process-private memory, so native growth (the leak) is visible
    /// separately from the .NET heap.
    /// </summary>
    internal sealed class MemoryWatchdog : IDisposable
    {
        private readonly TranscriptionSystem _system;
        private readonly bool _enabled;
        private readonly long _thresholdBytes;
        private readonly int _quietMs;
        private readonly int _maxWaitMs;
        private readonly int _pollMs;
        private readonly int _logIntervalMs;
        private readonly int _minRecycleMs;

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _stop = new(false);
        private int _disposed;

        public MemoryWatchdog(TranscriptionSystem system, WatchdogConfig config)
        {
            _system = system;
            _enabled = config.Enabled;
            _thresholdBytes = (long)config.ThresholdMb * 1024 * 1024;
            _quietMs = (int)(config.QuietSeconds * 1000);
            _maxWaitMs = (int)(config.MaxWaitSeconds * 1000);
            _pollMs = Math.Max(1000, (int)(config.PollSeconds * 1000));
            _logIntervalMs = Math.Max(_pollMs, (int)(config.LogIntervalSeconds * 1000));
            _minRecycleMs = (int)(config.MinRecycleSeconds * 1000);
            _thread = new Thread(Loop) { IsBackground = true, Name = "mem-watchdog" };
        }

        public void Start() => _thread.Start();

        private void Loop()
        {
            using var proc = Process.GetCurrentProcess();
            var sinceLog = Stopwatch.StartNew();
            var overBudget = new Stopwatch();           // how long we've been over the threshold this episode
            DateTime lastRecycleUtc = DateTime.MinValue;

            LogMemory(proc, _enabled
                ? $"watchdog armed (recycle at {Mb(_thresholdBytes)} MB)"
                : "watchdog armed (logging only; recycle disabled)");

            while (!_stop.Wait(_pollMs))
            {
                proc.Refresh();
                long ws = proc.WorkingSet64;   // resident RAM — the "in use" number the user watches

                if (sinceLog.ElapsedMilliseconds >= _logIntervalMs)
                {
                    LogMemory(proc, null);
                    sinceLog.Restart();
                }

                if (!_enabled || ws < _thresholdBytes)
                {
                    overBudget.Reset();
                    continue;
                }

                if (!overBudget.IsRunning)
                {
                    overBudget.Restart();
                    Console.Error.WriteLine(
                        $"[watchdog] over budget: working-set {Mb(ws)} MB >= {Mb(_thresholdBytes)} MB; " +
                        "waiting for a quiet gap to recycle.");
                }

                // Respect the cooldown so a mis-set (too-low) threshold can't thrash-restart.
                if ((DateTime.UtcNow - lastRecycleUtc).TotalMilliseconds < _minRecycleMs)
                    continue;

                long idleMs = (long)(DateTime.UtcNow - _system.LastTranscriptUtc).TotalMilliseconds;
                bool quiet = idleMs >= _quietMs;
                bool forced = overBudget.ElapsedMilliseconds >= _maxWaitMs;
                if (!quiet && !forced)
                    continue;

                Console.Error.WriteLine(
                    $"[watchdog] recycling at working-set {Mb(ws)} MB " +
                    $"({(forced ? $"forced after {_maxWaitMs / 1000}s wait" : $"quiet {idleMs} ms")}).");

                try { _system.Restart(); }
                catch (Exception ex) { Console.Error.WriteLine($"[watchdog] recycle failed: {ex.Message}"); }

                lastRecycleUtc = DateTime.UtcNow;
                overBudget.Reset();
                proc.Refresh();
                LogMemory(proc, "after recycle");
                sinceLog.Restart();

                if (proc.WorkingSet64 >= _thresholdBytes)
                    Console.Error.WriteLine(
                        "[watchdog] still over budget right after a recycle — ThresholdMb is below the " +
                        "model baseline; raise it or the worker will recycle every cooldown.");
            }
        }

        private static long Mb(long bytes) => bytes / (1024 * 1024);

        private static void LogMemory(Process proc, string? note)
        {
            proc.Refresh();
            long priv = proc.PrivateMemorySize64;
            long ws = proc.WorkingSet64;
            long managed = GC.GetTotalMemory(forceFullCollection: false);
            string suffix = note is null ? "" : $"  ({note})";
            Console.Error.WriteLine(
                $"[mem] private {Mb(priv)} MB | working-set {Mb(ws)} MB | managed-heap {Mb(managed)} MB{suffix}");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Set();
            _thread.Join(2000);
            _stop.Dispose();
        }
    }
}
