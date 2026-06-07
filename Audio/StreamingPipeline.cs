using System.Collections.Concurrent;
using SherpaOnnx;

namespace p3k.audio_transcriber.Audio
{
    /// <summary>
    /// Streaming recognition lane: audio chunks are fed directly into an
    /// <see cref="OnlineRecognizer"/> without any VAD pre-segmentation. The recogniser's
    /// built-in trailing-silence endpoint rules decide when a sentence is done.
    ///
    /// The <paramref name="onResult"/> callback is invoked on the worker thread:
    ///   isFinal=false  → partial result (text grew since last call)
    ///   isFinal=true   → endpoint reached; full sentence text, then stream resets
    ///
    /// One pipeline per audio source; each source needs its own recogniser instance
    /// since OnlineRecognizer is not thread-safe across concurrent decode calls.
    /// </summary>
    internal sealed class StreamingPipeline : IDisposable
    {
        public string Label { get; }
        public BlockingCollection<float[]> Queue { get; }

        private readonly OnlineRecognizer _recognizer;
        private readonly int _sampleRate;
        private readonly Action<string, bool> _onResult;
        private Thread? _worker;

        public StreamingPipeline(string label, OnlineRecognizer recognizer,
                                 int sampleRate, Action<string, bool> onResult)
        {
            Label = label;
            _recognizer = recognizer;
            _sampleRate = sampleRate;
            _onResult = onResult;
            Queue = new BlockingCollection<float[]>(boundedCapacity: 256);
        }

        public void Start()
        {
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = $"stream-{Label}" };
            _worker.Start();
        }

        // 100 ms of silence at the pipeline sample rate, reused across injections.
        private float[] Silence => _silence ??= new float[_sampleRate / 10];
        private float[]? _silence;

        private void WorkerLoop()
        {
            using var stream = _recognizer.CreateStream();
            string lastPartial = "";

            // Use TryTake with a timeout instead of GetConsumingEnumerable so that
            // sources which stop producing audio (e.g. WASAPI loopback when playback
            // is paused) still receive silence frames. Without them the recogniser
            // never sees trailing silence and any pending partial hangs indefinitely.
            while (!Queue.IsCompleted)
            {
                float[] samples = Queue.TryTake(out var chunk, millisecondsTimeout: 500)
                    ? chunk
                    : Silence; // no audio for 500 ms — inject silence to drive endpoint detection

                stream.AcceptWaveform(_sampleRate, samples);

                while (_recognizer.IsReady(stream))
                    _recognizer.Decode(stream);

                var text = _recognizer.GetResult(stream).Text.Trim();

                if (_recognizer.IsEndpoint(stream))
                {
                    if (text.Length > 0)
                        _onResult(text, true);
                    _recognizer.Reset(stream);
                    lastPartial = "";
                }
                else if (text.Length > 0 && text != lastPartial)
                {
                    _onResult(text, false);
                    lastPartial = text;
                }
            }

            // Flush audio still buffered when the source closes.
            stream.InputFinished();
            while (_recognizer.IsReady(stream))
                _recognizer.Decode(stream);
        }

        public void Complete(int joinMs = 3000)
        {
            Queue.CompleteAdding();
            _worker?.Join(joinMs);
        }

        public void Dispose()
        {
            _recognizer.Dispose();
            Queue.Dispose();
        }
    }
}
