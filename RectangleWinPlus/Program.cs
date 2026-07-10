namespace RectangleWinPlus;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One hook, one tray icon. A second instance would fight the first for every keystroke.
        using var single = new Mutex(initiallyOwned: true, @"Local\RectangleWinPlus.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("RectangleWinPlus is already running — look for it in the system tray.",
                "RectangleWinPlus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        using var app = new TrayApp();
        Application.Run(app);

        GC.KeepAlive(single);
    }
}
