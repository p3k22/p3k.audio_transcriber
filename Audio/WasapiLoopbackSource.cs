using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace p3k.audio_transcriber.Audio
{
    /// <summary>
    /// Captures system audio output (whatever Windows is playing) via WASAPI loopback
    /// and pushes mono float samples resampled to the pipeline's target rate into
    /// <paramref name="queue"/>. One instance per loopback source; dispose to stop.
    ///
    /// When constructed with DeviceIndex -1 (default device), a background thread polls
    /// the Windows default render device every 300 ms and restarts capture whenever it
    /// changes, so device switches via Volume2 or Sound settings are handled automatically.
    /// RecordingStopped with an exception triggers the same restart path for physical
    /// device removal/disconnection.
    /// </summary>
    internal sealed class WasapiLoopbackSource : IDisposable
    {
        private WasapiLoopbackCapture _capture;
        private readonly BlockingCollection<float[]> _queue;
        private readonly int _targetRate;
        private readonly bool _trackDefault;
        private readonly object _restartLock = new();
        private string? _trackedDeviceId;
        private DateTime _lastRestartUtc = DateTime.MinValue;
        private bool _disposed;

        public string DeviceName { get; private set; }

        public WasapiLoopbackSource(BlockingCollection<float[]> queue, int targetRate, int deviceIndex = -1)
        {
            _queue = queue;
            _targetRate = targetRate;
            _trackDefault = deviceIndex < 0;

            if (deviceIndex < 0)
            {
                _capture = new WasapiLoopbackCapture();
                DeviceName = "default playback device";
            }
            else
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                if (deviceIndex >= devices.Count)
                    throw new ArgumentOutOfRangeException(nameof(deviceIndex),
                        $"Loopback DeviceIndex {deviceIndex} is out of range (0..{devices.Count - 1}).");
                var device = devices[deviceIndex];
                DeviceName = device.FriendlyName;
                _capture = new WasapiLoopbackCapture(device);
            }

            HookCapture(_capture);
        }

        public void Start()
        {
            if (_trackDefault)
            {
                // Snapshot the current default device ID so the watcher can detect changes
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _trackedDeviceId = device.ID;
                }
                catch { }

                var watcher = new Thread(WatchDefaultDevice)
                {
                    IsBackground = true,
                    Name = "loopback-watcher"
                };
                watcher.Start();
            }

            lock (_restartLock)
                _capture.StartRecording();
        }

        private void WatchDefaultDevice()
        {
            while (!_disposed)
            {
                Thread.Sleep(300);
                if (_disposed) break;

                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    string currentId = device.ID;

                    if (currentId != _trackedDeviceId)
                    {
                        _trackedDeviceId = currentId;
                        TryRestartCapture("default device changed");
                    }
                }
                catch { }
            }
        }

        private void HookCapture(WasapiLoopbackCapture capture)
        {
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
        }

        private void UnhookCapture(WasapiLoopbackCapture capture)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_disposed || e.BytesRecorded == 0) return;

            WaveFormat fmt;
            lock (_restartLock)
            {
                if (_disposed) return;
                fmt = _capture.WaveFormat;
            }

            float[] mono = ToMonoFloat(e.Buffer, e.BytesRecorded, fmt);
            float[] samples = fmt.SampleRate == _targetRate
                ? mono
                : LinearResample(mono, fmt.SampleRate, _targetRate);
            _queue.TryAdd(samples);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            // Physical device removal — watcher won't catch this because GetDefaultAudioEndpoint
            // will also fail or return something different, but handle it explicitly just in case
            if (!_trackDefault || _disposed || e.Exception == null) return;
            ThreadPool.QueueUserWorkItem(_ => TryRestartCapture($"session interrupted: {e.Exception.Message}"));
        }

        private void TryRestartCapture(string reason)
        {
            // Debounce: ignore if we restarted within the last 1 s
            if ((DateTime.UtcNow - _lastRestartUtc).TotalSeconds < 1) return;

            lock (_restartLock)
            {
                if (_disposed) return;
                if ((DateTime.UtcNow - _lastRestartUtc).TotalSeconds < 1) return;

                Console.Error.WriteLine($"Loopback: {reason}, restarting...");

                for (int attempt = 1; attempt <= 10; attempt++)
                {
                    try
                    {
                        UnhookCapture(_capture);
                        try { _capture.StopRecording(); } catch { }
                        _capture.Dispose();

                        _capture = new WasapiLoopbackCapture();
                        HookCapture(_capture);
                        _capture.StartRecording();
                        _lastRestartUtc = DateTime.UtcNow;
                        Console.Error.WriteLine("Loopback: restarted on new default playback device.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Loopback: restart attempt {attempt}/10 failed: {ex.Message}");
                        Thread.Sleep(500);
                    }
                }

                Console.Error.WriteLine("Loopback: gave up restarting after device change.");
            }
        }

        /// <summary>
        /// Convert raw capture bytes to a mono float[] at the capture sample rate.
        /// Handles IEEE float (32-bit), PCM 16-bit, PCM 24-bit, and PCM 32-bit.
        /// Multi-channel audio is downmixed by averaging channels.
        /// </summary>
        private static float[] ToMonoFloat(byte[] buf, int count, WaveFormat fmt)
        {
            int channels = fmt.Channels;
            int bytesPerSample = fmt.BitsPerSample / 8;
            bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;
            int frames = count / (bytesPerSample * channels);
            var mono = new float[frames];

            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int off = (f * channels + ch) * bytesPerSample;
                    sum += fmt.BitsPerSample switch
                    {
                        32 when isFloat => BitConverter.ToSingle(buf, off),
                        32 => BitConverter.ToInt32(buf, off) / 2_147_483_648f,
                        24 => ((buf[off] | buf[off + 1] << 8 | (sbyte)buf[off + 2] << 16)) / 8_388_608f,
                        16 => (short)(buf[off] | buf[off + 1] << 8) / 32_768f,
                        _ => 0f,
                    };
                }
                mono[f] = sum / channels;
            }

            return mono;
        }

        /// <summary>Linear interpolation resample, sufficient quality for speech at 16 kHz.</summary>
        private static float[] LinearResample(float[] src, int fromRate, int toRate)
        {
            if (src.Length == 0) return src;
            int outLen = (int)((long)src.Length * toRate / fromRate);
            if (outLen == 0) return Array.Empty<float>();
            var out_ = new float[outLen];
            double step = (double)(src.Length - 1) / Math.Max(1, outLen - 1);
            for (int i = 0; i < outLen; i++)
            {
                double pos = i * step;
                int idx = (int)pos;
                double frac = pos - idx;
                out_[i] = (float)(src[idx] + frac * ((idx + 1 < src.Length ? src[idx + 1] : src[idx]) - src[idx]));
            }
            return out_;
        }

        public void Dispose()
        {
            lock (_restartLock)
            {
                if (_disposed) return;
                _disposed = true;
                UnhookCapture(_capture);
                try { _capture.StopRecording(); } catch { }
                _capture.Dispose();
            }
        }
    }
}
