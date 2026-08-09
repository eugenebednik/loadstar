using System.Diagnostics;
using System.Text;
using Loadstar.Core.Model;
using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// Shows the advice, with the locally computed numbers kept visually distinct from the model's prose.
///
/// <para>That separation is the point. Stat costs and drop estimates are arithmetic this app does
/// itself precisely because a language model is unreliable at it; presenting both in one
/// undifferentiated block would throw away the distinction and leave the user unable to tell which
/// numbers are checkable.</para>
/// </summary>
internal sealed class ResultWindow : ThemedForm
{
    public ResultWindow(Advice advice, RedistributionPlan? plan, string? question)
    {
        Text = "Loadstar";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 680);
        MinimumSize = new Size(560, 400);

        // Opened BEHIND the game without this, which made the app look like it had silently failed:
        // the player asks a question, waits, and nothing appears.
        //
        // Activate() alone does not fix it. Windows refuses SetForegroundWindow to a process that does
        // not already own the foreground, and it fails silently rather than returning an error — so a
        // fullscreen game keeps the front and the dialog sits behind it. TopMost does not go through
        // that check. AskWindow has always set it, which is exactly why the ask dialog appeared
        // correctly and the answer did not.
        TopMost = true;

        var rendered = Render(advice, plan, question);

        // A RichTextBox rather than a TextBox, for one reason: DetectUrls. The advice names questlog builds
        // and the useful thing to do with a build is open it, which a plain TextBox cannot offer at all — the
        // player was left selecting a URL by hand out of a monospace block.
        var body = new RichTextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = Theme.MonoFont,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            DetectUrls = true,
            // Otherwise the control paints a white margin outside the text area, which reads as a rendering
            // fault rather than as padding.
            Padding = new Padding(6),
            // NEWLINES NORMALISED TO \n, and this is not cosmetic — without it the answer arrives as one
            // run-on paragraph. Render builds with StringBuilder.AppendLine, which emits Environment.NewLine
            // (CRLF on Windows), and assigning CRLF to RichTextBox.Text loses the breaks entirely. Diagnosed
            // rather than guessed: the same text showed 21 line breaks going in, the control reported a
            // different count, and the only breaks that survived on screen were the handful written as bare
            // \n inside a step's rationale. A plain TextBox handles CRLF fine, which is why this only appeared
            // when the control was swapped to get clickable links.
            Text = rendered.Replace("\r\n", "\n"),
        };

        body.LinkClicked += (_, e) => OpenBuildLink(e.LinkText);

        // NO MANUAL RECOLOURING, and that is a measured decision rather than a shrug. WinForms' RichTextBox
        // has no LinkColor property, so the tempting fix is to select each URL after setting Text and assign
        // SelectionColor. That was implemented and it DESTROYED THE TEXT: 38 lines went in and 4 came out,
        // because Select() indexes the control's own character space, which does not agree with what the Text
        // getter returns once CRLF is involved. The answer arrived as one run-on paragraph.
        //
        // The system link colour is legible against this Surface anyway, and links are already clickable from
        // DetectUrls. Colouring them would be cosmetic; losing every line break was not. If a future attempt
        // is made, build the content as RTF with an explicit colour table rather than mutating the control
        // after the fact — and check the line count.

        var bodyFrame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 4, 16, 4),
            BackColor = Theme.Background,
        };

        bodyFrame.Controls.Add(body);

        var copy = new Button { Text = Strings.Get("result.copy") };
        var close = new Button { Text = Strings.Get("result.close"), DialogResult = DialogResult.OK };

        // RETAKE, offered only when the model said this screen could not answer the question. Then it
        // is the single most useful control in the window -- the advice has just named a screen to open,
        // and the alternative was closing the window, navigating, finding the hotkey and retyping.
        //
        // Hidden otherwise rather than merely disabled: on a good answer a retake button is noise, and
        // it would sit next to Close inviting a pointless second API call.
        var retake = new Button { Text = Strings.Get("ask.retake"), DialogResult = DialogResult.Retry };

        Theme.MakePrimary(close);
        Theme.MakeSecondary(copy);
        Theme.MakeSecondary(retake);

        copy.Click += (_, _) =>
        {
            // Advice is worth keeping — pasting it into notes or a guild chat is a normal thing to
            // want, and re-running a capture to recover it costs another API call.
            if (!string.IsNullOrEmpty(rendered))
            {
                Clipboard.SetText(rendered);
                copy.Text = Strings.Get("result.copied");
            }
        };

        Controls.Add(bodyFrame);

        // When the captured screen could not answer the question, the useful next action is to open
        // the right screen and capture again — so say that at the top rather than burying it in the
        // body text where it reads as an apology.
        if (!advice.AnsweredFromScreen)
        {
            var callToAction = new Label
            {
                Text = Strings.Get("result.wrongScreen"),
                Dock = DockStyle.Top,
                // Bold, so taller than the UI font, and a full sentence in nine languages.
                Height = Theme.RowHeight(lines: 2, extra: 10),
                Padding = new Padding(16, 8, 16, 0),
                ForeColor = Theme.Accent,
                Font = new Font(Theme.UiFont, FontStyle.Bold),
                BackColor = Color.Transparent,
            };

            Controls.Add(callToAction);
        }

        Controls.Add(advice.AnsweredFromScreen
            ? CreateActionBar(close, copy)
            : CreateActionBar(retake, close, copy));
        Controls.Add(CreateHeading(advice.Headline));

        AcceptButton = close;
        CancelButton = close;

        Shown += (_, _) =>
        {
            // Caret to the start with nothing selected. A RichTextBox opens with the whole first line
            // highlighted otherwise, which looks like the user clicked something.
            body.SelectionStart = 0;
            body.SelectionLength = 0;

            Activate();
        };
    }

    /// <summary>
    /// Opens a link from the advice text in the default browser.
    ///
    /// <para><b>Only questlog.gg, and that restriction is the point rather than tidiness.</b> This text is
    /// model output, and model output is shaped by screenshots and by build names other players wrote —
    /// neither of which this app controls. A link is the one element of an answer that does something when
    /// touched, so the host is checked here rather than trusted from the string. Anything else is left as
    /// plain text the player can read and copy, which costs them a paste and costs an attacker the click.</para>
    ///
    /// <para>Also why the prompt lists the real URLs and says to quote them character for character: a
    /// composed questlog URL 404s, and a dead link is indistinguishable from a deleted build.</para>
    /// </summary>
    private void OpenBuildLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        if (!Core.Net.LinkPolicy.IsAllowed(link, out var uri) || uri is null)
        {
            Core.Diagnostics.Log.Warn($"Result: refused to open a link outside questlog.gg — {link}");

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Core.Diagnostics.Log.Error($"Result: could not open {uri.AbsoluteUri}", ex);
        }
    }

    private static string Render(Advice advice, RedistributionPlan? plan, string? question)
    {
        var text = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(question))
        {
            text.AppendLine(string.Format(Strings.Get("result.youAsked"), question)).AppendLine();
        }

        // EVERY screen, not just one. A single line saying "Screen recognised as: Character" after four
        // screenshots were sent reads as though the other three were discarded — which is exactly how it
        // was reported, and exactly what it looked like.
        //
        // The screen NAMES are English enum values the model reports and code parses; the labels around
        // them are ours to translate.
        if (advice.Screens.Count > 1)
        {
            text.AppendLine(Strings.Get("result.screens"));

            for (var i = 0; i < advice.Screens.Count; i++)
            {
                var reading = advice.Screens[i];

                text.Append("  ")
                    .Append(i + 1)
                    .Append(". ")
                    .Append(reading.Screen)
                    .Append(reading.Used ? " — " : Strings.Get("result.screens.unused"));

                if (!string.IsNullOrWhiteSpace(reading.Note))
                {
                    text.Append(reading.Note);
                }

                text.AppendLine();
            }
        }
        else
        {
            text.AppendLine(string.Format(Strings.Get("result.screen"), advice.RecognizedScreen));
        }

        if (!advice.AnsweredFromScreen)
        {
            text.AppendLine(Strings.Get("result.note.wrongScreen"));
        }

        // The headline is the window's heading, so it is not repeated here.
        text.AppendLine();

        foreach (var step in advice.Steps)
        {
            text.AppendLine($"{step.Rank}. {step.Action}");

            if (!string.IsNullOrWhiteSpace(step.Category))
            {
                text.AppendLine($"   [{step.Category}]");
            }

            if (step.Cost.Count > 0)
            {
                // Currency NAMES stay as the model gave them: Sollant and Lucent are what the player
                // sees in game, and a translated currency is one they cannot find.
                var costs = string.Join(", ", step.Cost.Select(c => $"{c.Value:N0} {c.Key}"));

                text.AppendLine($"   {string.Format(Strings.Get("result.cost"), costs)}"
                    + (step.Affordable ? string.Empty : "   — " + Strings.Get("result.notAffordable")));
            }
            else
            {
                text.AppendLine($"   {Strings.Get("result.costFree")}");
            }

            if (!string.IsNullOrWhiteSpace(step.Rationale))
            {
                text.AppendLine($"   {step.Rationale}");
            }

            text.AppendLine();
        }

        // BEFORE "could not see", because with no build pinned this is the most useful thing on the page:
        // the player has nothing to aim at, and every URL here is clickable.
        if (advice.SuggestedBuilds.Count > 0)
        {
            text.AppendLine(Strings.Get("result.suggestedBuilds"));
            text.AppendLine();

            foreach (var build in advice.SuggestedBuilds)
            {
                var labels = new[] { build.Role, build.Axis }
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .ToArray();

                text.Append("  ").Append(build.Name);

                if (labels.Length > 0)
                {
                    text.Append("  (").Append(string.Join(", ", labels)).Append(')');
                }

                text.AppendLine();

                if (!string.IsNullOrWhiteSpace(build.Why))
                {
                    text.AppendLine($"    {build.Why}");
                }

                // On its own line and unindented past the margin, so DetectUrls gets the whole URL and a
                // long one is not broken by wrapping in the middle.
                if (!string.IsNullOrWhiteSpace(build.Url))
                {
                    text.AppendLine($"    {build.Url}");
                }

                text.AppendLine();
            }
        }

        if (advice.MissingInformation.Count > 0)
        {
            text.AppendLine(Strings.Get("result.couldNotSee"));

            foreach (var missing in advice.MissingInformation)
            {
                text.AppendLine($"  - {missing}");
            }

            text.AppendLine();
        }

        // Rendered by PlanReport, not plan.Describe(): that method is English prose for the developer
        // console, and this one is for the player.
        if (plan is not null)
        {
            var report = PlanReport.Render(plan);

            if (!string.IsNullOrWhiteSpace(report))
            {
                text.AppendLine(report);
            }
        }

        if (advice.Usage is { } usage)
        {
            text.AppendLine(string.Format(
                Strings.Get("result.tokens"),
                usage.InputTokens.ToString("N0"),
                usage.OutputTokens.ToString("N0")));
        }

        return text.ToString().Replace("\n", Environment.NewLine);
    }
}
