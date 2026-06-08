using System.IO.Pipes;
using System.Text;
using p3k.audio_transcriber.Runtime;

// ---------------------------------------------------------------------------
// Live microphone transcriber: Parakeet (sherpa-onnx, offline) + Silero VAD.
//
// Designed to run as a managed child process:
//   * stdout  = transcriptions only (one line per utterance)
//   * stderr  = status/diagnostics, incl. a "READY" line once listening
//   * config  = config.json next to the exe (or an explicit path via arg[0])
//   * stop    = close stdin, send "quit" on stdin, or Ctrl+C (Kill() is also safe)
//
// Launch silently from a host with: UseShellExecute=false, CreateNoWindow=true,
// RedirectStandardOutput=true, RedirectStandardError=true (and RedirectStandardInput
// =true if you want graceful "quit"/close shutdown).
// ---------------------------------------------------------------------------

// --display <pipeName> <title>: child-window mode — read from named pipe, render to own console.
if (args.Length >= 2 && args[0] == "--display")
{
    RunDisplayWindow(pipeName: args[1], title: args.Length >= 3 ? args[2] : args[1]);
    return 0;
}

static void RunDisplayWindow(string pipeName, string title)
{
    Console.Title = title;
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
    try { pipe.Connect(10000); } catch { return; }
    using var reader = new StreamReader(pipe, new UTF8Encoding(false));

    int partialRow = -1;
    int partialLen = 0;

    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (line.Length < 1) continue;
        bool isPartial = line[0] == '~';
        string text = line[1..];

        if (isPartial)
        {
            if (partialRow >= 0)
            {
                try { Console.SetCursorPosition(0, partialRow); }
                catch { partialRow = -1; }
            }
            if (partialRow < 0)
            {
                try { partialRow = Console.CursorTop; } catch { }
            }
            int pad = Math.Max(0, partialLen - text.Length);
            Console.Write(text + new string(' ', pad));
            partialLen = text.Length;
        }
        else
        {
            if (partialRow >= 0)
            {
                try { Console.SetCursorPosition(0, partialRow); } catch { }
                int pad = Math.Max(0, partialLen - text.Length);
                Console.WriteLine(text + new string(' ', pad));
            }
            else
            {
                Console.WriteLine(text);
            }
            partialRow = -1;
            partialLen = 0;
        }
    }
}

// Args: an optional config-path (first non-flag arg) overrides the default config.json,
// and --fp32 forces the higher-accuracy fp32 model (default is int8).
bool forceFp32 = args.Any(a => a.Equals("--fp32", StringComparison.OrdinalIgnoreCase));
string? configPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

using var system = new TranscriptionSystem(configPath, forceFp32);

if (!system.Start())
{
    Console.Error.WriteLine("Failed to start - see errors above. Fix the config and retry.");
    return 1;
}

// Cap native memory growth (sherpa-onnx / ONNX Runtime leak below the managed layer):
// recycle the pipeline when committed memory crosses the budget, and log managed-vs-native
// memory periodically so the leak is observable.
var watchdog = new MemoryWatchdog(system, system.Watchdog);
watchdog.Start();

var quit = new ManualResetEventSlim(false);

// Ctrl+C -> graceful shutdown (when a console is attached).
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    quit.Set();
};

// Window close (X button) fires ProcessExit without going through CancelKeyPress.
// Explicitly stop so ONNX thread pools are signalled before the process dies.
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    watchdog.Dispose();
    system.Stop();
};

// Host-driven shutdown: only when stdin is a redirected pipe, so an interactive
// run (no piped input) doesn't see instant EOF. The host stops us by writing
// "quit" or closing our stdin.
if (Console.IsInputRedirected)
{
    var stdinWatcher = new Thread(() =>
    {
        try
        {
            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                if (line.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }
        catch { /* stdin unavailable */ }
        quit.Set();   // "quit" received or stdin closed (EOF)
    })
    { IsBackground = true, Name = "stdin-watcher" };
    stdinWatcher.Start();
}

quit.Wait();

Console.Error.WriteLine("Stopping ...");
watchdog.Dispose();   // stop recycling before we tear the pipeline down
system.Stop();
Console.Error.WriteLine("Stopped.");
return 0;
