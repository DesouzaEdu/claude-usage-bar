using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using UsageCore;

namespace ClaudeUsageBar;

sealed class PanelState
{
    public Usage? Usage { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool OpensAtLogin { get; set; }
}

sealed record Theme(Color Back, Color Fore, Color Secondary, Color Track, Color Border, Color Button,
    Color Blue, Color Orange, Color Red, Color Green)
{
    public static readonly Theme Dark = new(
        Color.FromArgb(0x20, 0x20, 0x20), Color.White, Color.FromArgb(0xA0, 0xA0, 0xA0), Color.FromArgb(0x3A, 0x3A, 0x3A),
        Color.FromArgb(0x45, 0x45, 0x45), Color.FromArgb(0x2D, 0x2D, 0x2D),
        Color.FromArgb(0x0A, 0x84, 0xFF), Color.FromArgb(0xFF, 0x9F, 0x0A), Color.FromArgb(0xFF, 0x45, 0x3A), Color.FromArgb(0x32, 0xD7, 0x4B));

    public static readonly Theme Light = new(
        Color.FromArgb(0xF9, 0xF9, 0xF9), Color.Black, Color.FromArgb(0x60, 0x60, 0x60), Color.FromArgb(0xE0, 0xE0, 0xE0),
        Color.FromArgb(0xD0, 0xD0, 0xD0), Color.White,
        Color.FromArgb(0x00, 0x7A, 0xFF), Color.FromArgb(0xE0, 0x80, 0x00), Color.FromArgb(0xE0, 0x30, 0x25), Color.FromArgb(0x1E, 0x8E, 0x3E));
}

/// <summary>The panel that opens from the tray icon, the Windows counterpart of the macOS MenuBarExtra window.
/// Rows are painted by hand (like the macOS UsageBar) so colors and spacing match on every Windows version.</summary>
sealed class PanelForm : Form
{
    const int LogicalWidth = 280;

    public event Action? RefreshClicked;
    public event Action? QuitClicked;
    public event Action<bool>? OpenAtLoginChanged;

    public PanelState State { get; } = new();

    readonly float? fixedScale;
    readonly bool? fixedDark;
    readonly Button refresh = new() { Text = "Refresh", FlatStyle = FlatStyle.Flat, TabIndex = 0 };
    readonly Button gear = new() { Text = "", FlatStyle = FlatStyle.Flat, TabIndex = 1, AccessibleName = "Options" };
    readonly ContextMenuStrip gearMenu = new();
    readonly ToolStripMenuItem loginItem = new("Open at login") { CheckOnClick = true };
    Theme theme = Theme.Dark;
    Font titleFont = null!, bodyFont = null!, captionFont = null!, iconFont = null!;
    float scale = 1;

    /// <param name="fixedScale">Fixed pixel scale and theme, for the offscreen README screenshot.</param>
    public PanelForm(float? fixedScale = null, bool? dark = null)
    {
        this.fixedScale = fixedScale;
        fixedDark = dark;
        Text = "Claude Usage Bar";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;

        loginItem.CheckedChanged += (_, _) => { if (loginItem.Checked != State.OpensAtLogin) OpenAtLoginChanged?.Invoke(loginItem.Checked); };
        gearMenu.Items.AddRange([loginItem, new ToolStripSeparator(), new ToolStripMenuItem("Quit", null, (_, _) => QuitClicked?.Invoke()) { ShortcutKeyDisplayString = "Ctrl+Q" }]);
        refresh.Click += (_, _) => RefreshClicked?.Invoke();
        gear.Click += (_, _) => gearMenu.Show(gear, new Point(0, gear.Height));
        Controls.AddRange([refresh, gear]);
        ApplyStyle();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: no Alt+Tab entry
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11 rounded corners; ignored on Windows 10.
        var round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyStyle();
        UpdateView();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.R) RefreshClicked?.Invoke();
        else if (e.Control && e.KeyCode == Keys.Q) QuitClicked?.Invoke();
        else if (e.KeyCode == Keys.Escape) Hide();
        else return;
        e.Handled = true;
    }

    int S(float v) => (int)Math.Round(v * scale);

