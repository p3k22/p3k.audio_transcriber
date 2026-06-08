using System.Text.Json.Serialization;

namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Settings for the online (streaming) Zipformer RNN-T recogniser. Unlike the offline
    /// Parakeet pipeline, this mode bypasses Silero VAD entirely: endpoint detection is
    /// handled by the model itself (trailing-silence rules), so transcription appears
    /// word-by-word while you speak and a final line is emitted when the sentence ends.
    ///
    /// Default model: sherpa-onnx-streaming-zipformer-en-2023-06-26 (~73 MB int8, chunk-16-left-128).
    /// Files are auto-downloaded from HuggingFace on first use.
    /// </summary>
    internal sealed record StreamingConfig
    {
        public string StreamingDir { get; init; } = "models/streaming";
        public int FeatureDim { get; init; } = 80;
        public int NumThreads { get; init; } = 6;
        public string Provider { get; init; } = "cpu";
        public int Debug { get; init; } = 0;
        public string DecodingMethod { get; init; } = "modified_beam_search";

        /// <summary>1 = enable endpoint detection (trailing-silence rules). 0 = stream until max length.</summary>
        public int EnableEndpoint { get; init; } = 1;

        /// <summary>Seconds of trailing silence that always closes a segment (any length).</summary>
        public float Rule1MinTrailingSilence { get; init; } = 2.4f;

        /// <summary>Seconds of trailing silence for longer utterances (see Rule3).</summary>
        public float Rule2MinTrailingSilence { get; init; } = 1.2f;

        /// <summary>Minimum utterance length (seconds) before Rule2 kicks in.</summary>
        public float Rule3MinUtteranceLength { get; init; } = 20.0f;

        [JsonIgnore] public string ResolvedDir => AppPaths.Resolve(StreamingDir);
        [JsonIgnore] public string Encoder => Path.Combine(ResolvedDir, "encoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx");
        [JsonIgnore] public string Decoder => Path.Combine(ResolvedDir, "decoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx");
        [JsonIgnore] public string Joiner => Path.Combine(ResolvedDir, "joiner-epoch-99-avg-1-chunk-16-left-128.int8.onnx");
        [JsonIgnore] public string Tokens => Path.Combine(ResolvedDir, "tokens.txt");

        internal const string ModelBaseUrl =
            "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/resolve/main/";
    }
}
