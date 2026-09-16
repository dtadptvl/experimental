namespace KiloSessionManager;

internal static class PromptDialog
{
    public static string? Show(IWin32Window owner, string title, string label, string initialValue)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(460, 128)
        };

        var prompt = new Label { Left = 12, Top = 12, Width = 430, Text = label };
        var box = new TextBox { Left = 12, Top = 38, Width = 430, Text = initialValue };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 286, Top = 82, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 367, Top = 82, Width = 75 };
        form.Controls.AddRange(new Control[] { prompt, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { box.SelectAll(); box.Focus(); };

        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