    /// <summary>Re-reads the theme and DPI; fonts and buttons follow.</summary>
    public void ApplyStyle()
    {
        scale = fixedScale ?? DeviceDpi / 96f;
        theme = fixedDark ?? WindowsTheme.AppsDark ? Theme.Dark : Theme.Light;
        foreach (var f in new[] { titleFont, bodyFont, captionFont, iconFont }) f?.Dispose();
        titleFont = new Font("Segoe UI Semibold", 13 * scale, GraphicsUnit.Pixel);
        bodyFont = new Font("Segoe UI", 13 * scale, GraphicsUnit.Pixel);
        captionFont = new Font("Segoe UI", 11 * scale, GraphicsUnit.Pixel);
        iconFont = new Font("Segoe MDL2 Assets", 12 * scale, GraphicsUnit.Pixel);

        BackColor = theme.Back;
        foreach (var b in new[] { refresh, gear })
        {
            b.BackColor = theme.Button;
            b.ForeColor = theme.Fore;
            b.FlatAppearance.BorderColor = theme.Border;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(theme.Button, theme == Theme.Dark ? 0.2f : -0.05f);
        }
        refresh.Font = bodyFont;
        gear.Font = iconFont;
        gearMenu.Renderer = new ToolStripProfessionalRenderer();
    }

    /// <summary>Lays the panel out for the current <see cref="State"/> and repaints it.</summary>
    public void UpdateView()
    {
        loginItem.Checked = State.OpensAtLogin;
        var height = RenderContent(null);
        var buttonHeight = S(28);
        var footerY = height - S(16) - buttonHeight;
        gear.Bounds = new Rectangle(S(LogicalWidth) - S(16) - S(32), footerY, S(32), buttonHeight);
        var refreshWidth = TextRenderer.MeasureText("Refresh", bodyFont).Width + S(20);
        refresh.Bounds = new Rectangle(gear.Left - S(8) - refreshWidth, footerY, refreshWidth, buttonHeight);
        ClientSize = new Size(S(LogicalWidth), height);
        Invalidate();
    }

