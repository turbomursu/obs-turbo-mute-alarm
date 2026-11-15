using System.Runtime.InteropServices;
using System.Text.Json;
using ObsTurboMuteAlarm.Icp;
using ObsTurboMuteAlarm.Obs;

namespace ObsTurboMuteAlarm;

internal static class Program
{
    private const string AppName = "OBS Turbo Mute Alarm";

    [STAThread]
    private static int Main(string[] args)
    {
        bool Flag(string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);

        if (Flag("--preview-ded"))
        {
            AttachConsole(AttachParentProcess);
            Console.Write(DedRenderer.ToAscii(DedRenderer.BigMute(inverted: false)));
            return 0;
        }

        string path = AppSettings.DefaultPath;
        AppSettings settings;
        bool existed;
        try
        {
            settings = AppSettings.Load(path, out existed);
        }
        catch (JsonException ex)
        {
            Fail($"Invalid settings file {path}:\n{ex.Message}");
            return 1;
        }

        if (Flag("--check-obs"))
            return CheckObs(settings);

        using var mutex = new Mutex(true, @"Local\ObsTurboMuteAlarm", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show($"{AppName} is already running (see the system tray).", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        // A background warning tool must not stop on a modal crash dialog; log the error and keep running.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Info($"Unhandled UI error: {e.Exception}");
        Log.Info("Started.");
        var app = new TrayApp(settings, path, firstRun: !existed, showTestWarning: Flag("--test"));
        if (Flag("--help")) UI.HelpForm.ShowHelp();
        Application.Run(app);
        return 0;
    }

    /// <summary>Connects to OBS once, prints the audio sources and their state, and exits. Never touches the ICP.</summary>
    private static int CheckObs(AppSettings settings)
    {
        AttachConsole(AttachParentProcess);
        try
        {
            var inputs = ObsMuteMonitor.FetchAudioInputsAsync(settings.Obs, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Console.WriteLine($"Connected to OBS at {settings.Obs.Host}:{settings.Obs.Port}. Audio sources (* = watched):");
            foreach (var i in inputs)
            {
                bool watched = settings.Sources.Any(s =>
                    (s.Uuid.Length > 0 && string.Equals(s.Uuid, i.Uuid, StringComparison.OrdinalIgnoreCase)) ||
                    (s.Uuid.Length == 0 && s.Name == i.Name));
                Console.WriteLine($"{(watched ? "*" : " ")} {(i.Muted ? "MUTED" : "live "),-5}  {i.Name}  [{i.Kind}] {i.Uuid}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not connect: {(ex is TimeoutException ? "timed out" : ex.Message)}");
            return 1;
        }
    }

    private static void Fail(string message)
    {
        Log.Info(message);
        MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
