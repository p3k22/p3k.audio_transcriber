using System.Collections.Concurrent;
using NAudio.Wave;
using p3k.audio_transcriber.Configs;

namespace p3k.audio_transcriber.Audio
{
    internal static class WavInFactory
    {
        internal static bool ValidateWavInConfig(WavInEventConfig c)
        {
            if (c.SampleRate <= 0)
            {
                Console.Error.WriteLine("Audio SampleRate must be positive.");
                return false;
            }
            if (c.BitDepth != 16)
            {
                // The DataAvailable handler converts 16-bit PCM -> float; only 16 is supported.
                Console.Error.WriteLine("Audio BitDepth must be 16.");
                return false;
            }
            if (c.Channels < 1 || c.Channels > 2)
            {
                Console.Error.WriteLine("Audio Channels must be 1 (mono) or 2 (stereo).");
                return false;
            }
            if (c.BufferMilliseconds <= 0)
            {
                Console.Error.WriteLine("Audio BufferMilliseconds must be positive.");
                return false;
            }
            if (c.DeviceNumber < 0 || c.DeviceNumber >= WaveInEvent.DeviceCount)
            {
                Console.Error.WriteLine(
                    $"Audio DeviceNumber {c.DeviceNumber} is out of range " +
                    $"(0..{WaveInEvent.DeviceCount - 1}).");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Build a mic capture that pushes mono float samples into <paramref name="queue"/>.
        /// Heavy work (VAD + decode) happens on the consumer side, so this callback
        /// stays light and the capture buffer never overflows mid-transcription.
        /// </summary>
        internal static WaveInEvent CreateWavIn(WavInEventConfig c, BlockingCollection<float[]> queue)
        {
            var waveIn = new WaveInEvent
            {
                DeviceNumber = c.DeviceNumber,
                WaveFormat = new WaveFormat(c.SampleRate, c.BitDepth, c.Channels),
                BufferMilliseconds = c.BufferMilliseconds,
            };

            int channels = c.Channels;
            waveIn.DataAvailable += (_, e) =>
            {
                int frames = e.BytesRecorded / 2 / channels;   // 16-bit samples per channel
                var samples = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    // Downmix to mono by averaging channels (mono = passthrough).
                    int acc = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int idx = 2 * (f * channels + ch);
                        acc += (short)(e.Buffer[idx] | (e.Buffer[idx + 1] << 8));
                    }
                    samples[f] = (acc / channels) / 32768f;
                }
                queue.TryAdd(samples);
            };

            return waveIn;
        }
    }
}
