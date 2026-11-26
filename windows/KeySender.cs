using System.Runtime.InteropServices;

namespace PptRemoteServer;

/// <summary>Turns protocol commands into keypresses using SendInput.</summary>
internal static class KeySender
{
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_PRIOR = 0x21;  // Page Up
    private const ushort VK_NEXT = 0x22;   // Page Down
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_B = 0x42;
    private const ushort VK_F5 = 0x74;

    /// <summary>Sends the key for a command. Returns a label for the log, or null if the command isn't a key action.</summary>
    public static string? Execute(byte command, KeyScheme scheme)
    {
        bool pages = scheme == KeyScheme.PageKeys;
        bool ok;

        switch (command)
        {
            case Protocol.Next:
                ok = Tap(pages ? VK_NEXT : VK_RIGHT, extended: true);
                break;
            case Protocol.Prev:
                ok = Tap(pages ? VK_PRIOR : VK_LEFT, extended: true);
                break;
            case Protocol.Start:
                ok = Tap(VK_F5);
                break;
            case Protocol.End:
                ok = Tap(VK_ESCAPE);
                break;
            case Protocol.Black:
                ok = Tap(VK_B);
                break;
            default:
                return null;
        }

        var label = Protocol.Describe(command);
        return ok ? label : $"{label} (key send failed)";
    }

    private static bool Tap(ushort vk, bool extended = false)
    {
        uint ext = extended ? KEYEVENTF_EXTENDEDKEY : 0u;
        var inputs = new[] { Key(vk, ext), Key(vk, ext | KEYEVENTF_KEYUP) };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static INPUT Key(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };

    // ---- Win32 interop ----
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
