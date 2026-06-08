using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace p3k.audio_transcriber.Display
{
    /// <summary>
    /// Spawns a copy of this exe in a new console window and pipes transcription
    /// lines to it over a named pipe. Each instance handles exactly one source label,
    /// so there is no shared cursor state and partial overwrite always works cleanly.
    ///
    /// Protocol: each line sent starts with '~' (partial) or ' ' (final), followed
    /// by the formatted transcript line. The child process renders accordingly.
    /// </summary>
    internal sealed class ConsoleWindow : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamWriter _writer;
        private readonly Process _child;
        private bool _disposed;

        public ConsoleWindow(string title)
        {
            string pipeName = $"p3k_{Guid.NewGuid():N}";

            _pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte);

            string exePath = Environment.ProcessPath!;
            _child = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                ArgumentList = { "--display", pipeName, title },
            })!;

            var connectTask = Task.Run(() => _pipe.WaitForConnection());
            if (!connectTask.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException($"Display window '{title}' did not connect.");

            _writer = new StreamWriter(_pipe, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public void Write(string line, bool isPartial)
        {
            if (_disposed) return;
            try { _writer.WriteLine((isPartial ? '~' : ' ') + line); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer.Dispose(); } catch { }
            try { _pipe.Dispose(); } catch { }
            try { if (!_child.HasExited) _child.Kill(); } catch { }
            _child.Dispose();
        }
    }
}
