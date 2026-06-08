using NAudio.Wave;
using p3k.audio_transcriber.Audio;
using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Transcription;
using p3k.audio_transcriber.Vad;

namespace p3k.audio_transcriber.Runtime
{
    /// <summary>Owns the live mic/TCP/loopback source lanes and their per-source transcribers.</summary>
    internal sealed class SourceRuntime : IDisposable
    {
        // VAD path (Parakeet / Whisper / Compare)
        private ISegmentTranscriber? _micTranscriber;
        private WaveInEvent? _waveIn;
        private SourcePipeline? _micPipeline;

        private ISegmentTranscriber? _tcpTranscriber;
        private PcmTcpSource? _tcpSource;
        private SourcePipeline? _tcpPipeline;

        private ISegmentTranscriber? _loopbackTranscriber;
        private WasapiLoopbackSource? _loopbackSource;
        private SourcePipeline? _loopbackPipeline;

        // Streaming path (online Zipformer, no VAD)
        private StreamingPipeline? _micStreamingPipeline;
        private StreamingPipeline? _tcpStreamingPipeline;
        private StreamingPipeline? _loopbackStreamingPipeline;

        public void Start(AppConfig config, EngineMode mode, int sampleRate, TranscriptEmitter emitter)
        {
            if (mode == EngineMode.Streaming)
                StartStreaming(config, sampleRate, emitter);
            else
                StartVad(config, mode, sampleRate, emitter);
        }

        private void StartStreaming(AppConfig config, int sampleRate, TranscriptEmitter emitter)
        {
            string micLabel = config.Audio.Label;
            var micRecognizer = StreamingFactory.CreateRecognizer(config.Streaming, sampleRate);
            _micStreamingPipeline = new StreamingPipeline(
                micLabel, micRecognizer, sampleRate,
                (text, isFinal) => emitter.EmitStreaming(text, isFinal, micLabel));
            _waveIn = WavInFactory.CreateWavIn(config.Audio, _micStreamingPipeline.Queue);
            _micStreamingPipeline.Start();
            _waveIn.StartRecording();

            if (config.TcpSource.Enabled)
            {
                string tcpLabel = config.TcpSource.Label;
                var tcpRecognizer = StreamingFactory.CreateRecognizer(config.Streaming, sampleRate);
                _tcpStreamingPipeline = new StreamingPipeline(
                    tcpLabel, tcpRecognizer, sampleRate,
                    (text, isFinal) => emitter.EmitStreaming(text, isFinal, tcpLabel));
                _tcpSource = new PcmTcpSource(config.TcpSource.Host, config.TcpSource.Port, _tcpStreamingPipeline.Queue);
                _tcpStreamingPipeline.Start();
                _tcpSource.Start();
                Console.Error.WriteLine(
                    $"TCP source (streaming): listening on {config.TcpSource.Host}:{config.TcpSource.Port} " +
                    $"(tag '{config.TcpSource.Label}').");
            }

            if (config.Loopback.Enabled)
            {
                string loopbackLabel = config.Loopback.Label;
                var loopbackRecognizer = StreamingFactory.CreateRecognizer(config.Streaming, sampleRate);
                _loopbackStreamingPipeline = new StreamingPipeline(
                    loopbackLabel, loopbackRecognizer, sampleRate,
                    (text, isFinal) => emitter.EmitStreaming(text, isFinal, loopbackLabel));
                _loopbackSource = new WasapiLoopbackSource(_loopbackStreamingPipeline.Queue, sampleRate, config.Loopback.DeviceIndex);
                _loopbackStreamingPipeline.Start();
                _loopbackSource.Start();
                Console.Error.WriteLine($"Loopback (streaming): {_loopbackSource.DeviceName}  (tag '{loopbackLabel}').");
            }

            LogMic(config, sampleRate);
        }

