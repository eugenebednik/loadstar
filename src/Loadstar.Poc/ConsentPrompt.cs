namespace Loadstar.Poc;

/// <summary>
/// The first-run consent screen.
///
/// <para>docs/anti-cheat-posture.md requires that capture is off until the user turns it on and
/// that the first-run flow says plainly what gets captured and where it is sent. That is a
/// promise about informed consent, so the text below names the provider and is explicit that
/// screenshots leave the machine — burying that would satisfy the letter of the rule and none of
/// its point.</para>
/// </summary>
internal static class ConsentPrompt
{
    public const string CurrentVersion = "1";

    public static bool Ask(bool assumeYes)
    {
        Console.WriteLine();
        Console.WriteLine("  Before Loadstar can read your screen");
        Console.WriteLine("  ------------------------------------");
        Console.WriteLine();
        Console.WriteLine("  Loadstar captures the game window and sends that image to Anthropic to be");
        Console.WriteLine("  analysed. Specifically:");
        Console.WriteLine();
        Console.WriteLine("    - It captures only the window whose title you configure, not your whole");
        Console.WriteLine("      desktop and not any other application.");
        Console.WriteLine("    - The bottom-left corner, which carries the party list and chat, is blanked");
        Console.WriteLine("      out before anything is sent.");
        Console.WriteLine("    - The image goes to Anthropic and nowhere else. Loadstar has no backend.");
        Console.WriteLine("    - Your API key is stored encrypted on this machine and is sent only to");
        Console.WriteLine("      Anthropic.");
        Console.WriteLine();
        Console.WriteLine("  Loadstar never touches the game: no injection, no renderer hooking, no reading");
        Console.WriteLine("  process memory, no synthetic input. It captures the way OBS does.");
        Console.WriteLine();

        if (assumeYes)
        {
            Console.WriteLine("  Consent given via --yes.");
            Console.WriteLine();
            return true;
        }

        Console.Write("  Turn on screen capture? [y/N] ");
        var answer = Console.ReadLine();
        Console.WriteLine();

        return answer?.Trim().StartsWith('y') == true
            || answer?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
    }
}
