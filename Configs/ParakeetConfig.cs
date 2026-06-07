using System.Text.Json.Serialization;

namespace p3k.audio_transcriber.Configs
{
    /// <summary>
    /// Settings for the offline Parakeet (sherpa-onnx) recogniser. The four model
    /// file paths are derived from <see cref="ParakeetDir"/> (resolved relative to
    /// the exe), so the config only needs the directory plus tuning knobs.
    /// </summary>
    internal sealed record ParakeetConfig
    {
        public string ParakeetDir { get; init; } = "models/parakeet";

        /// <summary>
        /// Model precision: "fp32" (default, ~2.4 GB, higher accuracy) or "int8" (~630 MB,
        /// fastest). The two tiers use different file names (encoder.onnx vs
        /// encoder.int8.onnx) so they coexist in the same dir. Overridable at launch with
        /// the --fp32 flag.
        /// </summary>
        public string Precision { get; init; } = "fp32";

        public int FeatureDim { get; init; } = 80;
        public string ModelType { get; init; } = "nemo_transducer";
        public int NumThreads { get; init; } = 6;            // Ryzen 5 5600X = 6 physical cores
        public string Provider { get; init; } = "cpu";
        public int Debug { get; init; } = 0;
        public string DecodingMethod { get; init; } = "greedy_search";

        [JsonIgnore] public bool IsFp32 => string.Equals(Precision, "fp32", StringComparison.OrdinalIgnoreCase);

        // int8 files are suffixed ".int8.onnx"; fp32 are plain ".onnx". tokens.txt is shared.
        [JsonIgnore] private string Suffix => IsFp32 ? string.Empty : ".int8";

        // Absolute model paths, derived from ParakeetDir; never read from / written to the file.
        [JsonIgnore] public string ResolvedDir => AppPaths.Resolve(ParakeetDir);
        [JsonIgnore] public string Encoder => Path.Combine(ResolvedDir, $"encoder{Suffix}.onnx");
        [JsonIgnore] public string Decoder => Path.Combine(ResolvedDir, $"decoder{Suffix}.onnx");
        [JsonIgnore] public string Joiner => Path.Combine(ResolvedDir, $"joiner{Suffix}.onnx");
        [JsonIgnore] public string Tokens => Path.Combine(ResolvedDir, "tokens.txt");
    }
}