        private void StartVad(AppConfig config, EngineMode mode, int sampleRate, TranscriptEmitter emitter)
        {
            var micTranscriber = RuntimeTranscriberFactory.Create(mode, config, sampleRate);
            _micTranscriber = micTranscriber;
            _micPipeline = new SourcePipeline(
                config.Audio.Label,
                VadFactory.CreateVAD(config.Vad, sampleRate),
                () => VadFactory.CreateVAD(config.Vad, sampleRate),
                (samples, label) => emitter.Emit(micTranscriber, samples, label),
                VadFactory.CreatePreRoll(config.Vad, sampleRate));
            _waveIn = WavInFactory.CreateWavIn(config.Audio, _micPipeline.Queue);
            _micPipeline.Start();
            _waveIn.StartRecording();

            if (config.TcpSource.Enabled)
            {
                var tcpTranscriber = RuntimeTranscriberFactory.Create(mode, config, sampleRate);
                _tcpTranscriber = tcpTranscriber;
                VadConfig tcpVad = config.TcpSource.Vad ?? config.Vad;
                _tcpPipeline = new SourcePipeline(
                    config.TcpSource.Label,
                    VadFactory.CreateVAD(tcpVad, sampleRate),
                    () => VadFactory.CreateVAD(tcpVad, sampleRate),
                    (samples, label) => emitter.Emit(tcpTranscriber, samples, label),
                    VadFactory.CreatePreRoll(tcpVad, sampleRate));
                _tcpSource = new PcmTcpSource(config.TcpSource.Host, config.TcpSource.Port, _tcpPipeline.Queue);
                _tcpPipeline.Start();
                _tcpSource.Start();
                Console.Error.WriteLine(
                    $"TCP source: listening on {config.TcpSource.Host}:{config.TcpSource.Port} " +
                    $"(tag '{config.TcpSource.Label}').");
            }

            if (config.Loopback.Enabled)
            {
                var loopbackTranscriber = RuntimeTranscriberFactory.Create(mode, config, sampleRate);
                _loopbackTranscriber = loopbackTranscriber;
                VadConfig loopbackVad = config.Loopback.Vad ?? config.Vad;
                _loopbackPipeline = new SourcePipeline(
                    config.Loopback.Label,
                    VadFactory.CreateVAD(loopbackVad, sampleRate),
                    () => VadFactory.CreateVAD(loopbackVad, sampleRate),
                    (samples, label) => emitter.Emit(loopbackTranscriber, samples, label),
                    VadFactory.CreatePreRoll(loopbackVad, sampleRate),
                    maxForcedCutSeconds: loopbackVad.MaxSpeechDuration,
                    sampleRate: sampleRate);
                _loopbackSource = new WasapiLoopbackSource(_loopbackPipeline.Queue, sampleRate, config.Loopback.DeviceIndex);
                _loopbackPipeline.Start();
                _loopbackSource.Start();
                Console.Error.WriteLine($"Loopback: {_loopbackSource.DeviceName}  (tag '{config.Loopback.Label}').");
            }

            LogMic(config, sampleRate);
        }

        private static void LogMic(AppConfig config, int sampleRate)
        {
            string mic = WaveInEvent.DeviceCount > config.Audio.DeviceNumber
                ? WaveInEvent.GetCapabilities(config.Audio.DeviceNumber).ProductName
                : "default input";
            Console.Error.WriteLine(
                $"Mic: {mic}  ({sampleRate} Hz, {config.Audio.Channels} ch, tag '{config.Audio.Label}')");
        }

        public void Stop()
        {
            _waveIn?.StopRecording();
            _tcpSource?.Dispose();
            _loopbackSource?.Dispose();

            _micPipeline?.Complete();
            _tcpPipeline?.Complete();
            _loopbackPipeline?.Complete();
            _micStreamingPipeline?.Complete();
            _tcpStreamingPipeline?.Complete();
            _loopbackStreamingPipeline?.Complete();

            _waveIn?.Dispose();
            _micPipeline?.Dispose();
            _tcpPipeline?.Dispose();
            _loopbackPipeline?.Dispose();
            _micStreamingPipeline?.Dispose();
            _tcpStreamingPipeline?.Dispose();
            _loopbackStreamingPipeline?.Dispose();
            _micTranscriber?.Dispose();
            _tcpTranscriber?.Dispose();
            _loopbackTranscriber?.Dispose();

            _waveIn = null;
            _micPipeline = null;
            _micTranscriber = null;
            _tcpSource = null;
            _tcpPipeline = null;
            _tcpTranscriber = null;
            _loopbackSource = null;
            _loopbackPipeline = null;
            _loopbackTranscriber = null;
            _micStreamingPipeline = null;
            _tcpStreamingPipeline = null;
            _loopbackStreamingPipeline = null;
        }

        public void Dispose() => Stop();
    }
}
