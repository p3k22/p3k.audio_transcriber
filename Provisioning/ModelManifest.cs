using p3k.audio_transcriber.Configs;

namespace p3k.audio_transcriber.Provisioning
{
    /// <summary>Precision-aware list of local model files required by the selected runtime mode.</summary>
    internal static class ModelManifest
    {
        private const string ParakeetInt8Base =
            "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/";
        private const string ParakeetFp32Base =
            "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2/resolve/main/";
        private const string VadUrl =
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx";

        public static IReadOnlyList<RequiredFile> Required(AppConfig config, bool needParakeet, bool needStreaming = false)
        {
            var files = new List<RequiredFile>();

            if (needParakeet)
            {
                ParakeetConfig p = config.Parakeet;
                string baseUrl = p.IsFp32 ? ParakeetFp32Base : ParakeetInt8Base;

                RequiredFile FromPath(string path, long minBytes) =>
                    new(Path.GetFileName(path), path, baseUrl + Path.GetFileName(path), minBytes);

                files.Add(FromPath(p.Encoder, p.IsFp32 ? 30_000_000 : 100_000_000));
                files.Add(FromPath(p.Decoder, 100_000));
                files.Add(FromPath(p.Joiner, 100_000));
                files.Add(FromPath(p.Tokens, 100));

                if (p.IsFp32)
                {
                    string weightsPath = Path.Combine(p.ResolvedDir, "encoder.weights");
                    files.Add(new RequiredFile("encoder.weights", weightsPath,
                        baseUrl + "encoder.weights", 1_000_000_000));
                }
            }

            if (needStreaming)
            {
                StreamingConfig s = config.Streaming;
                string b = StreamingConfig.ModelBaseUrl;
                files.Add(new RequiredFile(Path.GetFileName(s.Encoder), s.Encoder, b + Path.GetFileName(s.Encoder), 50_000_000));
                files.Add(new RequiredFile(Path.GetFileName(s.Decoder), s.Decoder, b + Path.GetFileName(s.Decoder), 1_000_000));
                files.Add(new RequiredFile(Path.GetFileName(s.Joiner),  s.Joiner,  b + Path.GetFileName(s.Joiner),  200_000));
                files.Add(new RequiredFile("tokens.txt", s.Tokens, b + "tokens.txt", 1_000));
            }

            // Silero VAD is required for all modes except streaming (which uses built-in endpoint detection).
            if (!needStreaming)
                files.Add(new RequiredFile("silero_vad.onnx", config.Vad.ResolvedModel, VadUrl, 100_000));

            return files;
        }
    }
}
