using System.Reflection;
using System.Runtime.Loader;
using ObsTurboMuteAlarm.Warning;

namespace ObsTurboMuteAlarm;

/// <summary>Implemented by the optional ICP plugin (ObsTurboMuteAlarm.Icp.dll).</summary>
internal interface IIcpPlugin
{
    /// <summary>True if a WinWing ViperAce ICP is plugged in.</summary>
    bool IsDevicePresent();

    /// <summary>Opens the ICP connection; it reconnects automatically. Dispose blanks the DED.</summary>
    IIcpDisplay OpenDisplay();

    /// <summary>The big "MUTE" frame as ASCII art (for --preview-ded).</summary>
    string PreviewAscii();
}

internal interface IIcpDisplay : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Raised (on a thread-pool thread) when the ICP connects or disconnects.</summary>
    event Action<bool>? ConnectionChanged;

    IWarningOutput CreateOutput(byte warningBrightness, byte? brightnessAfter);
}

/// <summary>Finds and loads the ICP plugin from the app folder, if it was installed.</summary>
internal static class IcpSupport
{
    public const string PluginFile = "ObsTurboMuteAlarm.Icp.dll";

    private static readonly Lazy<IIcpPlugin?> LazyPlugin = new(Load);

    /// <summary>The ICP plugin, or null when ICP support is not installed.</summary>
    public static IIcpPlugin? Plugin => LazyPlugin.Value;

    public static bool IsInstalled => Plugin != null;

    private static IIcpPlugin? Load()
    {
        string dir = AppContext.BaseDirectory;
        string path = Path.Combine(dir, PluginFile);
        if (!File.Exists(path)) return null;
        try
        {
            // The plugin's own dependencies (HidSharp.dll) sit next to it.
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                string dep = Path.Combine(dir, name.Name + ".dll");
                return File.Exists(dep) ? ctx.LoadFromAssemblyPath(dep) : null;
            };
            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            var type = asm.GetTypes().First(t => typeof(IIcpPlugin).IsAssignableFrom(t) && !t.IsAbstract);
            return (IIcpPlugin)Activator.CreateInstance(type, nonPublic: true)!;
        }
        catch (Exception ex)
        {
            Log.Info($"ICP support could not be loaded: {(ex is ReflectionTypeLoadException r ? r.LoaderExceptions.FirstOrDefault()?.Message : ex.Message)}");
            return null;
        }
    }
}
