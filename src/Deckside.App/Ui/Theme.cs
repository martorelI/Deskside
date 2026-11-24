namespace Deckside.App.Ui;

/// <summary>Colors and fonts of the window (one place, so pages look the same).</summary>
internal static class Theme
{
    public static readonly Color Sidebar = Color.FromArgb(31, 41, 55);
    public static readonly Color SidebarText = Color.FromArgb(229, 231, 235);
    public static readonly Color SidebarActive = Color.FromArgb(55, 65, 81);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color Background = Color.FromArgb(249, 250, 251);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Ok = Color.FromArgb(21, 128, 61);
    public static readonly Color Danger = Color.FromArgb(185, 28, 28);
    public static readonly Color DangerBack = Color.FromArgb(254, 226, 226);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font Small = new("Segoe UI", 8.5f);

    public static Label Heading(string text) => new() { Text = text, Font = Title, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };

    public static Label Note(string text) => new() { Text = text, ForeColor = Muted, AutoSize = true, MaximumSize = new Size(720, 0), Margin = new Padding(0, 0, 0, 8) };

    public static Button PrimaryButton(string text) => new()
    {
        Text = text, AutoSize = true, BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 4, 10, 4),
    };
}
