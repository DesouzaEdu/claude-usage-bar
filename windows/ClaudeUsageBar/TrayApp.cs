using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using UsageCore;

namespace ClaudeUsageBar;

/// <summary>The tray icon and its panel: the Windows counterpart of ClaudeUsageBarApp and UsageModel.</summary>
sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon tray = new();
    readonly PanelForm panel = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 5 * 60 * 1000 };
    readonly System.Windows.Forms.Timer retryTimer = new();
    readonly AppState state = AppState.Load();
    readonly ToolStripMenuItem loginMenuItem = new("Open at login") { CheckOnClick = true };
    Icon? icon;
    DateTime panelHiddenAt;
    bool refreshing;

    PanelState View => panel.State;

    public TrayApp()
    {
        // Opt in to launch at login on first run; afterwards the toggle decides.
        if (!state.DidSetUpLoginItem)
        {
            state.DidSetUpLoginItem = true;
            state.Save();
            SetOpensAtLogin(true);
        }
        else
        {
            LoginItem.Repair();
        }
        View.OpensAtLogin = LoginItem.IsEnabled;

        panel.RefreshClicked += () => _ = RefreshAsync();
        panel.QuitClicked += Quit;
        panel.OpenAtLoginChanged += SetOpensAtLogin;
        panel.VisibleChanged += (_, _) => { if (!panel.Visible) panelHiddenAt = DateTime.UtcNow; };

        loginMenuItem.CheckedChanged += (_, _) => { if (loginMenuItem.Checked != View.OpensAtLogin) SetOpensAtLogin(loginMenuItem.Checked); };
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            new ToolStripMenuItem("Refresh", null, (_, _) => _ = RefreshAsync()),
            loginMenuItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Quit", null, (_, _) => Quit()),
        ]);
        menu.Opening += (_, _) => loginMenuItem.Checked = View.OpensAtLogin;
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };

        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        retryTimer.Tick += (_, _) => { retryTimer.Stop(); _ = RefreshAsync(); };

        UpdateTray();
        tray.Visible = true;
        _ = RefreshAsync();
    }

    void TogglePanel()
    {
        if (panel.Visible) { panel.Hide(); return; }
        // Clicking the icon while the panel is open first deactivates (hides) it; don't reopen on that same click.
        if ((DateTime.UtcNow - panelHiddenAt).TotalMilliseconds < 300) return;
        panel.ShowNearTray();
    }

    void SetOpensAtLogin(bool on)
    {
        try
        {
            LoginItem.Set(on);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            View.Error = $"Open at login: {e.Message}";
        }
        View.OpensAtLogin = LoginItem.IsEnabled;
        panel.UpdateView();
    }

    void Quit()
    {
        tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Dispose();
            icon?.Dispose();
            timer.Dispose();
            retryTimer.Dispose();
            panel.Dispose();
        }
        base.Dispose(disposing);
    }

    async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var usage = await UsageApi.FetchAsync();
            View.Usage = usage;
            View.Error = null;
            View.UpdatedAt = DateTimeOffset.Now;
            Notify(Pace.Alerts(usage.Rows, DateTimeOffset.Now));
        }
        catch (UsageException e)
        {
            View.Error = e.Message;
            // The usage endpoint rate-limits per account; retry when it says so instead of waiting the full 5 min.
            if (e.RetryAfter is { } wait)
            {
                retryTimer.Interval = (int)Math.Clamp((wait + 1) * 1000, 1000, int.MaxValue);
                retryTimer.Start();
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            View.Error = e is TaskCanceledException ? "Request timed out" : e.Message;
        }
        finally
        {
            refreshing = false;
            UpdateTray();
            panel.UpdateView();
        }
    }

    AlertLevel Level => (View.Usage?.Rows ?? []).Select(r => Pace.AlertLevel(r, DateTimeOffset.Now)).DefaultIfEmpty(AlertLevel.None).Max();

    void UpdateTray()
    {
        var usage = View.Usage;
        var level = Level;
        // The tray only fits a square icon, so it shows the first limit (the session) and the tooltip has the rest.
        var top = usage is null ? [] : Usage.TopPerGroup(usage.Rows);
        var label = usage is null ? (View.Error is null ? "…" : "!") : top.Count == 0 ? "–" : UsageFormat.Round(top[0].Percent).ToString(CultureInfo.InvariantCulture);
        var old = icon;
        icon = TrayIcon.Render(label, usage is null && View.Error is not null ? AlertLevel.High : level);
        tray.Icon = icon;
        old?.Dispose();

        var title = usage is null ? (View.Error is null ? "✳ …" : "⚠") : (level == AlertLevel.None ? "✳ " : "⚠ ") + usage.MenuBarTitle;
        var lines = new List<string> { $"Claude {title}" };
        if (usage is not null)
            lines.AddRange(usage.Rows.Select(r => $"{r.Title}: {UsageFormat.Round(r.Percent)}%"));
        else if (View.Error is not null)
            lines.Add(View.Error);
        var text = string.Join("\n", lines);
        tray.Text = text.Length > 127 ? text[..126] + "…" : text; // NotifyIcon caps the tooltip at 127 chars
    }

    /// <summary>Shows each alert once per limit window; sent keys are kept in the state file.</summary>
    void Notify(IReadOnlyList<UsageAlert> alerts)
    {
        var fresh = alerts.Where(a => !state.SentAlerts.Contains(a.Key)).ToList();
        if (fresh.Count == 0) return;
        // shortcut: keys are never pruned; a few per week, fine for years.
        state.SentAlerts.UnionWith(fresh.Select(a => a.Key));
        state.Save();

        static string When(DateTimeOffset d) => d.ToLocalTime().ToString("ddd d MMM, t", CultureInfo.CurrentCulture);
        var messages = fresh.Select(alert =>
        {
            var row = alert.Row;
            var used = UsageFormat.Round(row.Percent);
            return alert.Level == AlertLevel.Pace && Pace.Projection(row, DateTimeOffset.Now) is Projection.RunsOut runsOut
                ? (Title: $"{row.Title}: heavy usage", Body: $"{used}% used. At this pace it runs out {When(runsOut.At)}.")
                : (Title: $"{row.Title}: {used}% used", Body: row.ResetsAt is { } r ? $"Resets {When(r)}." : "");
        }).ToList();
        // A tray balloon (a toast on Windows 10/11) holds one message, so several alerts are merged into one.
        var (title, body) = messages.Count == 1
            ? messages[0]
            : ("Claude usage alerts", string.Join("\n", messages.Select(m => $"{m.Title}. {m.Body}")));
        tray.ShowBalloonTip(10_000, title, string.IsNullOrEmpty(body) ? " " : body, ToolTipIcon.Warning);
    }
}

static class TrayIcon
{
    /// <summary>Draws the percentage into the square tray icon: plain text in the taskbar's color, or
    /// white on orange/red when a limit runs out early or passes 90%, like the macOS menu bar.</summary>
    public static Icon Render(string text, AlertLevel level)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Color? background = level switch
            {
                AlertLevel.Pace => Color.FromArgb(0xFF, 0x9F, 0x0A),
                AlertLevel.High => Color.FromArgb(0xFF, 0x45, 0x3A),
                _ => null,
            };
            if (background is { } bg)
            {
                using var brush = new SolidBrush(bg);
                using var path = RoundedRect(new RectangleF(0, 0, size, size), size / 4f);
                g.FillPath(brush, path);
            }
            var fore = background is not null ? Color.White : WindowsTheme.TaskbarDark ? Color.White : Color.Black;
            var em = size * (text.Length >= 3 ? 0.5f : 0.72f);
            using var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(fore);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(-size / 4f, 0, size * 1.5f, size + size / 16f), format);
        }
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
