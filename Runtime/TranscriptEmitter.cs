using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Display;
using p3k.audio_transcriber.Net;
using p3k.audio_transcriber.Transcription;

namespace p3k.audio_transcriber.Runtime
{
    internal sealed class TranscriptEmitter
    {
        private int _sampleRate;
        private RemoteLogConfig _remoteLog = new();

        private readonly SharedConsole _console = new();
        private readonly Dictionary<string, ConsoleWindow> _windows = new();

        public void ClearScreen() => _console.ClearScreen();

        public void Configure(int sampleRate, RemoteLogConfig remoteLog)
        {
            _sampleRate = sampleRate;
            _remoteLog = remoteLog;
        }

        public void ConfigureWindows(AppConfig config)
        {
            DisposeWindows();
            if (config.Audio.Window) OpenWindow(config.Audio.Label);
            if (config.TcpSource.Enabled && config.TcpSource.Window) OpenWindow(config.TcpSource.Label);
            if (config.Loopback.Enabled && config.Loopback.Window) OpenWindow(config.Loopback.Label);
        }

        public void DisposeWindows()
        {
            foreach (var w in _windows.Values) w.Dispose();
            _windows.Clear();
        }

        private void OpenWindow(string label)
        {
            try { _windows[label] = new ConsoleWindow(label); }
            catch (Exception ex) { Console.Error.WriteLine($"Could not open display window for '{label}': {ex.Message}"); }
        }

        public void Emit(ISegmentTranscriber transcriber, float[] samples, string label)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            foreach (TranscriptResult r in transcriber.Transcribe(samples, _sampleRate))
            {
                if (r.Text.Length == 0) continue;
                string tag = r.Engine is null ? label : $"{label}/{r.Engine}";
                string line = $"[{stamp}] [{tag}] {r.Text}";

                if (_windows.TryGetValue(label, out var win))
                    win.Write(line, isPartial: false);
                else
                    _console.CommitFinal(label, line);

                RemoteLogger.Post(_remoteLog.Url, _remoteLog.Token, line);
            }
        }

        public void EmitStreaming(string text, bool isFinal, string label)
        {
            if (text.Length == 0) return;
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            string tag = isFinal ? label : $"{label}~";
            string line = $"[{stamp}] [{tag}] {text}";

            if (_windows.TryGetValue(label, out var win))
            {
                win.Write(line, isPartial: !isFinal);
                if (isFinal) RemoteLogger.Post(_remoteLog.Url, _remoteLog.Token, line);
                return;
            }

            if (isFinal)
            {
                _console.CommitFinal(label, line);
                RemoteLogger.Post(_remoteLog.Url, _remoteLog.Token, line);
            }
            else
            {
                _console.UpdatePartial(label, line);
            }
        }
    }
}
