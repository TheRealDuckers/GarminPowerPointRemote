using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace PptRemoteServer;

internal enum KeyScheme
{
    Arrows = 0,
    PageKeys = 1
}

/// <summary>PIN generation and formatting.</summary>
internal static class PinCode
{
    public const int MinLength = 6;
    public const int MaxLength = 12;

    public static string Generate(int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }
        return new string(chars);
    }

    public static bool IsValid(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    /// <summary>Groups digits in threes ("482  913"), optionally hiding them.</summary>
    public static string Format(string pin, bool masked)
    {
        var shown = masked ? new string('\u2022', pin.Length) : pin;
        var parts = new List<string>();
        for (int i = 0; i < shown.Length; i += 3)
        {
            parts.Add(shown.Substring(i, Math.Min(3, shown.Length - i)));
        }
        return string.Join("  ", parts);
    }
}

/// <summary>Tiny settings file in %AppData%.</summary>
internal sealed class Settings
{
    private static readonly object SaveLock = new();

    public KeyScheme KeyScheme { get; set; } = KeyScheme.Arrows;
    public string Pin { get; set; } = "";
    public uint LastCounter { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GarminPowerPointRemote", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
        }
        catch
        {
            // Corrupt or unreadable: fall back to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        lock (SaveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch
            {
                // Not worth interrupting a talk over.
            }
        }
    }
}

/// <summary>"Start with Windows" via the per-user Run key.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GarminPowerPointRemote";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry locked down by policy: ignore.
        }
    }
}
