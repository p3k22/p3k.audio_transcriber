using System.Runtime.InteropServices;

namespace p3k.audio_transcriber.Display
{
    /// <summary>
    /// Multi-source terminal display.
    ///
    /// Active partials are pinned to the bottom of the terminal. Finals are written
    /// via Console.WriteLine so they scroll naturally into the terminal's history buffer.
    ///
    /// Before each write the reserved partial area is erased by moving the cursor up
    /// by _reservedLines and clearing to end of screen. Partials are then redrawn
    /// row-by-row with auto-wrap disabled so exact-width chunks don't double-advance
    /// the cursor. _reservedLines stays accurate and the invariant holds: cursor is
    /// always at the end of the partial area after every operation.
    ///
    /// Falls back to plain Console.WriteLine when VT processing is unavailable.
    /// </summary>
    internal sealed class SharedConsole
    {
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr h, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr h, uint mode);
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);

        private readonly Lock _lock = new();
        private readonly List<string> _order = new();                // label display order in partial area
        private readonly Dictionary<string, string> _texts = new(); // label -> current partial line
        private readonly bool _vt;
        private int _reservedLines;  // rows currently occupied by the partial area

        public SharedConsole()
        {
            if (Console.IsOutputRedirected) { _vt = false; return; }
            _vt = TryEnableVt();
        }

        private static bool TryEnableVt()
        {
            try
            {
                var h = GetStdHandle(-11); // STD_OUTPUT_HANDLE
                if (!GetConsoleMode(h, out uint mode)) return false;
                return SetConsoleMode(h, mode | 0x0004u); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
            }
            catch { return false; }
        }

        private static void W(string s) => Console.Write(s);

        private int CalcRows(string text)
        {
            int w = Console.WindowWidth;
            if (w <= 0 || text.Length == 0) return 1;
            return (text.Length + w - 1) / w;
        }

        public void ClearScreen()
        {
            lock (_lock)
            {
                if (Console.IsOutputRedirected) return;
                if (_vt) W("\x1b[2J\x1b[H");
                else Console.Clear();
                _reservedLines = 0;
                _order.Clear();
                _texts.Clear();
            }
        }

        // Move up to the start of the partial area and erase to end of screen.
        private void ClearReserved()
        {
            if (_reservedLines > 0) W($"\x1b[{_reservedLines}A");
            W("\x1b[0J");
        }

        // Redraw all active partials row-by-row. Cursor ends up below the last row.
        // Auto-wrap is disabled so a chunk that exactly fills the terminal width
        // doesn't cause an extra implicit newline before our explicit \r\n.
        private void DrawPartials()
        {
            int w = Console.WindowWidth;
            int total = 0;
            W("\x1b[?7l"); // disable auto-wrap
            foreach (var lbl in _order)
            {
                string text = _texts.GetValueOrDefault(lbl, "");
                int rows = CalcRows(text);
                for (int r = 0; r < rows; r++)
                {
                    int start = r * w;
                    string chunk = start < text.Length
                        ? text.Substring(start, Math.Min(w, text.Length - start))
                        : "";
                    Console.Write(chunk);
                    W("\x1b[0K\r\n"); // clear rest of line, then advance row
                }
                total += rows;
            }
            W("\x1b[?7h"); // re-enable auto-wrap
            _reservedLines = total;
        }

        public void UpdatePartial(string label, string line)
        {
            lock (_lock)
            {
                if (!_vt) return;
                bool isNew = !_texts.ContainsKey(label);
                _texts[label] = line;
                if (isNew) _order.Add(label);
                ClearReserved();
                DrawPartials();
            }
        }

        public void CommitFinal(string label, string line)
        {
            lock (_lock)
            {
                if (Console.IsOutputRedirected) { Console.WriteLine(line); return; }
                _texts.Remove(label);
                _order.Remove(label);
                if (!_vt) { Console.WriteLine(line); return; }
                ClearReserved();
                Console.WriteLine(line); // natural scroll into terminal history
                DrawPartials();
            }
        }
    }
}
