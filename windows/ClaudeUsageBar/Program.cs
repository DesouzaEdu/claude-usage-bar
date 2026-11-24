namespace ClaudeUsageBar;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var i = Array.IndexOf(args, "--snapshot");
        if (i >= 0 && i + 1 < args.Length) return Snapshot.Render(args[i + 1]) ? 0 : 1;

        // One tray icon per user session: a second launch (e.g. login item plus a manual start) just exits.
        using var mutex = new Mutex(true, @"Local\ClaudeUsageBar", out var first);
        if (!first) return 0;
        Application.Run(new TrayApp());
        return 0;
    }
}
