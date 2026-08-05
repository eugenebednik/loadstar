using System.Text;

namespace Loadstar.Core.Diagnostics;

/// <summary>
/// A file log, next to the settings.
///
/// <para>This exists because of a real failure: a question was asked, a screenshot was taken, and no
/// answer ever appeared. The error had been reported into a six-second tray balloon — which Windows
/// often suppresses outright, and which nobody sees while looking at a game — and written nowhere. So
/// the app knew exactly what went wrong and there was no way to find out what that was.</para>
///
/// <para>Static and dependency-free on purpose. The alternative was threading an <c>ILogger</c>
/// through a tray app, a hotkey host and a capture pipeline to write one file, and the moment logging
/// is inconvenient to reach it stops being called from the places that need it most.</para>
///
/// <para><b>Never throws.</b> A logger that can fail takes down the thing it was meant to explain.
/// Every write is guarded, and a broken log is silently no log.</para>
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>
    /// Rotation threshold. Small deliberately: this is read by a human pasting it into a bug report,
    /// so recent history is worth far more than complete history, and an unbounded log on a machine
    /// that captures every two minutes is a slow leak.
    /// </summary>
    private const long MaxBytes = 1024 * 1024;

    private static string? _path;

    /// <summary>
    /// Where the log is written, once <see cref="Initialize"/> has run. Null before that, which is
    /// what makes logging a no-op in tests rather than something that writes into a test runner's
    /// working directory.
    /// </summary>
    public static string? Path => _path;

    /// <summary>
    /// Points the log at a directory — in practice the same one holding settings, so a user asked for
    /// "the folder with your settings in it" produces everything needed to diagnose a problem.
    /// </summary>
    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                _path = System.IO.Path.Combine(directory, "loadstar.log");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _path = null;
            }
        }
    }

    public static void Info(string message) => Write("INFO ", message, exception: null);

    public static void Warn(string message) => Write("WARN ", message, exception: null);

    /// <summary>
    /// Records a failure with its exception. <paramref name="context"/> should say what was being
    /// attempted, since an exception message alone rarely identifies which operation produced it.
    /// </summary>
    public static void Error(string context, Exception? exception) => Write("ERROR", context, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_path is null)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append("  ")
            .Append(level)
            .Append("  ")
            .Append(message);

        if (exception is not null)
        {
            // The full exception, not just Message: the type and stack are what identify a bug, and
            // an inner exception is frequently the only part that names the real cause.
            line.AppendLine().Append(exception);
        }

        line.AppendLine();

        lock (Gate)
        {
            try
            {
                Rotate();
                File.AppendAllText(_path, line.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written is not a reason to fail the operation being logged.
            }
        }
    }

    /// <summary>
    /// Keeps one previous generation and starts fresh. Renaming rather than truncating means a
    /// failure that has just scrolled past the threshold is still recoverable from the .1 file.
    /// </summary>
    private static void Rotate()
    {
        if (_path is null || !File.Exists(_path) || new FileInfo(_path).Length < MaxBytes)
        {
            return;
        }

        var previous = _path + ".1";

        try
        {
            File.Delete(previous);
            File.Move(_path, previous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep appending to the oversized file rather than losing the entries.
        }
    }
}
