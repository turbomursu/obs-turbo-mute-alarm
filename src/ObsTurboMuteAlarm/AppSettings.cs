using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ObsTurboMuteAlarm;

internal enum PopupPosition { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

internal enum PopupSize { Small, Medium, Large }

/// <summary>User settings, edited in the Settings window and stored as JSON in %APPDATA%\ObsTurboMuteAlarm.</summary>
internal sealed class AppSettings
{
    public ObsConfig Obs { get; set; } = new();

    /// <summary>OBS audio inputs to watch. The warning shows if any of them is muted.</summary>
    public List<WatchedSource> Sources { get; set; } = new();

    /// <summary>Monitors (Screen.DeviceName, e.g. \\.\DISPLAY1) that show the popup warning.</summary>
    public List<string> Monitors { get; set; } = new();

    /// <summary>Show the warning on a WinWing ViperAce ICP DED display.</summary>
    public bool IcpEnabled { get; set; }

    /// <summary>How long each phase of the flashing warning lasts.</summary>
    public int FlashIntervalMs { get; set; } = 400;

    public string PopupText { get; set; } = "MUTED";
    public PopupPosition PopupPosition { get; set; } = PopupPosition.TopCenter;
    public PopupSize PopupSize { get; set; } = PopupSize.Medium;
    public int PopupOpacityPercent { get; set; } = 90;

    /// <summary>Mouse clicks go through the popup to the window underneath.</summary>
    public bool PopupClickThrough { get; set; } = true;

    /// <summary>DED screen backlight (0-255) while the warning is shown on the ICP.</summary>
    public byte IcpWarningBrightness { get; set; } = 255;

    /// <summary>DED backlight (0-255) to set when the warning ends; null leaves it at the warning level.</summary>
    public byte? IcpBrightnessAfterWarning { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ObsTurboMuteAlarm", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads settings; returns defaults (and false) if the file doesn't exist yet.</summary>
    public static AppSettings Load(string path, out bool existed)
    {
        existed = File.Exists(path);
        var s = existed
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings()
            : new AppSettings();
        s.Normalize();
        return s;
    }

    public void Save(string path)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    private void Normalize()
    {
        Obs ??= new ObsConfig();
        Sources ??= new();
        Monitors ??= new();
        Sources.RemoveAll(s => s == null || (string.IsNullOrWhiteSpace(s.Name) && string.IsNullOrWhiteSpace(s.Uuid)));
        Monitors.RemoveAll(string.IsNullOrWhiteSpace);
        FlashIntervalMs = Math.Clamp(FlashIntervalMs, 100, 5000);
        PopupOpacityPercent = Math.Clamp(PopupOpacityPercent, 20, 100);
        if (string.IsNullOrWhiteSpace(PopupText)) PopupText = "MUTED";
    }
}

internal sealed class ObsConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4455;

    /// <summary>
    /// obs-websocket password, encrypted for the current Windows user (DPAPI) in the settings file.
    /// Empty = read it from OBS's own obs-websocket config on this PC.
    /// </summary>
    [JsonIgnore]
    public string Password
    {
        get => Unprotect(ProtectedPassword);
        set => ProtectedPassword = Protect(value);
    }

    /// <summary>DPAPI-protected password as stored in the settings file.</summary>
    public string ProtectedPassword { get; set; } = "";

    public string ResolvePassword() =>
        Password is { Length: > 0 } pw ? pw : ReadObsWebSocketPassword(ObsWebSocketConfigPath) ?? "";

    public static string ObsWebSocketConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "obs-studio", "plugin_config", "obs-websocket", "config.json");

    internal static string? ReadObsWebSocketPassword(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("server_password", out var pw) && pw.ValueKind == JsonValueKind.String
                ? pw.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Protect(string? plain) =>
        string.IsNullOrEmpty(plain)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return ""; // settings copied from another user/PC
        }
    }
}

internal sealed class WatchedSource
{
    /// <summary>OBS input name (used if the UUID is empty or no longer exists).</summary>
    public string Name { get; set; } = "";

    /// <summary>OBS input UUID (preferred: survives renames).</summary>
    public string Uuid { get; set; } = "";

    public override string ToString() => string.IsNullOrEmpty(Name) ? Uuid : Name;
}
