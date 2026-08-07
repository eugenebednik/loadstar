namespace Loadstar.App;

/// <summary>
/// A two-column label/field form that positions its rows itself.
///
/// <para><b>This exists because TableLayoutPanel was the settings dialog's whole load time.</b> Every add
/// to a table with <see cref="SizeType.AutoSize"/> rows re-measures the entire table, so the cost per row
/// climbs as rows accumulate — the General tab took ~1.9 seconds to build, every single time the dialog was
/// opened. Batching the adds behind <c>SuspendLayout</c> was tried and measured and made it WORSE (4.9s to
/// open instead of 4.3s; 11.5s when the form was suspended too), because deferring the measurement does not
/// avoid it. The only way out was to stop asking a layout engine to solve a problem that is one running
/// total of heights.</para>
///
/// <para><b>Arranged once, on demand, not on every add.</b> <see cref="AddRow"/> only parents the controls
/// and records the pair; <see cref="Arrange"/> does the arithmetic. That split is what makes this cheap, and
/// it also puts the measurement in the one place where it can be correct — see the remarks there.</para>
///
/// <para>Absolute positions are appropriate here in a way they usually are not: this dialog is
/// <see cref="FormBorderStyle.FixedDialog"/> at a fixed <see cref="Form.ClientSize"/>, so there is no resize
/// case to serve and nothing for an engine to recompute.</para>
/// </summary>
internal sealed class FormGrid : Panel
{
    private readonly List<Row> _rows = [];

    /// <summary>
    /// Width of the caption column.
    ///
    /// <para>200 rather than 160 because "Character build URL" wrapped onto two lines at the narrower
    /// width and knocked every following row out of vertical alignment. Captions that DO wrap are handled
    /// — see <see cref="Arrange"/> — but a column wide enough that most do not is still worth having.</para>
    /// </summary>
    public int LabelColumn { get; init; } = 200;

    /// <summary>Width forced onto text boxes and combo boxes, which would otherwise stretch or collapse.</summary>
    public int FieldWidth { get; init; } = 420;

    /// <summary>Nudges the caption down onto the text baseline of the field beside it.</summary>
    public int CaptionBaselineNudge { get; init; } = 6;

    /// <summary>
    /// Adds a row. Pass an empty <paramref name="label"/> for a field that spans from the field column with
    /// no caption — hints, checkboxes and loose buttons all do.
    /// </summary>
    public void AddRow(string label, Control field)
    {
        ArgumentNullException.ThrowIfNull(field);

        // Forced here rather than in Arrange because the width has to be set BEFORE anything measures the
        // field's preferred height: a text box's wrap width decides how tall it wants to be.
        if (field is TextBox or ComboBox)
        {
            field.Width = FieldWidth;
        }

        Label? caption = null;

        if (!string.IsNullOrEmpty(label))
        {
            caption = new Label
            {
                Text = label,
                AutoSize = false,
                Width = LabelColumn,
                BackColor = Color.Transparent,
            };

            Controls.Add(caption);
        }

        Controls.Add(field);

        _rows.Add(new Row(caption, field));
    }

