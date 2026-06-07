using p3k.audio_transcriber.Configs;
using p3k.audio_transcriber.Provisioning;
using p3k.audio_transcriber.Setup;

namespace p3k.audio_transcriber.Runtime
{
    /// <summary>
    /// The live transcription runtime. Owns config loading, engine resolution, source
    /// lifecycle, and teardown so it can be rebuilt cleanly on <see cref="Restart"/>.
    /// </summary>
    public sealed class TranscriptionSystem : IDisposable
    {
        private readonly object _gate = new();
        private readonly string? _configPath;
        private readonly bool _forceFp32;
        private readonly TranscriptEmitter _emitter = new();

        private EngineRuntime? _engine;
        private SourceRuntime? _sources;
        private bool _enginePrompted;

        public bool IsRunning { get; private set; }

        public TranscriptionSystem(string? configPath = null, bool forceFp32 = false)
        {
            _configPath = configPath;
            _forceFp32 = forceFp32;
        }

        /// <summary>Load config, validate, build the pipeline, and start listening.</summary>
        public bool Start()
        {
            lock (_gate)
            {
                if (IsRunning) return true;

                AppConfig config = ConfigLoader.Load(_configPath);

                if (!_enginePrompted)
                {
                    config = EngineSelection.EnsureSelected(config, _configPath);
                    _enginePrompted = true;
                }

                if (_forceFp32 && !config.Parakeet.IsFp32)
                    config = config with { Parakeet = config.Parakeet with { Precision = "fp32" } };

                int sampleRate = config.Audio.SampleRate;
                _engine = new EngineRuntime();

                EngineConfig engine = config.Engine;
                EngineMode mode = _engine.ResolveMode(ref engine);
                config = config with { Engine = engine };

                Console.Error.WriteLine($"Engine: {EngineRuntime.DescribeMode(mode, config.Engine)} ...");

                bool needParakeet = mode is EngineMode.Parakeet or EngineMode.Compare;
                bool needStreaming = mode == EngineMode.Streaming;
                if (!DependencyProvisioner.Ensure(config, needParakeet, needStreaming))
                {
                    Console.Error.WriteLine("Dependencies missing; not starting.");
                    _engine.Dispose();
                    _engine = null;
                    return false;
                }

                if (!RuntimeValidator.Validate(config, needParakeet, needStreaming))
                {
                    Console.Error.WriteLine("Configuration is invalid; not starting.");
                    _engine.Dispose();
                    _engine = null;
                    return false;
                }

                _emitter.Configure(sampleRate, config.RemoteLog);
                _emitter.ConfigureWindows(config);
                _sources = new SourceRuntime();
                _sources.Start(config, mode, sampleRate, _emitter);

                IsRunning = true;
                Console.Error.WriteLine("READY");
                _emitter.ClearScreen();
                return true;
            }
        }

        /// <summary>Stop listening and dispose the pipeline (frees native models).</summary>
        public void Stop()
        {
            lock (_gate)
            {
                if (!IsRunning) return;

                _sources?.Dispose();
                _engine?.Dispose();
                _emitter.DisposeWindows();
                _sources = null;
                _engine = null;
                IsRunning = false;
            }
        }

        /// <summary>
        /// Reload config.json and rebuild the pipeline. Public entry point for
        /// external systems to apply config changes live. Returns false if the
        /// reloaded config is invalid (the system is left stopped in that case).
        /// </summary>
        public bool Restart()
        {
            lock (_gate)
            {
                Console.Error.WriteLine("Restarting - reloading config ...");
                Stop();
                return Start();
            }
        }

        public void Dispose() => Stop();
    }
}
