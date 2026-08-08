using Loadstar.Core.Startup;

using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// Autostart, with the registry replaced by a dictionary. The rules worth pinning down are the quoting,
/// the tolerance about what counts as "already correct", and the fact that nothing here throws — a
/// checkbox that takes the settings dialog down with it is worse than a checkbox that reports failure.
/// </summary>
public class StartupRegistrationTests
{
    /// <summary>The install path really does contain a space, which is the whole reason quoting matters.</summary>
    private const string Installed = @"C:\Program Files\Loadstar\Loadstar.exe";

    private sealed class FakeKey : IStartupKey
    {
        public string? Value;
        public int Writes;
        public int Deletes;
        public Exception? Throws;

        public string? Read() => Throws is null ? Value : throw Throws;

        public void Write(string command)
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            Writes++;
            Value = command;
        }

        public void Delete()
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            Deletes++;
            Value = null;
        }
    }

    [Fact]
    public void ItIsOffUntilItIsTurnedOn()
    {
        var key = new FakeKey();

        Assert.False(new StartupRegistration(key, Installed).IsEnabled());
    }

    /// <summary>
    /// The default the user asked for, stated as a test: nothing registers autostart on its own. Only an
    /// explicit Set(true) writes an entry.
    /// </summary>
    [Fact]
    public void NothingRegistersItselfWithoutBeingAsked()
    {
        var key = new FakeKey();
        var startup = new StartupRegistration(key, Installed);

        Assert.False(startup.IsEnabled());

        startup.Synchronise();

        Assert.Equal(0, key.Writes);
        Assert.Null(key.Value);
        Assert.False(startup.IsEnabled());
    }

    /// <summary>
    /// Unquoted, Windows parses a Run value up to the first space and tries to launch "C:\Program".
    /// It fails at every sign-in and says nothing.
    /// </summary>
    [Fact]
    public void TheRegisteredCommandIsQuoted()
    {
        var key = new FakeKey();

        Assert.True(new StartupRegistration(key, Installed).Set(true));
        Assert.Equal($"\"{Installed}\"", key.Value);
    }

    [Fact]
    public void TurningItOffRemovesTheEntry()
    {
        var key = new FakeKey { Value = $"\"{Installed}\"" };
        var startup = new StartupRegistration(key, Installed);

        Assert.True(startup.Set(false));
        Assert.Null(key.Value);
        Assert.False(startup.IsEnabled());
    }

    /// <summary>Turning it off when it is already off is success, not an error.</summary>
    [Fact]
    public void TurningItOffTwiceIsFine()
    {
        var key = new FakeKey();
        var startup = new StartupRegistration(key, Installed);

        Assert.True(startup.Set(false));
        Assert.True(startup.Set(false));
        Assert.False(startup.IsEnabled());
    }

    /// <summary>
    /// The upgrade case, and the reason Synchronise exists. An entry left pointing at the old install
    /// directory fails in the worst possible way: the checkbox still reads on, nothing errors, and the app
    /// simply never starts again.
    /// </summary>
    [Fact]
    public void AStalePathIsRewrittenToTheRunningExecutable()
    {
        var key = new FakeKey { Value = @"""C:\Users\me\AppData\Local\Loadstar\0.4.0\Loadstar.exe""" };
        var startup = new StartupRegistration(key, Installed);

        Assert.True(startup.Synchronise());
        Assert.Equal($"\"{Installed}\"", key.Value);
        Assert.True(startup.IsEnabled());
    }

    /// <summary>
    /// And a correct entry is left alone. Rewriting on every launch would be pointless registry churn, so
    /// the comparison has to tolerate the ways an equivalent path can differ.
    /// </summary>
    [Theory]
    [InlineData(@"""C:\Program Files\Loadstar\Loadstar.exe""")]
    [InlineData(@"C:\Program Files\Loadstar\Loadstar.exe")]
    [InlineData(@"""c:\program files\loadstar\loadstar.exe""")]
    [InlineData(@"""C:/Program Files/Loadstar/Loadstar.exe""")]
    [InlineData("   \"C:\\Program Files\\Loadstar\\Loadstar.exe\"   ")]
    public void AnEquivalentPathIsNotTreatedAsStale(string stored)
    {
        var key = new FakeKey { Value = stored };

        Assert.False(new StartupRegistration(key, Installed).Synchronise());
        Assert.Equal(0, key.Writes);
    }

    /// <summary>
    /// Group policy denies registry writes on plenty of managed machines. The checkbox has to report that
    /// rather than throwing out of a click handler.
    /// </summary>
    [Fact]
    public void ADeniedWriteReportsFailureInsteadOfThrowing()
    {
        var key = new FakeKey { Throws = new UnauthorizedAccessException("policy") };
        var startup = new StartupRegistration(key, Installed);

        Assert.False(startup.Set(true));
        Assert.False(startup.Set(false));
        Assert.False(startup.IsEnabled());
        Assert.False(startup.Synchronise());
    }

    /// <summary>
    /// Some single-file hosts report no process path. Registering a command that cannot launch anything is
    /// worse than saying the feature is unavailable, so the UI gets told and can disable the checkbox.
    /// </summary>
    [Fact]
    public void WithNoExecutablePathTheFeatureReportsUnsupported()
    {
        var key = new FakeKey();

        foreach (var path in (string?[])[null, "", "   "])
        {
            var startup = new StartupRegistration(key, path);

            Assert.False(startup.IsSupported);
            Assert.Null(startup.DesiredCommand);
            Assert.False(startup.Set(true));
            Assert.Equal(0, key.Writes);
        }
    }

    /// <summary>
    /// Turning it OFF must still work without a usable path. Otherwise a user who somehow got an entry
    /// registered would have no way to remove it from inside the app.
    /// </summary>
    [Fact]
    public void ItCanStillBeTurnedOffWithNoExecutablePath()
    {
        var key = new FakeKey { Value = $"\"{Installed}\"" };

        Assert.True(new StartupRegistration(key, null).Set(false));
        Assert.Null(key.Value);
    }
}
