using System.Text.RegularExpressions;
using System.Text.Json;
using NAudio.Wave;
using p3k.audio_transcriber.Configs;

namespace p3k.audio_transcriber.Setup
{
    /// <summary>
    /// Startup picker shown on every interactive launch. Prompts for engine, microphone,
    /// TCP source, and loopback source. On a true first run (no saved choice) it shows
    /// defaults; on later launches "use last settings" keeps everything with a single Enter.
    ///
    /// If the app is running headless (stdin redirected — i.e. launched as a managed
    /// child process) it can't prompt: it uses the saved choice if there is one, else
    /// defaults to Parakeet, which needs no GPU and always comes up.
    /// </summary>
    internal static class EngineSelection
    {
        private const string KeepLast = "__last__";
        private const string RemoteLog = "__remotelog__";

        public static AppConfig EnsureSelected(AppConfig config, string? configPath)
        {
            EngineConfig engine = config.Engine;
            bool hasSaved = !string.IsNullOrWhiteSpace(engine.Type);

            if (Console.IsInputRedirected)
            {
                if (hasSaved)
                    return config;   // headless: keep the saved choice
                Console.Error.WriteLine("No engine selected and no console to ask; defaulting to Parakeet.");
                return Persisted(config, configPath, "parakeet", "", config.Audio.DeviceNumber,
                    config.TcpSource.Enabled, config.Loopback.Enabled);
            }

            // The remote-log option just opens the token setup and re-shows the menu,
            // so loop until a real engine choice is picked.
            string type;
            while (true)
            {
                type = PromptEngine(hasSaved, engine, config.RemoteLog.Enabled);
                if (type != RemoteLog) break;
                var updatedRemoteLog = RemoteLogSetup.Prompt(config.RemoteLog);
                config = config with { RemoteLog = updatedRemoteLog };
                PersistRemoteLog(ConfigLoader.Resolve(configPath), updatedRemoteLog);
            }
            if (type == KeepLast)
                return config;       // keep last settings untouched

            // "whisper" and "compare" drive the whisper server, so both need a build choice.
            bool needsWhisper = type is "whisper" or "compare";
            string whisperBuild = needsWhisper ? PromptWhisperBuild(engine.WhisperBuild) : "";

            int deviceNumber = PromptMic(config.Audio.DeviceNumber);
            bool tcpEnabled = PromptTcp(config.TcpSource.Enabled, config.TcpSource.Host, config.TcpSource.Port);
            bool loopbackEnabled = PromptLoopback(config.Loopback.Enabled, config.Loopback.Label);

            return Persisted(config, configPath, type, whisperBuild, deviceNumber, tcpEnabled, loopbackEnabled);
        }

        private static string PromptEngine(bool hasSaved, EngineConfig saved, bool remoteLogEnabled)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Choose a transcription engine:");
            if (hasSaved)
            {
                Console.Error.WriteLine($"  1) Use last settings ({Describe(saved)})  (default)");
                Console.Error.WriteLine("  2) Parakeet   - local, CPU, offline (VAD-segmented)");
                Console.Error.WriteLine("  3) Whisper    - whisper.cpp server, GPU-accelerated");
                Console.Error.WriteLine("  4) Streaming  - local, CPU, word-by-word (online Zipformer)");
                Console.Error.WriteLine("  5) Compare    - run both, one tagged line per engine (needs the whisper server)");
                Console.Error.WriteLine($"  6) Remote Log - {RemoteLogSetup.Status(remoteLogEnabled)} (configure)");
                return Ask("Engine [1-6]: ",
                    new() { ["1"] = KeepLast, ["2"] = "parakeet", ["3"] = "whisper", ["4"] = "streaming", ["5"] = "compare", ["6"] = RemoteLog }, KeepLast);
            }

            Console.Error.WriteLine("  1) Parakeet   - local, CPU, offline (VAD-segmented)  (default)");
            Console.Error.WriteLine("  2) Whisper    - whisper.cpp server, GPU-accelerated");
            Console.Error.WriteLine("  3) Streaming  - local, CPU, word-by-word (online Zipformer)");
            Console.Error.WriteLine("  4) Compare    - run both, one tagged line per engine (needs the whisper server)");
            Console.Error.WriteLine($"  5) Remote Log - {RemoteLogSetup.Status(remoteLogEnabled)} (configure)");
            return Ask("Engine [1-5]: ",
                new() { ["1"] = "parakeet", ["2"] = "whisper", ["3"] = "streaming", ["4"] = "compare", ["5"] = RemoteLog }, "parakeet");
        }