    /// <summary>Places the panel next to the tray, on the screen with the cursor, whatever side the taskbar is on.</summary>
    public void ShowNearTray()
    {
        ApplyStyle();
        UpdateView();
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        var margin = S(12);
        var x = Math.Clamp(cursor.X - Width / 2, area.Left + margin, area.Right - Width - margin);
        var y = cursor.Y < area.Top ? area.Top + margin : area.Bottom - Height - margin;
        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!gearMenu.Visible) Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RenderContent(e.Graphics);
        using var border = new Pen(theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    static string When(DateTimeOffset d)
    {
        var local = d.ToLocalTime();
        return local.ToString("ddd d MMM, ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
    }

    /// <summary>Draws the content when <paramref name="g"/> is set, else only measures it. Returns the panel height.</summary>
    int RenderContent(Graphics? g)
    {
        var pad = S(16);
        var width = S(LogicalWidth) - 2 * pad;
        var y = pad;
        var now = DateTimeOffset.Now;

        int Line(string text, Font font, Color color, int top, TextFormatFlags align = TextFormatFlags.Left)
        {
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | align;
            var size = TextRenderer.MeasureText(text, font, new Size(width, 0), flags);
            if (g != null) TextRenderer.DrawText(g, text, font, new Rectangle(pad, top, width, size.Height), color, flags);
            return size.Height;
        }

        var rows = State.Usage?.Rows ?? [];
        foreach (var row in rows)
        {
            var percent = $"{UsageFormat.Round(row.Percent)}%";
            var h = Line(percent, bodyFont, theme.Fore, y, TextFormatFlags.Right);
            if (g != null)
            {
                var percentWidth = TextRenderer.MeasureText(percent, bodyFont).Width;
                TextRenderer.DrawText(g, row.Title, titleFont, new Rectangle(pad, y, width - percentWidth - S(8), h),
                    theme.Fore, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            y += h + S(4);

            y += S(3); // room for the time marker, which sticks out of the bar
            if (g != null) DrawBar(g, new Rectangle(pad, y, width, S(BarHeight)), row, now);
            y += S(BarHeight) + S(3);

            if (row.ResetsAt is { } resetsAt)
                y += S(4) + Line($"{UsageFormat.ResetDescription(resetsAt, now)} · {When(resetsAt)}", captionFont, theme.Secondary, y + S(4));
            switch (Pace.Projection(row, now))
            {
                case Projection.Lasts lasts:
                    y += S(4) + Line($"At this pace: lasts, about {UsageFormat.Round(lasts.PercentAtReset)}% at reset", captionFont, theme.Green, y + S(4));
                    break;
                case Projection.RunsOut runsOut:
                    y += S(4) + Line($"At this pace: runs out {When(runsOut.At)}", captionFont, theme.Orange, y + S(4));
                    break;
            }
            y += S(12);
        }
        if (State.Error is { } error) y += Line(error, bodyFont, theme.Red, y) + S(12);
        else if (rows.Count == 0) y += Line("Loading…", bodyFont, theme.Secondary, y) + S(12);

        if (g != null)
        {
            using var divider = new Pen(theme.Border, Math.Max(1, S(1)));
            g.DrawLine(divider, pad, y, pad + width, y);
        }
        y += S(12);

        var buttonHeight = S(28);
        if (State.UpdatedAt is { } updated && g != null)
        {
            var text = $"Updated {updated.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}";
            TextRenderer.DrawText(g, text, captionFont, new Rectangle(pad, y, width / 2, buttonHeight), theme.Secondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return y + buttonHeight + pad;
    }

    const int BarHeight = 12;

    /// <summary>The usage bar: a gradient fill for what is used, a translucent extension up to where the
    /// current pace ends at reset, faint ticks at the 70% and 90% color thresholds, and a marker for how
    /// much of the window has passed (being left of it means using less than an even pace).</summary>
    void DrawBar(Graphics g, Rectangle r, UsageRow row, DateTimeOffset now)
    {
        var percent = Math.Clamp(row.Percent, 0, 100);
        var color = percent >= 90 ? theme.Red : percent >= 70 ? theme.Orange : theme.Blue;
        int X(double p) => r.X + (int)Math.Round(r.Width * Math.Clamp(p, 0, 100) / 100);

        using var trackPath = Capsule(r);
        using (var track = new SolidBrush(theme.Track)) g.FillPath(track, trackPath);
        var clip = g.Clip;
        g.SetClip(trackPath);

        using (var tick = new Pen(Color.FromArgb(70, theme.Fore), Math.Max(1, S(1))))
            foreach (var threshold in new[] { 70, 90 }) g.DrawLine(tick, X(threshold), r.Top, X(threshold), r.Bottom);

        var projected = Pace.Projection(row, now) switch
        {
            Projection.Lasts lasts => lasts.PercentAtReset,
            Projection.RunsOut => 100,
            _ => percent,
        };
        if (projected > percent)
        {
            var ghostColor = projected >= 90 ? theme.Red : projected >= 70 ? theme.Orange : color;
            using var ghost = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(110, ghostColor), Color.FromArgb(45, ghostColor));
            g.FillRectangle(ghost, Rectangle.FromLTRB(X(percent) - r.Height, r.Top, X(projected), r.Bottom));
        }

        if (percent > 0)
        {
            var fillRect = new Rectangle(r.X, r.Y, Math.Max(X(percent) - r.X, r.Height), r.Height);
            using var fillPath = Capsule(fillRect);
            using var fill = new LinearGradientBrush(fillRect, ControlPaint.Light(color, 0.6f), color, LinearGradientMode.Horizontal);
            g.FillPath(fill, fillPath);
            // Soft highlight on the upper half for depth.
            using var shine = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, r.Height / 2 + 1),
                Color.FromArgb(90, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical);
            g.FillRectangle(shine, fillRect.X, fillRect.Y, fillRect.Width, r.Height / 2);
        }
        g.Clip = clip;

        // Time marker: share of the window already elapsed.
        if (row.ResetsAt is { } resetsAt && Pace.WindowLength(row.Group) is { } length)
        {
            var elapsed = 100 * (1 - (resetsAt - now).TotalSeconds / length.TotalSeconds);
            if (elapsed is > 0 and < 100)
            {
                var x = X(elapsed);
                using var outline = new Pen(theme.Back, S(4));
                using var marker = new Pen(theme.Fore, S(2));
                g.DrawLine(outline, x, r.Top - S(3), x, r.Bottom + S(3));
                g.DrawLine(marker, x, r.Top - S(3), x, r.Bottom + S(3));
            }
        }
    }

    static GraphicsPath Capsule(Rectangle r)
    {
        var path = new GraphicsPath();
        var d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
