using Loadstar.Core.Configuration;

namespace Loadstar.App;

/// <summary>
/// Records a hotkey by having the user press it, rather than type it.
///
/// <para>Typing a combination into a text box asks the user to know a spelling convention that only
/// exists inside this app — "Ctrl+Alt+S" versus "CTRL-ALT-S" versus "Control+Alt+S" — and then
/// punishes them with a validation error when they guess differently. Pressing the keys removes the
/// guess entirely: what they press is what gets stored.</para>
///
/// <para>Only Ctrl, Alt and Shift are capturable. The Windows key is intercepted by the shell before
/// a normal window sees it, so it is documented as unavailable rather than silently ignored.</para>
/// </summary>
internal sealed class HotkeyRecorderDialog : ThemedForm
{
    private readonly Label _preview;
    private readonly Label _hint;
    private readonly Button _ok;

    public Hotkey? Recorded { get; private set; }

    public HotkeyRecorderDialog(string? current)
    {
        Text = "Loadstar — set hotkey";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 250);

        // The whole form listens, so focus never has to be in a particular control.
        KeyPreview = true;

        _preview = new Label
        {
            Text = current ?? "Press a combination",
            Dock = DockStyle.Top,
            // Sized against its OWN 20pt font, not the UI font — this is the largest text in the app.
            Height = Theme.RowHeight(lines: 1, extra: 20, font: new Font(Theme.HeadingFont.FontFamily, 20f, FontStyle.Bold)),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Theme.HeadingFont.FontFamily, 20f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = Color.Transparent,
        };

        _hint = new Label
        {
            Text = "Hold Ctrl, Alt or Shift and press a key.",
            Dock = DockStyle.Top,
            Height = Theme.RowHeight(lines: 2, extra: 12),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
        };

        _ok = new Button { Text = "Use this", DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = Strings.Get("common.cancel"), DialogResult = DialogResult.Cancel };

        Theme.MakePrimary(_ok);
        Theme.MakeSecondary(cancel);

        Controls.Add(_hint);
        Controls.Add(_preview);
        Controls.Add(CreateActionBar(_ok, cancel));
        Controls.Add(CreateHeading("Press the keys you want"));

        CancelButton = cancel;

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Never let the combination itself act on the dialog — Alt would open the system menu and
        // Enter would accept before anything was recorded.
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode is Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }

        // A modifier on its own is a partial press, not a hotkey. Show progress rather than
        // rejecting it, so holding Ctrl before choosing a key feels responsive.
        if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin)
        {
            _preview.Text = DescribeModifiers(e) is { Length: > 0 } partial ? partial + "+…" : "Press a combination";
            _preview.ForeColor = Theme.SubtleText;
            _ok.Enabled = false;
            return;
        }

        var modifiers = DescribeModifiers(e);

        if (modifiers.Length == 0)
        {
            _preview.Text = KeyName(e.KeyCode);
            _preview.ForeColor = Color.FromArgb(255, 120, 110);
            _hint.Text = "That needs a modifier — a bare key would be captured in every application.";
            _ok.Enabled = false;
            return;
        }

        var candidate = $"{modifiers}+{KeyName(e.KeyCode)}";
        var parsed = Hotkey.TryParse(candidate);

        if (parsed is null)
        {
            _preview.Text = candidate;
            _preview.ForeColor = Color.FromArgb(255, 120, 110);
            _hint.Text = "That key cannot be used as a hotkey. Try a letter, number or function key.";
            _ok.Enabled = false;
            return;
        }

        Recorded = parsed;
        _preview.Text = parsed.Display;
        _preview.ForeColor = Theme.Accent;
        _hint.Text = "Press another combination to change it, or accept.";
        _ok.Enabled = true;
    }

    private static string DescribeModifiers(KeyEventArgs e)
    {
        var parts = new List<string>(3);

        if (e.Control) { parts.Add("Ctrl"); }
        if (e.Alt) { parts.Add("Alt"); }
        if (e.Shift) { parts.Add("Shift"); }

        return string.Join("+", parts);
    }

    /// <summary>
    /// Turns a <see cref="Keys"/> value into something <see cref="Hotkey.TryParse"/> understands.
    /// Digits arrive as <c>D1</c>…<c>D0</c>, which would otherwise fail to parse.
    /// </summary>
    private static string KeyName(Keys key)
    {
        var name = key.ToString();

        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
        {
            return name[1..];
        }

        if (name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7)
        {
            return name[6..];
        }

        return name switch
        {
            "Return" => "Enter",
            "Capital" => "CapsLock",
            "Next" => "PageDown",
            "Prior" => "PageUp",
            "Oemtilde" => "`",
            _ => name,
        };
    }
}