    /// <summary>
    /// Computes every row's position, top to bottom.
    ///
    /// <para><b>Call this AFTER the theme has been applied and the form has scaled for DPI</b> —
    /// <c>SettingsWindow.OnLoad</c>, past <c>base.OnLoad</c>. Both matter:</para>
    ///
    /// <para>The theme pass assigns fonts (<c>Theme.Apply</c> sets <c>Font = UiFont</c> on text boxes,
    /// combos and checkboxes), and a height measured against a different font is wrong. And
    /// <see cref="ContainerControl.AutoScaleMode"/> is <see cref="AutoScaleMode.Dpi"/>, which rescales child
    /// BOUNDS when the form's handle is created — so positions computed in the constructor would be scaled a
    /// second time on a 150% display, while the font heights they were derived from were already in device
    /// pixels. Arranging after scaling sidesteps the double-scale entirely.</para>
    ///
    /// <para>Idempotent, so it is safe to call again if anything downstream changes a font.</para>
    /// </summary>
    public void Arrange()
    {
        var y = Padding.Top;
        var captionFont = Font;
        var fieldLeft = Padding.Left + LabelColumn;

        // THE WIDTH THE FIELD COLUMN ACTUALLY HAS, and the reason this is not just a running total of
        // heights. A TableLayoutPanel cell handed its content a width, which is what made the model row —
        // a wrapping FlowLayoutPanel holding a combo, a button and a price hint — fold onto a second line.
        // Absolute positioning offers unlimited width instead, so the same row laid out in one long line
        // and ran off the edge of the dialog. Constraining it restores the wrap.
        //
        // FLOORED AT FieldWidth, and that floor is load-bearing rather than defensive. A grid on a tab that
        // is not the active one sits inside a hidden page whose Dock has not been resolved, so ClientSize is
        // still the default and the subtraction goes NEGATIVE. Clamping that to zero produced two bugs that
        // looked unrelated: Math.Min(FieldWidth, 0) set every combo box to zero width, so the server picker
        // was simply absent, and a MaximumSize of 0 means UNLIMITED in WinForms, so the hint labels stopped
        // wrapping and ran off the edge of the window. One wrong number, two symptoms, neither obviously
        // about width.
        var available = Math.Max(FieldWidth, ClientSize.Width - fieldLeft - Padding.Right);

        foreach (var (caption, field) in _rows)
        {
            // Margins are honoured rather than replaced by a uniform gap, so the spacing the callers
            // already tuned per row — a hint tucked under its field, a button given room below it —
            // survives this change instead of needing to be re-tuned.
            y += field.Margin.Top;

            int fieldHeight;

            if (field.AutoSize)
            {
                // MaximumSize rather than Width: an AutoSize control ignores an assigned width and resizes
                // itself back, so a cap is the only thing it will respect. A narrower existing cap is kept
                // — the hint labels set 420 deliberately so their prose does not run the full width.
                var cap = field.MaximumSize.Width > 0
                    ? Math.Min(field.MaximumSize.Width, available)
                    : available;

                field.MaximumSize = new Size(cap, 0);
                field.Location = new Point(fieldLeft, y);
                fieldHeight = field.PreferredSize.Height;
            }
            else
            {
                field.SetBounds(fieldLeft, y, Math.Min(field.Width, available), field.Height);
                fieldHeight = field.Height;
            }

            var captionHeight = 0;

            if (caption is not null)
            {
                // Measured with word-break at the column width, because several captions genuinely wrap:
                // "Ссылка на сборку персонажа" is two lines at 200px and its row has to be tall enough for
                // both. Font.Height alone would clip the second line.
                captionHeight = TextRenderer.MeasureText(
                    caption.Text,
                    captionFont,
                    new Size(LabelColumn, int.MaxValue),
                    TextFormatFlags.WordBreak).Height;

                caption.SetBounds(Padding.Left, y + CaptionBaselineNudge, LabelColumn, captionHeight);
            }

            y += Math.Max(fieldHeight, captionHeight + CaptionBaselineNudge) + field.Margin.Bottom;
        }

        ContentHeight = y + Padding.Bottom;
    }

    /// <summary>
    /// How tall the arranged rows came out, available after <see cref="Arrange"/>.
    ///
    /// <para><b>Deliberately not wired to AutoScroll.</b> Setting <c>AutoScrollMinSize</c> implicitly turns
    /// <c>AutoScroll</c> on, which put a scrollbar on both axes of the settings dialog — and WinForms draws
    /// scrollbars in the system light style whatever the theme, which is exactly why this dialog was sized
    /// to avoid them. Exposed instead so the owner can size the window to its content, which is the real
    /// fix and one a table could never offer: it never told anyone how tall it was.</para>
    /// </summary>
    public int ContentHeight { get; private set; }

    private readonly record struct Row(Label? Caption, Control Field);
}
