using System.Runtime.InteropServices;

namespace RevitAgent.Cli;

/// <summary>
/// Enables Windows Virtual Terminal processing so ANSI escape sequences (used for gray
/// "process" text) are interpreted by the console. Legacy conhost does not honor ANSI by
/// default — without this, escape bytes would litter the output. No-op when output is
/// redirected (a redirected handle is not a console screen buffer) or on non-Windows.
/// Call <see cref="EnsureEnabled"/> once before writing any ANSI; rendering falls back to
/// plain text if VT could not be enabled.
/// </summary>
internal static class ConsoleAnsi
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    private static readonly object s_gate = new();
    private static bool _tried;

    /// <summary>True when ANSI escapes will be interpreted (a real Windows console with VT enabled).</summary>
    public static bool Enabled { get; private set; }

    public static void EnsureEnabled()
    {
        lock (s_gate)
        {
            if (_tried) return;
            _tried = true;
            if (Console.IsOutputRedirected || !OperatingSystem.IsWindows())
            {
                Enabled = false;
                return;
            }
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle != IntPtr.Zero && GetConsoleMode(handle, out uint mode) &&
                    SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING))
                {
                    Enabled = true;
                }
            }
            catch
            {
                Enabled = false; // best-effort; rendering falls back to plain text
            }
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
