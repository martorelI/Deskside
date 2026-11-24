using System.ComponentModel;

namespace Deckside.App.Ui;

/// <summary>Factory of the styled controls every page uses (same look as Rackside).</summary>
internal static class Make
{
    public static Button Primary(string text, EventHandler? click = null) => Styled(text, Theme.Accent, Color.White, Theme.Accent, click);

    public static Button Secondary(string text, EventHandler? click = null) => Styled(text, Theme.Surface, Theme.TextColor, Theme.Border, click);

    public static Button Danger(string text, EventHandler? click = null) => Styled(text, Theme.Danger, Color.White, Theme.Danger, click);

    private static Button Styled(string text, Color back, Color fore, Color border, EventHandler? click)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = Theme.BodyBold,
            Padding = new Padding(12, 5, 12, 5),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = back == Theme.Surface ? Theme.AccentSoft : ControlPaint.Dark(back, 0.05f);
        // A flat button keeps its colors when disabled: switch to a gray look so the state is obvious.
        b.EnabledChanged += (_, _) =>
        {
            b.BackColor = b.Enabled ? back : Theme.Disabled;
            b.ForeColor = b.Enabled ? fore : Theme.DisabledText;
            b.FlatAppearance.BorderColor = b.Enabled ? border : Theme.Border;
            b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
        };
        if (click is not null)
        {
            b.Click += click;
        }

        return b;
    }

    public static TextBox Input(string text = "", bool password = false, int width = 260) => new()
    {
        Text = text,
        UseSystemPasswordChar = password,
        Width = width,
        Font = Theme.Body,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 2, 0, 6),
    };

    public static ComboBox Combo(IEnumerable<string> items, int selected = 0, int width = 260)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Font = Theme.Body, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 0, 6) };
        c.Items.AddRange([.. items]);
        if (c.Items.Count > selected && selected >= 0)
        {
            c.SelectedIndex = selected;
        }

        return c;
    }

    public static NumericUpDown Number(int value, int min, int max, int width = 100) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = width,
        Font = Theme.Body,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 2, 0, 6),
    };

    public static CheckBox Check(string text, bool value = false) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Font = Theme.Body,
        Margin = new Padding(0, 3, 16, 3),
    };

    public static RadioButton Radio(string text, bool value = false) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Font = Theme.Body,
        Margin = new Padding(0, 3, 16, 3),
    };

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Theme.Body,
        ForeColor = color ?? Theme.TextColor,
        Margin = new Padding(0, 6, 12, 0),
    };

    /// <summary>A note in gray that wraps to the width of its container.</summary>
    public static Label Note(string text, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(760, 0),
        Font = Theme.Small,
        ForeColor = color ?? Theme.Muted,
        Margin = new Padding(0, 2, 0, 8),
    };

    /// <summary>Horizontal row of controls.</summary>
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0), FlowDirection = FlowDirection.LeftToRight };
        f.Controls.AddRange(controls);
        return f;
    }

    /// <summary>Read-only table with the light look of the cards (no 3D borders, soft selection).</summary>
    public static DataGridView Grid()
    {
        var g = new DataGridView
        {
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Theme.Surface,
            GridColor = Theme.Border,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 36,
            EnableHeadersVisualStyles = false,
            Font = Theme.Body,
            Dock = DockStyle.Fill,
        };
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        g.ColumnHeadersDefaultCellStyle.Font = Theme.BodyBold;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Surface;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        g.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
        g.DefaultCellStyle.SelectionForeColor = Theme.TextColor;
        g.DefaultCellStyle.ForeColor = Theme.TextColor;
        g.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        g.RowTemplate.Height = 32;
        return g;
    }
}

/// <summary>Label + control grid in two columns.</summary>
internal sealed class Fields : TableLayoutPanel
{
    public Fields(int labelWidth = 190)
    {
        ColumnCount = 2;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = new Padding(0);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    public T Add<T>(string label, T control) where T : Control
    {
        Controls.Add(Make.Label(label, color: Theme.Muted), 0, RowCount);
        Controls.Add(control, 1, RowCount);
        RowCount++;
        return control;
    }

    public T Full<T>(T control) where T : Control
    {
        Controls.Add(control, 0, RowCount);
        SetColumnSpan(control, 2);
        RowCount++;
        return control;
    }
}

/// <summary>A white panel with a title and a thin border; the body is a one-column table.</summary>
internal class Card : Panel
{
    public Card(string title, string? subtitle = null)
    {
        BackColor = Theme.Surface;
        Padding = new Padding(18, 14, 18, 14);
        Margin = new Padding(0, 0, 0, 14);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddRows, Margin = new Padding(0) };
        Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(Body);
        if (subtitle is not null)
        {
            var sub = Make.Note(subtitle);
            sub.Dock = DockStyle.Top;
            Controls.Add(sub);
        }

        var head = Make.Label(title, Theme.Section);
        head.Dock = DockStyle.Top;
        head.Margin = new Padding(0, 0, 0, 6);
        Controls.Add(head);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TableLayoutPanel Body { get; }

    public T Add<T>(T control) where T : Control
    {
        if (control is Fields or TextBox { Multiline: true })
        {
            control.Dock = DockStyle.None;
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        }

        Body.Controls.Add(control, 0, Body.RowCount);
        Body.RowCount++;
        return control;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>A scrollable column of cards.</summary>
internal sealed class CardList : Panel
{
    public CardList()
    {
        AutoScroll = true;
        Dock = DockStyle.Fill;
        Padding = new Padding(24, 16, 24, 16);
        BackColor = Theme.Background;
    }

    /// <summary>Adds at the bottom (Dock = Top stacks in reverse order, so it is moved to the front).</summary>
    public T Add<T>(T card) where T : Control
    {
        card.Dock = DockStyle.Top;
        Controls.Add(card);
        card.BringToFront();
        return card;
    }
}

/// <summary>A colored band with a message (warnings, errors, success) that hides itself when empty.</summary>
internal sealed class Banner : Label
{
    public Banner()
    {
        AutoSize = false;
        Dock = DockStyle.Top;
        Height = 38;
        Padding = new Padding(16, 0, 16, 0);
        TextAlign = ContentAlignment.MiddleLeft;
        Font = Theme.BodyBold;
        Visible = false;
    }

    public void Show(string? text, Color fore, Color back)
    {
        Text = text ?? "";
        ForeColor = fore;
        BackColor = back;
        Visible = !string.IsNullOrEmpty(text);
    }
}
