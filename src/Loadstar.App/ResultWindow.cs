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

        var body = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = Theme.MonoFont,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = rendered,
        };

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
                Height = 38,
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

        Shown += (_, _) => { body.SelectionLength = 0; Activate(); };
    }

    private static string Render(Advice advice, RedistributionPlan? plan, string? question)
    {
        var text = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(question))
        {
            text.AppendLine(string.Format(Strings.Get("result.youAsked"), question)).AppendLine();
        }

        // The screen NAME is an English enum value the model reports and code parses; the label around
        // it is ours to translate.
        text.AppendLine(string.Format(Strings.Get("result.screen"), advice.RecognizedScreen));

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
