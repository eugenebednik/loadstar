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

        var copy = new Button { Text = "Copy" };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK };

        Theme.MakePrimary(close);
        Theme.MakeSecondary(copy);

        copy.Click += (_, _) =>
        {
            // Advice is worth keeping — pasting it into notes or a guild chat is a normal thing to
            // want, and re-running a capture to recover it costs another API call.
            if (!string.IsNullOrEmpty(rendered))
            {
                Clipboard.SetText(rendered);
                copy.Text = "Copied";
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
                Text = "This screen can't fully answer that — open the screen named below and press "
                    + "the capture hotkey again.",
                Dock = DockStyle.Top,
                Height = 38,
                Padding = new Padding(16, 8, 16, 0),
                ForeColor = Theme.Accent,
                Font = new Font(Theme.UiFont, FontStyle.Bold),
                BackColor = Color.Transparent,
            };

            Controls.Add(callToAction);
        }

        Controls.Add(CreateActionBar(close, copy));
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
            text.AppendLine($"You asked: {question}").AppendLine();
        }

        text.AppendLine($"Screen recognised as: {advice.RecognizedScreen}");

        if (!advice.AnsweredFromScreen)
        {
            text.AppendLine("NOTE: this screen cannot fully answer that question — see 'Could not see' below.");
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

            text.AppendLine(step.Cost.Count > 0
                ? $"   Cost: {string.Join(", ", step.Cost.Select(c => $"{c.Value:N0} {c.Key}"))}" +
                  (step.Affordable ? string.Empty : "   — NOT AFFORDABLE")
                : "   Cost: free");

            if (!string.IsNullOrWhiteSpace(step.Rationale))
            {
                text.AppendLine($"   {step.Rationale}");
            }

            text.AppendLine();
        }

        if (advice.MissingInformation.Count > 0)
        {
            text.AppendLine("Could not see:");

            foreach (var missing in advice.MissingInformation)
            {
                text.AppendLine($"  - {missing}");
            }

            text.AppendLine();
        }

        if (plan is not null && (plan.HasChanges || plan.Unpriceable.Count > 0))
        {
            text.AppendLine(new string('-', 70));
            text.AppendLine("STAT REDISTRIBUTION — computed locally, not by the model");
            text.AppendLine(new string('-', 70));
            text.AppendLine(plan.Describe()).AppendLine();
            text.AppendLine("Assumptions behind these numbers:");

            foreach (var caveat in RedistributionPlan.Caveats)
            {
                text.AppendLine($"  - {caveat}");
            }

            text.AppendLine();
        }

        if (advice.Usage is { } usage)
        {
            text.AppendLine($"Tokens: {usage.InputTokens:N0} in, {usage.OutputTokens:N0} out");
        }

        return text.ToString().Replace("\n", Environment.NewLine);
    }
}
