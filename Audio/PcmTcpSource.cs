using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace p3k.audio_transcriber.Audio
{
    /// <summary>
    /// Loopback TCP listener for a second audio source. Accepts one PCM producer at
    /// a time and turns its raw 16-bit mono byte stream into float frames pushed into
    /// <paramref name="queue"/> — the same shape the mic produces, so the downstream
    /// VAD and recogniser are identical regardless of source.
    ///
    /// The producer's lifetime is independent: it can connect, drop, and reconnect
    /// freely without disturbing the mic pipeline.
    /// </summary>
    internal sealed class PcmTcpSource : IDisposable
    {
        private readonly BlockingCollection<float[]> _queue;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private Thread? _acceptThread;

        public PcmTcpSource(string host, int port, BlockingCollection<float[]> queue)
        {
            _queue = queue;
            _listener = new TcpListener(IPAddress.Parse(host), port);
        }

        public void Start()
        {
            _listener.Start();
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "tcp-source-accept" };
            _acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }

                Console.Error.WriteLine($"TCP source connected ({client.Client.RemoteEndPoint}).");
                try
                {
                    Pump(client);
                }
                catch (Exception ex) when (!_cts.IsCancellationRequested)
                {
                    Console.Error.WriteLine($"TCP source read error: {ex.Message}");
                }
                finally
                {
                    client.Dispose();
                    Console.Error.WriteLine("TCP source disconnected.");
                }
            }
        }

        /// <summary>
        /// Read 16-bit little-endian PCM and emit mono float frames. TCP can split a
        /// sample across reads, so a single leftover byte is carried into the next read.
        /// </summary>
        private void Pump(TcpClient client)
        {
            client.NoDelay = true;
            using NetworkStream stream = client.GetStream();

            var buffer = new byte[8192];
            bool haveCarry = false;
            byte carry = 0;

            while (!_cts.IsCancellationRequested)
            {
                int offset = 0;
                if (haveCarry) { buffer[0] = carry; offset = 1; }

                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) break;   // producer closed the connection

                int total = offset + read;
                int frames = total / 2;
                if (frames > 0)
                {
                    var samples = new float[frames];
                    for (int i = 0; i < frames; i++)
                    {
                        short s = (short)(buffer[2 * i] | (buffer[2 * i + 1] << 8));
                        samples[i] = s / 32768f;
                    }
                    _queue.TryAdd(samples);
                }

                haveCarry = (total & 1) == 1;
                if (haveCarry) carry = buffer[total - 1];
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* already stopped */ }
            _acceptThread?.Join(1000);
            _cts.Dispose();
        }
    }
}
