namespace Loadstar.App;

/// <summary>
/// First-run consent. Capture stays off until this is accepted.
///
/// <para>docs/anti-cheat-posture.md requires that the first-run flow "says plainly what gets captured
/// and where it is sent". So this names the provider and states that images leave the machine —
/// burying either would satisfy the letter of the rule and none of its purpose.</para>
///
/// <para>It deliberately does not promise that regions are blacked out. This game applies no fixed
/// privacy mask any more; see <c>ScreenRegions.PrivacyMasks</c> for why, and note that the ask window
/// showing each image with a delete button is the stronger guarantee anyway, because the player can
/// check it.</para>
///
/// <para><see cref="CurrentVersion"/> is compared against the stored acceptance, so changing what is
/// captured means bumping it and asking again rather than relying on consent given for something
/// narrower.</para>
/// </summary>
internal static class ConsentPrompt
{
    public const string CurrentVersion = "1";

    public const string Body = """
        Loadstar captures the game window and sends that image to Anthropic to be analysed.

        • It captures only the window you configure — not your whole desktop, and not any other
          application.
        • The bottom-left corner, which carries the party list and chat, is blanked out before
          anything is sent.
        • The image goes to Anthropic and nowhere else. Loadstar has no backend.
        • Your API key is stored encrypted on this machine and is sent only to Anthropic.
        • Nothing is captured unless you press the hotkey. You see the exact image, and can cancel,
          before it is sent.

        Loadstar never touches the game: no injection, no renderer hooking, no reading process
        memory, no synthetic input. It captures the way OBS does.

        Turn on screen capture?
        """;

    public static bool Ask(IWin32Window? owner) =>
        MessageBox.Show(
            owner,
            Body,
            "Loadstar — before it can read your screen",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}
