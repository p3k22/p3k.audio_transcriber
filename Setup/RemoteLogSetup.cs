using p3k.audio_transcriber.Configs;

namespace p3k.audio_transcriber.Setup
{
    /// <summary>
    /// Startup management of the remote-log Bearer token. The token is a secret, so it
    /// lives in a per-user environment variable (<see cref="EnvVar"/>) instead of in
    /// config.json. This prompt lets an interactive launch enable logging (pasting a
    /// token the first time), rotate the stored token, or disable it again.
    ///
    /// Headless launches (stdin redirected — a managed child process) skip the prompt
    /// and just use whatever token the environment already holds.
    /// </summary>
    internal static class RemoteLogSetup
    {
        /// <summary>Per-user environment variable that holds the Bearer token.</summary>
        public const string EnvVar = "P3K_REMOTE_LOG_TOKEN";

        /// <summary>The current token from the environment (empty if unset).</summary>
        public static string GetToken() =>
            Environment.GetEnvironmentVariable(EnvVar) ?? "";

        /// <summary>Menu status word: "On" / "Off".</summary>
        public static string Status(bool enabled) => enabled ? "On" : "Off";

        /// <summary>
        /// Interactive enable/disable / set URL / set token / erase token prompt. No-op when headless.
        /// Returns the (possibly updated) config — caller must persist it.
        /// </summary>
        public static RemoteLogConfig Prompt(RemoteLogConfig config)
        {
            if (Console.IsInputRedirected)
                return config;

            Console.Error.WriteLine();
            if (string.IsNullOrWhiteSpace(config.Url))
                Console.Error.WriteLine("Remote transcription log: no RemoteLog.Url in config.json - logging stays off until one is set.");
            else
                Console.Error.WriteLine($"Remote transcription log -> {config.Url}");

            bool hasToken = !string.IsNullOrWhiteSpace(GetToken());
            Console.Error.WriteLine($"  Logging: {Status(config.Enabled)}   Token: {(hasToken ? "set" : "none")}");
            Console.Error.WriteLine("  1) Enable / Disable");
            Console.Error.WriteLine("  2) Set URL");
            Console.Error.WriteLine("  3) Set Token");
            Console.Error.WriteLine("  4) Erase Token");
            Console.Error.WriteLine("  5) Go back");
            switch (Ask("Remote log [1-5]: ", "1", "2", "3", "4", "5"))
            {
                case "1":
                    bool nowEnabled = !config.Enabled;
                    Console.Error.WriteLine($"  Remote logging {(nowEnabled ? "enabled" : "disabled")}.");
                    return config with { Enabled = nowEnabled };
                case "2":
                    string? updatedUrl = PromptForUrl(config.Url);
                    return updatedUrl is null ? config : config with { Url = updatedUrl };
                case "3":
                    string? token = PromptForToken();
                    if (token is null)
                        return config;
                    SaveToken(token);
                    return config with { Enabled = true };
                case "4":
                    ClearToken();
                    return config with { Enabled = false };
                // 5 = go back, fall through
            }
            return config;
        }

        /// <summary>Edit the URL. Enter applies the edit; Escape cancels.</summary>
        private static string? PromptForUrl(string currentUrl)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Edit RemoteLog.Url:");
            Console.Error.WriteLine("  Enter  - confirm");
            Console.Error.WriteLine("  Escape - cancel");
            string? editedUrl = ReadEditableLine("URL: ", currentUrl, maskInput: false);
            if (editedUrl is null)
            {
                Console.Error.WriteLine("  URL unchanged.");
                return null;
            }

            string url = editedUrl.Trim();
            Console.Error.WriteLine(string.IsNullOrWhiteSpace(url)
                ? "  Cleared RemoteLog.Url."
                : $"  RemoteLog.Url set to {url}.");
            return url;
        }

