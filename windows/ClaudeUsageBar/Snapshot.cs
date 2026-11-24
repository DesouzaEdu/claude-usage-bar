using System.Drawing.Imaging;
using System.Globalization;
using UsageCore;

namespace ClaudeUsageBar;

/// <summary>Renders the panel offscreen with sample data into a PNG (<c>ClaudeUsageBar.exe --snapshot out.png</c>),
/// so the README screenshot never shows real account data.</summary>
static class Snapshot
{
    public static bool Render(string path)
    {
        // Same date format on every machine that redraws the README image.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        var now = DateTimeOffset.UtcNow;
        string Iso(double hours) => now.AddHours(hours).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var json = $$"""
        {"limits": [
          {"kind": "session", "group": "session", "percent": 34, "resets_at": "{{Iso(2.5)}}"},
          {"kind": "weekly_all", "group": "weekly", "percent": 58, "resets_at": "{{Iso(88)}}"},
          {"kind": "weekly_scoped", "group": "weekly", "percent": 12, "resets_at": "{{Iso(88)}}",
           "scope": {"model": {"display_name": "Fable"} } }
        ]}
        """;
        // 2x so the image is sharp on high-DPI screens.
        using var form = new PanelForm(fixedScale: 2, dark: true);
        form.State.Usage = Usage.Decode(json);
        form.State.UpdatedAt = DateTimeOffset.Now;
        form.State.OpensAtLogin = true;
        form.UpdateView();
        form.CreateControl();
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
        // The form is never shown, so its buttons are not part of its own capture.
        foreach (Control child in form.Controls) child.DrawToBitmap(bitmap, child.Bounds);
        try
        {
            bitmap.Save(path, ImageFormat.Png);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }
}
