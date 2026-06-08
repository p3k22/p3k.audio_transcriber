using System;
using System.IO;

namespace p3k.audio_transcriber.Audio
{
    /// <summary>
    /// Encodes mono float samples (-1..1) into an in-memory 16-bit PCM WAV, the
    /// format whisper.cpp's server expects when a segment is POSTed for decoding.
    /// </summary>
    internal static class WavBytes
    {
        public static byte[] FromMono(float[] samples, int sampleRate)
        {
            int dataBytes = samples.Length * 2;            // 16-bit
            using var ms = new MemoryStream(44 + dataBytes);
            using var w = new BinaryWriter(ms);

            // RIFF header
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);                       // chunk size
            w.Write(new[] { 'W', 'A', 'V', 'E' });

            // fmt subchunk (PCM, mono)
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);                                   // subchunk1 size
            w.Write((short)1);                             // audio format = PCM
            w.Write((short)1);                             // channels = mono
            w.Write(sampleRate);
            w.Write(sampleRate * 2);                       // byte rate (sr * blockalign)
            w.Write((short)2);                             // block align (mono * 16-bit)
            w.Write((short)16);                            // bits per sample

            // data subchunk
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            foreach (float s in samples)
            {
                int v = (int)MathF.Round(Math.Clamp(s, -1f, 1f) * 32767f);
                w.Write((short)v);
            }

            w.Flush();
            return ms.ToArray();
        }
    }
}