        private static string PromptWhisperBuild(string? lastBuild)
        {
            // Default (Enter) keeps the previous build if there was one, else CPU.
            string fallback = string.IsNullOrWhiteSpace(lastBuild) ? "cpu" : lastBuild.Trim().ToLowerInvariant();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Which whisper build matches this PC's GPU? (default: {fallback})");
            Console.Error.WriteLine("  1) AMD     - Vulkan build (also works on any Vulkan GPU)");
            Console.Error.WriteLine("  2) NVIDIA  - CUDA build (needs a CUDA 12 runtime)");
            Console.Error.WriteLine("  3) CPU     - no GPU / other; slower but runs anywhere");
            return Ask("GPU [1-3]: ",
                new() { ["1"] = "amd", ["2"] = "nvidia", ["3"] = "cpu" }, fallback);
        }

        private static int PromptMic(int currentDevice)
        {
            int deviceCount = WaveInEvent.DeviceCount;
            if (deviceCount == 0)
            {
                Console.Error.WriteLine("No audio input devices found; keeping current device.");
                return currentDevice;
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("Choose microphone:");
            for (int i = 0; i < deviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                string tag = i == currentDevice ? "  (current, default)" : (i == 0 ? "  (default)" : "");
                Console.Error.WriteLine($"  {i + 1}) {caps.ProductName}{tag}");
            }

            Console.Error.Write($"Mic [1-{deviceCount}]: ");
            while (true)
            {
                string? line = Console.In.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line))
                    return currentDevice;
                if (int.TryParse(line, out int choice) && choice >= 1 && choice <= deviceCount)
                    return choice - 1;
                Console.Error.WriteLine($"  Please enter a number between 1 and {deviceCount}");
                Console.Error.Write($"Mic [1-{deviceCount}]: ");
            }
        }

        private static bool PromptTcp(bool currentEnabled, string host, int port)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Enable TCP audio source? ({host}:{port})");
            if (currentEnabled)
            {
                Console.Error.WriteLine("  1) Yes  (current, default)");
                Console.Error.WriteLine("  2) No");
            }
            else
            {
                Console.Error.WriteLine("  1) No   (current, default)");
                Console.Error.WriteLine("  2) Yes");
            }
            string result = Ask("TCP [1-2]: ",
                new() { ["1"] = currentEnabled ? "yes" : "no", ["2"] = currentEnabled ? "no" : "yes" },
                currentEnabled ? "yes" : "no");
            return result == "yes";
        }

        private static bool PromptLoopback(bool currentEnabled, string label)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Enable PC audio loopback? (captures system audio, tag '{label}')");
            if (currentEnabled)
            {
                Console.Error.WriteLine("  1) Yes  (current, default)");
                Console.Error.WriteLine("  2) No");
            }
            else
            {
                Console.Error.WriteLine("  1) No   (current, default)");
                Console.Error.WriteLine("  2) Yes");
            }
            string result = Ask("Loopback [1-2]: ",
                new() { ["1"] = currentEnabled ? "yes" : "no", ["2"] = currentEnabled ? "no" : "yes" },
                currentEnabled ? "yes" : "no");
            return result == "yes";
        }

        /// <summary>Human-readable summary of a saved engine choice for the menu.</summary>
        private static string Describe(EngineConfig e) =>
            e.NeedsWhisperServer
                ? $"{e.Type}" + (string.IsNullOrWhiteSpace(e.WhisperBuild) ? "" : $" / {e.WhisperBuild}")
                : e.Type;

        /// <summary>Read a line and map it to a choice; blank/invalid falls back to <paramref name="fallback"/>.</summary>
        private static string Ask(string prompt, Dictionary<string, string> choices, string fallback)
        {
            while (true)
            {
                Console.Error.Write(prompt);
                string? line = Console.In.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line))
                    return fallback;
                if (choices.TryGetValue(line, out string? choice))
                    return choice;
                Console.Error.WriteLine("  Please enter one of: " + string.Join(", ", choices.Keys));
            }
        }

        /// <summary>Apply the selection to the in-memory config and persist it to disk.</summary>
        private static AppConfig Persisted(AppConfig config, string? configPath, string type, string whisperBuild,
            int deviceNumber, bool tcpEnabled, bool loopbackEnabled)
        {
            Console.Error.WriteLine(type switch
            {
                "whisper" => $"Selected: whisper ({whisperBuild}).",
                "compare" => $"Selected: compare parakeet vs whisper ({whisperBuild}).",
                "streaming" => "Selected: streaming (online Zipformer).",
                _ => "Selected: parakeet.",
            });

            try
            {
                PersistToFile(ConfigLoader.Resolve(configPath), type, whisperBuild, deviceNumber, tcpEnabled, loopbackEnabled, config.RemoteLog);
            }
            catch (Exception ex)
            {
                // Non-fatal: we still run this session with the choice; it just won't be
                // remembered if we couldn't write the file.
                Console.Error.WriteLine($"Could not save settings to config: {ex.Message}");
            }

            return config with
            {
                Engine = config.Engine with { Type = type, WhisperBuild = whisperBuild },
                Audio = config.Audio with { DeviceNumber = deviceNumber },
                TcpSource = config.TcpSource with { Enabled = tcpEnabled },
                Loopback = config.Loopback with { Enabled = loopbackEnabled },
            };
        }

        /// <summary>
        /// Patch engine, mic, TCP, and loopback values in the raw config text, leaving all
        /// comments/formatting intact.
        /// </summary>
        private static void PersistToFile(string path, string type, string whisperBuild, int deviceNumber,
            bool tcpEnabled, bool loopbackEnabled, RemoteLogConfig remoteLog)
        {
            if (!File.Exists(path))
                return;

            string text = File.ReadAllText(path);

            text = ReplaceJsonString(text, "Type", type);

            if (Regex.IsMatch(text, "\"WhisperBuild\"\\s*:\\s*\"[^\"]*\""))
                text = ReplaceJsonString(text, "WhisperBuild", whisperBuild);
            else
                text = InsertAfterTypeLine(text, $"\"WhisperBuild\": \"{whisperBuild}\",");

            text = ReplaceJsonInt(text, "DeviceNumber", deviceNumber);
            text = ReplaceSectionBool(text, "TcpSource", "Enabled", tcpEnabled);
            text = EnsureLoopbackSection(text);
            text = ReplaceSectionBool(text, "Loopback", "Enabled", loopbackEnabled);
            text = EnsureRemoteLogSection(text);
            text = ReplaceSectionBool(text, "RemoteLog", "Enabled", remoteLog.Enabled);
            text = ReplaceSectionString(text, "RemoteLog", "Url", remoteLog.Url);

            File.WriteAllText(path, text);
        }

        /// <summary>Persist the RemoteLog fields — called immediately after the remote-log submenu.</summary>
        private static void PersistRemoteLog(string path, RemoteLogConfig remoteLog)
        {
            if (!File.Exists(path))
                return;
            string text = File.ReadAllText(path);
            text = EnsureRemoteLogSection(text);
            text = ReplaceSectionBool(text, "RemoteLog", "Enabled", remoteLog.Enabled);
            text = ReplaceSectionString(text, "RemoteLog", "Url", remoteLog.Url);
            File.WriteAllText(path, text);
        }

        /// <summary>If the config text has no "Loopback" section, insert one before "Engine".</summary>
        private static string EnsureLoopbackSection(string text)
        {
            if (Regex.IsMatch(text, "\"Loopback\"\\s*:"))
                return text;

            const string block = """

          // Optional WASAPI loopback: captures whatever Windows is playing (TV, YouTube, calls, etc).
          "Loopback": {
            "Enabled": false,
            "Label": "pc",
            "DeviceIndex": -1,
            "Vad": {
              "Model": "models/silero_vad.onnx",
              "Provider": "cpu",
              "Threshold": 0.3,
              "MinSilenceDuration": 0.4,
              "MinSpeechDuration": 0.1,
              "MaxSpeechDuration": 20.0,
              "PreRollDuration": 0.3,
              "BufferSizeInSeconds": 30.0,
              "WindowSize": 512,
              "NumThreads": 1
            }
          },

        """;

            var m = Regex.Match(text, "^([ \\t]*)\"Engine\"\\s*:", RegexOptions.Multiline);
            if (!m.Success) return text;
            return text[..m.Index] + block + text[m.Index..];
        }

        /// <summary>If the config text has no "RemoteLog" section, insert one before "Engine".</summary>
        private static string EnsureRemoteLogSection(string text)
        {
            if (Regex.IsMatch(text, "\"RemoteLog\"\\s*:"))
                return text;

            const string block = """

          // Remote transcription log - POST each utterance to a VPS endpoint over HTTPS.
          "RemoteLog": {
            "Enabled": false,
            "Url": ""
          },

        """;

            var m = Regex.Match(text, "^([ \\t]*)\"Engine\"\\s*:", RegexOptions.Multiline);
            if (!m.Success) return text;
            return text[..m.Index] + block + text[m.Index..];
        }

        /// <summary>Replace the first <c>"key": "..."</c> string value. No-op if the key is absent.</summary>
        private static string ReplaceJsonString(string text, string key, string value) =>
            new Regex($"(\"{Regex.Escape(key)}\"\\s*:\\s*\")[^\"]*(\")")
                .Replace(text, m => m.Groups[1].Value + value + m.Groups[2].Value, 1);

        /// <summary>Replace the first <c>"key": N</c> integer value.</summary>
        private static string ReplaceJsonInt(string text, string key, int value) =>
            new Regex($"(\"{Regex.Escape(key)}\"\\s*:\\s*)\\d+")
                .Replace(text, m => m.Groups[1].Value + value, 1);

        /// <summary>Replace the first <c>"key": true/false</c> boolean within the named JSON section.</summary>
        private static string ReplaceSectionBool(string text, string section, string key, bool value)
        {
            var sectionMatch = Regex.Match(text, $"\"{Regex.Escape(section)}\"\\s*:\\s*\\{{");
            if (!sectionMatch.Success) return text;

            int contentStart = sectionMatch.Index + sectionMatch.Length;
            int depth = 1, pos = contentStart;
            while (pos < text.Length && depth > 0)
            {
                if (text[pos] == '{') depth++;
                else if (text[pos] == '}') depth--;
                if (depth > 0) pos++;
            }

            string before = text[..contentStart];
            string inner = text[contentStart..pos];
            string after = text[pos..];

            string patched = new Regex($"(\"{Regex.Escape(key)}\"\\s*:\\s*)(?:true|false)")
                .Replace(inner, m => m.Groups[1].Value + (value ? "true" : "false"), 1);

            if (patched == inner)
                patched = InsertSectionBool(inner, key, value);

            return before + patched + after;
        }

        /// <summary>Replace or insert a string field within the named JSON section.</summary>
        private static string ReplaceSectionString(string text, string section, string key, string value)
        {
            var sectionMatch = Regex.Match(text, $"\"{Regex.Escape(section)}\"\\s*:\\s*\\{{");
            if (!sectionMatch.Success) return text;

            int contentStart = sectionMatch.Index + sectionMatch.Length;
            int depth = 1, pos = contentStart;
            while (pos < text.Length && depth > 0)
            {
                if (text[pos] == '{') depth++;
                else if (text[pos] == '}') depth--;
                if (depth > 0) pos++;
            }

            string before = text[..contentStart];
            string inner = text[contentStart..pos];
            string after = text[pos..];
            string literal = JsonSerializer.Serialize(value);

            string patched = new Regex($"(\"{Regex.Escape(key)}\"\\s*:\\s*)\"(?:\\\\.|[^\"\\\\])*\"")
                .Replace(inner, m => m.Groups[1].Value + literal, 1);

            if (patched == inner)
                patched = InsertSectionProperty(inner, key, literal);

            return before + patched + after;
        }

        /// <summary>Insert a missing boolean field at the top of a JSON section body.</summary>
        private static string InsertSectionBool(string inner, string key, bool value)
        {
            return InsertSectionProperty(inner, key, value ? "true" : "false");
        }

        /// <summary>Insert a missing field at the top of a JSON section body.</summary>
        private static string InsertSectionProperty(string inner, string key, string literalValue)
        {
            string lineEnding = inner.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var firstContent = Regex.Match(inner, @"\S");
            string indent = firstContent.Success
                ? Regex.Match(inner[..firstContent.Index], @"[ \t]*$").Value
                : "    ";
            string separator = HasSectionContent(inner) ? "," : "";
            return lineEnding + indent + $"\"{key}\": {literalValue}{separator}" + inner;
        }

        private static bool HasSectionContent(string inner) => Regex.IsMatch(inner, @"\S");

        /// <summary>Insert <paramref name="lineToInsert"/> on its own line right after the "Type" line.</summary>
        private static string InsertAfterTypeLine(string text, string lineToInsert)
        {
            var m = Regex.Match(text, "^(?<indent>[ \\t]*)\"Type\"\\s*:.*$", RegexOptions.Multiline);
            if (!m.Success)
                return text;   // nothing to anchor to; leave Type-only (WhisperBuild stays from defaults)

            string indent = m.Groups["indent"].Value;
            return text[..m.Index] + m.Value + Environment.NewLine + indent + lineToInsert + text[(m.Index + m.Length)..];
        }
    }
}
