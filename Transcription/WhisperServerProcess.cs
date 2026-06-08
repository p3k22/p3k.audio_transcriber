using System;
using System.Diagnostics;
using System.Threading;
using p3k.audio_transcriber.Configs;

namespace p3k.audio_transcriber.Transcription
{
    /// <summary>
    /// A whisper.cpp server that this process spawned and owns. It is assigned to a
    /// <see cref="JobObject"/> so it is killed if the transcriber goes away, and it is
    /// killed explicitly on <see cref="Dispose"/>. Only created when no server is
    /// already answering and one was successfully provisioned (see WhisperProvisioner).
    /// </summary>
    internal sealed class WhisperServerProcess : IDisposable
    {
        private readonly Process _proc;
        private readonly JobObject _job;

        private WhisperServerProcess(Process proc, JobObject job)
        {
            _proc = proc;
            _job = job;
        }

        /// <summary>
        /// Launch the server and block until it answers (or the timeout elapses).
        /// Returns null if it could not be started or never became ready.
        /// </summary>
        public static WhisperServerProcess? StartAndWait(EngineConfig cfg)
        {
            var uri = new Uri(cfg.WhisperServerUrl);

            var psi = new ProcessStartInfo
            {
                FileName = cfg.ServerExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(cfg.ServerModelPath);
            psi.ArgumentList.Add("--host"); psi.ArgumentList.Add(uri.Host);
            psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(uri.Port.ToString());
            psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(cfg.ServerThreads.ToString());
            psi.ArgumentList.Add("-l"); psi.ArgumentList.Add("en");

            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to launch whisper-server: {ex.Message}");
                return null;
            }
            if (proc is null)
            {
                Console.Error.WriteLine("Failed to launch whisper-server (no process).");
                return null;
            }

            // Tie the server's lifetime to ours, then surface its logs on our stderr
            // (prefixed) so a failed model load etc. is visible.
            var job = new JobObject();
            try
            {
                job.AssignProcess(proc);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not job-assign whisper-server: {ex.Message}");
                try { proc.Kill(true); } catch { /* ignore */ }
                proc.Dispose();
                job.Dispose();
                return null;
            }

            proc.OutputDataReceived += (_, e) => { if (IsNoteworthy(e.Data)) Console.Error.WriteLine($"[whisper-server] {e.Data}"); };
            proc.ErrorDataReceived += (_, e) => { if (IsNoteworthy(e.Data)) Console.Error.WriteLine($"[whisper-server] {e.Data}"); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Poll until it answers or we give up / it dies.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(cfg.ServerStartTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (proc.HasExited)
                {
                    Console.Error.WriteLine($"whisper-server exited early (code {proc.ExitCode}).");
                    proc.Dispose();
                    job.Dispose();
                    return null;
                }
                if (WhisperServerTranscriber.IsReachable(cfg.WhisperServerUrl))
                    return new WhisperServerProcess(proc, job);

                Thread.Sleep(500);
            }

            Console.Error.WriteLine(
                $"whisper-server did not become ready within {cfg.ServerStartTimeoutSeconds}s; giving up.");
            try { proc.Kill(true); } catch { /* ignore */ }
            proc.Dispose();
            job.Dispose();
            return null;
        }

        /// <summary>
        /// whisper-server is very chatty at startup and per request (the model-load
        /// banner, init-state buffer sizes, the <c>system_info</c> line, the per-segment
        /// "processing 'segment.wav'" / timings lines). None of it is useful here -
        /// readiness is detected by polling and transcripts arrive over HTTP - so we
        /// forward only lines that look like a genuine problem and swallow everything else.
        /// </summary>
        private static bool IsNoteworthy(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return line.Contains("error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || line.Contains("warn", StringComparison.OrdinalIgnoreCase)
                || line.Contains("abort", StringComparison.OrdinalIgnoreCase)
                || line.Contains("exception", StringComparison.OrdinalIgnoreCase)
                || line.Contains("unable", StringComparison.OrdinalIgnoreCase)
                || line.Contains("could not", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            try
            {
                if (!_proc.HasExited)
                    _proc.Kill(true);
            }
            catch { /* already gone */ }

            _proc.Dispose();
            _job.Dispose();   // also enforces the kill if the above missed
        }
    }
}