        /// <summary>Read one editable line, prefilled with the current value. Null means canceled.</summary>
        private static string? ReadEditableLine(string prompt, string initial, bool maskInput)
        {
            var buffer = new List<char>(initial);
            int cursor = buffer.Count;
            int lastLength = buffer.Count;
            Console.Error.Write(prompt);
            int inputLeft = Console.CursorLeft;
            int inputTop = Console.CursorTop;

            void Redraw()
            {
                Console.SetCursorPosition(inputLeft, inputTop);
                string displayText = maskInput ? new string('*', buffer.Count) : new string(buffer.ToArray());
                Console.Error.Write(displayText + new string(' ', Math.Max(1, lastLength - displayText.Length)));
                lastLength = displayText.Length;
                Console.SetCursorPosition(inputLeft + cursor, inputTop);
            }

            Redraw();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        Console.Error.WriteLine();
                        return new string(buffer.ToArray());
                    case ConsoleKey.Escape:
                        Console.Error.WriteLine();
                        return null;
                    case ConsoleKey.LeftArrow when cursor > 0:
                        cursor--;
                        break;
                    case ConsoleKey.RightArrow when cursor < buffer.Count:
                        cursor++;
                        break;
                    case ConsoleKey.Home:
                        cursor = 0;
                        break;
                    case ConsoleKey.End:
                        cursor = buffer.Count;
                        break;
                    case ConsoleKey.Backspace when cursor > 0:
                        buffer.RemoveAt(--cursor);
                        break;
                    case ConsoleKey.Delete when cursor < buffer.Count:
                        buffer.RemoveAt(cursor);
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            buffer.Insert(cursor, key.KeyChar);
                            cursor++;
                        }
                        break;
                }
                Redraw();
            }
        }

        /// <summary>Read a non-blank token from the console. Null means canceled.</summary>
        private static string? PromptForToken()
        {
            while (true)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Set remote-log token:");
                Console.Error.WriteLine("  Enter  - confirm");
                Console.Error.WriteLine("  Escape - cancel");
                string? editedToken = ReadEditableLine("Token: ", "", maskInput: true);
                if (editedToken is null)
                {
                    Console.Error.WriteLine("  Token unchanged.");
                    return null;
                }

                string token = editedToken.Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    return token;
                Console.Error.WriteLine("  Token can't be blank.");
            }
        }

        /// <summary>Persist the token to the per-user environment and this process.</summary>
        private static void SaveToken(string token)
        {
            // Always set the process var so this run uses it immediately; the User
            // target is what persists for future launches.
            Environment.SetEnvironmentVariable(EnvVar, token, EnvironmentVariableTarget.Process);
            try
            {
                Environment.SetEnvironmentVariable(EnvVar, token, EnvironmentVariableTarget.User);
                Console.Error.WriteLine($"  Saved token to %{EnvVar}% (per-user). Remote logging enabled.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Could not persist to the user environment ({ex.Message}); using the token for this session only.");
            }
        }

        /// <summary>Clear the token from the per-user environment and this process.</summary>
        private static void ClearToken()
        {
            Environment.SetEnvironmentVariable(EnvVar, null, EnvironmentVariableTarget.Process);
            try
            {
                Environment.SetEnvironmentVariable(EnvVar, null, EnvironmentVariableTarget.User);
                Console.Error.WriteLine($"  Cleared %{EnvVar}%. Remote logging disabled.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Could not clear the user environment var ({ex.Message}); cleared for this session only.");
            }
        }

        /// <summary>Prompt until the input matches one of <paramref name="valid"/>; blank = first option.</summary>
        private static string Ask(string prompt, params string[] valid)
        {
            while (true)
            {
                Console.Error.Write(prompt);
                string? line = Console.In.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line))
                    return valid[0];
                if (Array.IndexOf(valid, line) >= 0)
                    return line;
                Console.Error.WriteLine("  Please enter one of: " + string.Join(", ", valid));
            }
        }
    }
}
