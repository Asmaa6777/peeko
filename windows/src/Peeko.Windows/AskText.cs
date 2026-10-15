namespace Peeko.Windows;

/// <summary>
/// A tiny dark prompt for one line of text. Laid out with auto-sizing panels rather than fixed
/// pixel positions, so the message wraps and the buttons stay visible at any display scaling.
/// </summary>
internal static class AskText
{
    public static string? Ask(IWin32Window owner, string title, string label, string initial = "", string okText = "Add", bool allowEmpty = false)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScaleMode = AutoScaleMode.None,   // sizes are scaled by hand below; fonts scale on their own
            BackColor = Theme.Night,
            ForeColor = Theme.Chalk,
            Font = new Font("Segoe UI", 10f),
            Padding = new Padding(8),
        };
        var scale = form.DeviceDpi / 96f;
        var width = (int)(340 * scale);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8),
        };

        var text = new Label
        {
            Text = label,
            AutoSize = true,
            MaximumSize = new Size(width, 0),   // wrap long messages instead of cutting them off
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, (int)(8 * scale)),
        };
        var box = new TextBox
        {
            Text = initial,
            Width = width,
            BackColor = Color.FromArgb(0x24, 0x24, 0x24),
            ForeColor = Theme.Chalk,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, (int)(12 * scale)),
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        var ok = new Button
        {
            Text = okText,
            AutoSize = true,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Green,
            ForeColor = Theme.Night,
            Padding = new Padding((int)(10 * scale), (int)(2 * scale), (int)(10 * scale), (int)(2 * scale)),
        };
        ok.FlatAppearance.BorderSize = 0;
        var cancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Chalk,
            Padding = new Padding((int)(10 * scale), (int)(2 * scale), (int)(10 * scale), (int)(2 * scale)),
        };
        cancel.FlatAppearance.BorderColor = Theme.Outline;
        // RightToLeft flow: the first added sits on the right.
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(box, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => box.Focus();

        var result = form.ShowDialog(owner);
        var value = box.Text.Trim();
        return result == DialogResult.OK && (allowEmpty || value.Length > 0) ? value : null;
    }
}
