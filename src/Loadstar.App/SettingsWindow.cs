using Loadstar.Capture.Windows;
using Loadstar.Core.Ai;
using Loadstar.Core.Configuration;
using Loadstar.Games.ThroneAndLiberty;

namespace Loadstar.App;

/// <summary>
/// Configuration: which window to watch, which build to aim at, the hotkey, the API key, and the
/// boss timer.
///
/// <para>The window target gets the most room because it is the setting most likely to be wrong in a
/// way the user cannot diagnose. Title matching once selected <b>Firefox</b>, because a questlog
/// character build page was open and the tab title contained the game's name — so this offers a
/// picker over running processes and a "browse for the game executable" path, and stores the
/// <b>process name</b> rather than the title.</para>
/// </summary>
internal sealed class SettingsWindow : ThemedForm
{
    private readonly SettingsStore _store;
    private readonly SecretStore _secrets;

    private readonly TextBox _buildUrl = new() { Width = 380 };
    private readonly ComboBox _process = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDown };
    // Read-only by design: the combination is recorded by pressing it, not typed. Typing invites
    // a spelling the parser rejects, for no benefit.
    private readonly TextBox _hotkey = new() { Width = 150, ReadOnly = true, TabStop = false };
    private readonly ComboBox _provider = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
    // Editable on purpose: the bundled model lists age, and a model released after this build must
    // be usable by typing its id rather than by waiting for a new version of Loadstar.
    private readonly ComboBox _model = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _apiKey = new() { Width = 380, UseSystemPasswordChar = true };

    // AutoSize with a width cap, rather than the fixed height used for _status. Line height scales
    // with DPI, so any height picked here is right on one display and clips the last line on
    // another — which is exactly how the "Keys:" URL went missing at 150% scaling. MaximumSize caps
    // the width so the text wraps, and the row grows to fit whatever that produces.
    private readonly Label _billing = new()
    {
        AutoSize = true,
        MaximumSize = new Size(FieldWidth, 0),
    };

    /// <summary>
    /// Price and context for the selected model, beside the picker.
    /// <para>Separate from the combo's items on purpose — see <see cref="AiModelInfo.ToString"/>.
    /// Anything shown inside a value ends up saved as that value.</para>
    /// </summary>
    private readonly Label _modelHint = new() { AutoSize = true, Margin = new Padding(8, 8, 0, 0) };

    /// <summary>
    /// The model chosen per provider this session, so switching provider and back does not discard
    /// a deliberate choice. Seeded from settings and written back on save.
    /// </summary>
    private readonly Dictionary<AiProviderKind, string> _modelChoices = [];

    /// <summary>
    /// Suppresses the provider-changed handler while code (rather than the user) moves the
    /// selection. Without it, populating the combo fires the handler mid-construction and overwrites
    /// the stored model with the default before it has even been read.
    /// </summary>
    private bool _loadingProvider;

    /// <summary>
    /// Suppresses the server-changed handler while code populates the server list, for the same
    /// reason as <see cref="_loadingProvider"/>: the handler exists to react to a person picking a
    /// server, and firing it during a programmatic refresh overwrites the timezone they may have
    /// just typed.
    /// </summary>
    private bool _loadingServers;
    private readonly ThemedCheckBox _consent = new() { Text = Strings.Get("settings.consent") };
    private readonly ComboBox _language = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _server = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _regionLabel = new() { AutoSize = true };
    private readonly ComboBox _timezone = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _alertMinutes = new() { Width = 150 };
    private readonly ThemedCheckBox _bossOverlay = new() { Text = Strings.Get("settings.bossOverlay") };
    private readonly ThemedCheckBox _bossAlerts = new() { Text = Strings.Get("settings.bossAlerts") };

    // Fixed height rather than AutoSize: this text changes at runtime (server counts, targeting
    // messages), and a growing label reflowed every row beneath it.
    private readonly Label _status = new() { AutoSize = false, Width = FieldWidth, Height = 34 };

    public SettingsWindow(SettingsStore store, SecretStore secrets)
    {
        _store = store;
        _secrets = secrets;

        Text = "Loadstar — settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Tall enough that nothing needs a scrollbar. The scrollbar was not just ugly — WinForms
        // renders it in the system light style regardless of theme, so avoiding it entirely is
        // simpler than fighting it, and the content genuinely fits.
        // 860 rather than 700: the provider row, its billing note and the model price hint added
        // four rows to the General tab. A scrollbar is the thing being avoided — WinForms renders it
        // in the system light style regardless of theme, so growing the window is simpler than
        // fighting it, and the content genuinely fits at this height.
        ClientSize = new Size(760, 900);

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildBossTimerTab());

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 8) };
        frame.Controls.Add(tabs);

        var save = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        Theme.MakePrimary(save);
        Theme.MakeSecondary(cancel);

        save.Click += (_, _) =>
        {
            if (!Persist())
            {
                // Keep the dialog open so the user can fix what was rejected.
                DialogResult = DialogResult.None;
                return;
            }

            // Close by hand when we are not modal. `DialogResult` only dismisses a form shown with
            // ShowDialog; under `--settings` the window is run by Application.Run and setting it
            // does nothing at all — so Save appeared to do nothing, despite having written both the
            // settings file and the API key. Cancel has the same problem, below.
            if (!Modal)
            {
                Close();
            }
        };

        cancel.Click += (_, _) =>
        {
            if (!Modal)
            {
                Close();
            }
        };

        Controls.Add(frame);
        Controls.Add(CreateActionBar(save, cancel));
        Controls.Add(CreateHeading("Settings"));

        AcceptButton = save;
        CancelButton = cancel;

        Populate();
    }

    /// <summary>
    /// Re-applies the values that theming can disturb.
    ///
    /// <para><see cref="ThemedForm.OnShown"/> runs <see cref="Theme.Apply"/>, which touches
    /// <c>FlatStyle</c> on combo boxes — and that recreates the native handle, dropping whatever
    /// text was in an editable one. The stored process name kept coming up blank because of it, and
    /// a blank field then overwrote a good setting on Save. Restoring here, after the theme has
    /// settled, is the reliable order rather than trying to guess which property assignment is
    /// destructive.</para>
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Posted, not called directly: theming recreates combo box handles during Shown, and a Text
        // assigned before that completes is discarded. Also re-applied when the async server fetch
        // finishes, since that lands later still.
        BeginInvoke(new Action(RestoreStoredValues));
    }

    private void RestoreStoredValues()
    {
        var settings = _store.Load();

        SetComboText(_process, settings.Capture.WindowProcessName);
        SetComboText(_timezone, settings.Game.ServerTimeZone);

        // Provider first, then model: selecting the provider repopulates the model list, so the
        // reverse order sets a model into a list that is about to be replaced.
        _loadingProvider = true;

        _provider.SelectedItem = _provider.Items.Cast<ProviderChoice>()
            .FirstOrDefault(c => c.Kind == settings.Ai.Provider);

        _loadingProvider = false;

        OnProviderChanged();
        SetComboText(_model, _modelChoices.GetValueOrDefault(settings.Ai.Provider, settings.Ai.Model));

        _consent.Checked = settings.CaptureConsentGiven;
        _bossAlerts.Checked = settings.Game.BossAlertsEnabled;
        _bossOverlay.Checked = settings.Overlay.ShowBossCountdown;
    }

    /// <summary>
    /// Selects a value in a combo box, preferring a real item over free text.
    ///
    /// <para>Assigning <c>Text</c> alone does not reliably stick after a handle recreation, and does
    /// nothing at all on a DropDownList. Matching an existing item and setting
    /// <c>SelectedIndex</c> is what actually holds.</para>
    /// </summary>
    private static void SetComboText(ComboBox combo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        if (combo.DropDownStyle != ComboBoxStyle.DropDownList)
        {
            // Not in the list — a process that is not currently running, for instance. Keep it as
            // free text rather than dropping a setting the user deliberately chose.
            combo.Items.Add(value);
            combo.SelectedIndex = combo.Items.Count - 1;
        }
    }

    /// <summary>Uniform width for every input, so the right-hand edge is not ragged.</summary>
    private const int FieldWidth = 420;

    private static TableLayoutPanel NewGrid()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(20, 18, 20, 12),
            AutoScroll = false,
            BackColor = Theme.Surface,
        };

        // 200 rather than 160: "Character build URL" wrapped onto two lines at the old width, which
        // knocked every following row out of vertical alignment.
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        return layout;
    }

    private static void Row(TableLayoutPanel grid, string label, Control control)
    {
        // Width is forced below for TextBox/ComboBox, which would otherwise stretch. An AutoSize
        // label must keep its own measured width or it collapses to a single column of characters.
        // An explicit AutoSize style PER ROW. Without one, rows past the end of the RowStyles
        // collection do not size to their content, so a tall control (the wrapped hint labels, the
        // checkboxes) overlapped whatever came after it at the bottom of the tab.
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowCount = grid.RowStyles.Count;

        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            // Nudged down so the caption sits on the text baseline of the control beside it.
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent,
        });

        if (control is TextBox or ComboBox)
        {
            control.Width = FieldWidth;
        }

        control.Margin = new Padding(0, 5, 0, 5);
        grid.Controls.Add(control);
    }

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage(Strings.Get("settings.general")) { BackColor = Theme.Surface, Padding = new Padding(0) };
        var grid = NewGrid();

        var pick = new Button { Text = Strings.Get("settings.pickWindow"), AutoSize = true, Height = 30 };
        pick.Click += (_, _) => PickWindow();

        var browse = new Button { Text = Strings.Get("settings.browseExe"), AutoSize = true, Height = 30 };
        browse.Click += (_, _) => BrowseForExecutable();

        var targetButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 8) };
        targetButtons.Controls.AddRange([pick, browse]);

        var hint = new Label
        {
            Text = "Pick the running game window, or its .exe. Loadstar stores the process name — "
                + "matching on window title once selected a browser tab that mentioned the game.",
            AutoSize = false,
            Height = 52,
            Width = FieldWidth,
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
        };

        // "Build" alone reads as a compiled build. This is a questlog character loadout.
        Row(grid, Strings.Get("settings.buildUrl"), _buildUrl);
        Row(grid, Strings.Get("settings.process"), _process);
        Row(grid, string.Empty, targetButtons);
        Row(grid, string.Empty, hint);
        var editHotkey = new Button { Text = Strings.Get("settings.editHotkey"), AutoSize = true, Height = 30 };
        editHotkey.Click += (_, _) =>
        {
            using var recorder = new HotkeyRecorderDialog(_hotkey.Text);

            if (recorder.ShowDialog(this) == DialogResult.OK && recorder.Recorded is { } recorded)
            {
                _hotkey.Text = recorded.Display;
            }
        };

        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _hotkey.Margin = new Padding(0, 3, 8, 0);
        hotkeyRow.Controls.AddRange([_hotkey, editHotkey]);

        Row(grid, Strings.Get("settings.hotkey"), hotkeyRow);

        _provider.SelectedIndexChanged += (_, _) => OnProviderChanged();

        var refreshModels = new Button { Text = "Refresh", AutoSize = true, Height = 30 };
        refreshModels.Click += (_, _) => _ = RefreshModelsAsync();

        var modelRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _model.Margin = new Padding(0, 3, 8, 0);
        modelRow.Controls.AddRange([_model, refreshModels, _modelHint]);

        _model.TextChanged += (_, _) => UpdateModelHint();

        Row(grid, "AI provider", _provider);
        Row(grid, Strings.Get("settings.model"), modelRow);
        Row(grid, Strings.Get("settings.apiKey"), _apiKey);
        Row(grid, string.Empty, _billing);
        Row(grid, Strings.Get("settings.language"), _language);
        Row(grid, string.Empty, _consent);
        Row(grid, string.Empty, _status);

        page.Controls.Add(grid);
        return page;
    }

    /// <summary>
    /// Swaps the model list, key field and billing note to match the newly selected provider.
    ///
    /// <para>Clearing the key box is the important part. Each provider has its own stored key, so
    /// leaving a half-typed Anthropic key visible after switching to Gemini invites saving it into
    /// the wrong slot — where it would fail authentication against a provider that never issued
    /// it, with nothing on screen to explain why.</para>
    /// </summary>
    private void OnProviderChanged()
    {
        if (_loadingProvider || _provider.SelectedItem is not ProviderChoice choice)
        {
            return;
        }

        var info = AiCatalog.For(choice.Kind);

        _model.Items.Clear();

        foreach (var model in AiCatalog.UsableModels(choice.Kind))
        {
            _model.Items.Add(model);
        }

        _model.Text = _modelChoices.TryGetValue(choice.Kind, out var remembered)
            ? remembered
            : info.DefaultModel;

        _apiKey.Clear();
        _apiKey.PlaceholderText = _secrets.HasKey(choice.Kind)
            ? "(stored — type to replace)"
            : info.KeyPlaceholder;

        // Explicit line breaks rather than trusting the wrap: the URL must not be split across two
        // lines, since a half-shown URL is worse than none — it looks like the whole address.
        var seedWarning = info.SeedIsAuthoritative
            ? string.Empty
            : $"{Environment.NewLine}Press Refresh for this provider's current model list.";

        _billing.Text = $"{info.BillingNote}{Environment.NewLine}Keys: {info.ConsoleUrl}{seedWarning}";
        _billing.ForeColor = Theme.SubtleText;

        UpdateModelHint();
    }

    /// <summary>
    /// Shows what the selected model costs, or says plainly that we don't know.
    ///
    /// <para>Blank would read as free. An unpriced model is the normal case for anything typed in
    /// or pulled from Refresh that the bundled catalogue predates, and saying so is the same choice
    /// the cost estimate makes when it returns null rather than zero.</para>
    /// </summary>
    private void UpdateModelHint()
    {
        if (_provider.SelectedItem is not ProviderChoice choice)
        {
            return;
        }

        var model = AiCatalog.FindModel(choice.Kind, _model.Text.Trim());

        _modelHint.Text = model is null ? "price unknown" : model.Describe();
        _modelHint.ForeColor = Theme.SubtleText;
    }

    /// <summary>
    /// Replaces the model list with what the provider itself reports.
    ///
    /// <para>Same shape as <see cref="LoadServersAsync"/>, and for the same reason: a bundled list of
    /// third-party identifiers is wrong on a schedule nobody here controls. Failure keeps the
    /// existing list rather than emptying it — being offline should not cost the user their
    /// configured model.</para>
    /// </summary>
    private async Task RefreshModelsAsync()
    {
        if (_provider.SelectedItem is not ProviderChoice choice)
        {
            return;
        }

        // A key typed but not yet saved should work here, otherwise the first thing a new user tries
        // fails and the fix ("save, reopen, then press Refresh") is not discoverable.
        var key = string.IsNullOrWhiteSpace(_apiKey.Text)
            ? _secrets.Resolve(choice.Kind)
            : _apiKey.Text.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            _status.Text = $"Enter an API key for {AiCatalog.For(choice.Kind).DisplayName} first.";
            return;
        }

        _status.Text = "Fetching models…";

        try
        {
            var models = await ModelDirectory.ListAsync(choice.Kind, key, CancellationToken.None);

            if (models.Count == 0)
            {
                _status.Text = "That provider returned no models we carry pricing for. Keeping the existing list.";
                return;
            }

            // Preserve the current selection across the swap: the point is to widen the choice, not
            // to silently move the user onto a different model.
            var selected = _model.Text;

            _model.Items.Clear();

            foreach (var model in models)
            {
                _model.Items.Add(model);
            }

            _model.Text = selected;
            _status.Text = $"{models.Count} models from {AiCatalog.For(choice.Kind).DisplayName}.";
        }
        catch (AiProviderException ex)
        {
            _status.Text = ex.Message;
        }
    }

    private TabPage BuildBossTimerTab()
    {
        var page = new TabPage(Strings.Get("settings.bossTimer")) { BackColor = Theme.Surface, Padding = new Padding(0) };
        var grid = NewGrid();

        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            _timezone.Items.Add(zone.Id);
        }

        // Selecting a server sets the region, which is what actually picks the schedule.
        _server.SelectedIndexChanged += (_, _) => OnServerChanged();

        var refresh = new Button { Text = Strings.Get("settings.refreshServers"), AutoSize = true, Height = 30, Margin = new Padding(0, 0, 0, 8) };
        refresh.Click += async (_, _) => await LoadServersAsync();

        var coverage = new Label
        {
            Text =
                "Times are computed locally from a bundled weekly schedule — no scraping, works "
                + "offline. Only the Americas table is captured so far; other regions show nothing "
                + "until their data is filled in.",
            AutoSize = false,
            Height = 50,
            Width = FieldWidth,
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
        };

        var alertsHint = new Label
        {
            Text = "Comma-separated, e.g. 15, 5. An alert that fires as the boss spawns is useless — "
                + "travel time is the point.",
            AutoSize = false,
            Height = 34,
            Width = FieldWidth,
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
        };

        var overlayHint = new Label
        {
            Text = "The countdown starts movable — drag it where you want, then use "
                + "\"Lock countdown position\" in the tray menu to make it click-through.",
            AutoSize = false,
            Height = 34,
            Width = FieldWidth,
            ForeColor = Theme.SubtleText,
            BackColor = Color.Transparent,
        };

        Row(grid, Strings.Get("settings.server"), _server);
        Row(grid, string.Empty, refresh);
        Row(grid, Strings.Get("settings.region"), _regionLabel);
        Row(grid, Strings.Get("settings.timezone"), _timezone);
        Row(grid, string.Empty, coverage);
        Row(grid, Strings.Get("settings.alertMinutes"), _alertMinutes);
        Row(grid, string.Empty, alertsHint);
        Row(grid, string.Empty, _bossAlerts);
        Row(grid, string.Empty, _bossOverlay);
        Row(grid, string.Empty, overlayHint);

        page.Controls.Add(grid);
        return page;
    }

    /// <summary>
    /// Fetches the live server list. Servers are added and merged over time, so a hardcoded list
    /// would eventually offer one that no longer exists.
    /// </summary>
    /// <summary>
    /// Fetches the server list in the background and merges it into the combo.
    ///
    /// <para><b>This must not restore stored values.</b> It once ended by calling
    /// <see cref="RestoreStoredValues"/>, to undo the damage that repopulating the combo does. But
    /// this method completes seconds after the dialog opened — long after the user started typing —
    /// and re-applying saved settings at that moment silently reverted whatever they had just
    /// changed. A consent checkbox ticked during the fetch un-ticked itself when it landed, which
    /// reads as a dead control rather than as a race.</para>
    ///
    /// <para>So the blast radius is now the server list and nothing else: the handler is suppressed
    /// while the items are replaced, and the region label is refreshed directly. Every other control
    /// belongs to the user from the moment the dialog is visible.</para>
    /// </summary>
    private async Task LoadServersAsync()
    {
        var previous = _store.Load().Game.ServerName;

        // Before the await, not after: a fetch with no feedback looks like nothing happened, and
        // then a failure message arrives out of nowhere and appears to belong to whatever the user
        // clicked in the meantime.
        _status.Text = "Loading the server list…";
        _status.ForeColor = Theme.SubtleText;

        try
        {
            // Short enough that failing offline is quick news. The old 20s meant the user sat with
            // no answer, did something else, and got the bad news attached to that instead.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var servers = await new QuestlogClient(http).GetServersAsync(CancellationToken.None);

            _loadingServers = true;

            try
            {
                _server.Items.Clear();

                foreach (var server in servers.OrderBy(s => s.RegionSlug).ThenBy(s => s.Name))
                {
                    _server.Items.Add(server);
                }

                // Whatever is in the combo now wins over the stored name, because the user may have
                // picked a different server while this was in flight.
                _server.SelectedItem =
                    servers.FirstOrDefault(s => s.Name.Equals(_server.Text, StringComparison.OrdinalIgnoreCase))
                    ?? servers.FirstOrDefault(s => s.Name.Equals(previous, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _loadingServers = false;
            }

            if (_server.SelectedItem is GameServer selected)
            {
                _regionLabel.Text = selected.RegionSlug;
            }

            _status.Text = $"{servers.Count} servers across {servers.Select(s => s.RegionSlug).Distinct().Count()} regions.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline is a normal state for a desktop app. Keep whatever is stored rather than
            // clearing the user's server because a fetch failed.
            _status.Text = $"Could not reach questlog for the server list ({ex.Message}). Existing choice kept.";
        }
    }

    private void OnServerChanged()
    {
        if (_loadingServers || _server.SelectedItem is not GameServer server)
        {
            return;
        }

        _regionLabel.Text = server.RegionSlug;

        var suggested = BossSchedule.LoadBundled().DefaultTimeZone(server.RegionSlug);

        if (!string.IsNullOrWhiteSpace(suggested) && string.IsNullOrWhiteSpace(_timezone.Text))
        {
            // A suggestion, not a fact: servers within a region do not all share a timezone.
            _timezone.Text = suggested;
        }
    }

    private void Populate()
    {
        var settings = _store.Load();

        _buildUrl.Text = settings.Game.BuildUrl ?? string.Empty;
        _buildUrl.PlaceholderText = "https://questlog.gg/throne-and-liberty/en/character-builder/…";
        _hotkey.Text = settings.Overlay.CaptureHotkey;
        _consent.Checked = settings.CaptureConsentGiven;

        // Remember every provider's model before touching the combo, so switching provider and back
        // returns to what the user actually chose rather than to that provider's default.
        foreach (var (kind, model) in settings.Ai.ModelByProvider)
        {
            if (Enum.TryParse<AiProviderKind>(kind, ignoreCase: true, out var parsed))
            {
                _modelChoices[parsed] = model;
            }
        }

        _modelChoices[settings.Ai.Provider] = settings.Ai.Model;

        _loadingProvider = true;

        foreach (var info in AiCatalog.All)
        {
            _provider.Items.Add(new ProviderChoice(info.Kind));
        }

        _provider.SelectedItem = _provider.Items.Cast<ProviderChoice>()
            .FirstOrDefault(c => c.Kind == settings.Ai.Provider);

        _loadingProvider = false;

        // Fills the model list, key placeholder and billing note for the selected provider. Called
        // rather than left to the event, because assigning SelectedItem above happened while the
        // handler was suppressed.
        OnProviderChanged();

        // Each language listed in its own script, so someone who needs it can find it.
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            _language.Items.Add(new LanguageChoice(language));
        }

        _language.SelectedItem = _language.Items.Cast<LanguageChoice>()
            .FirstOrDefault(c => c.Language == settings.Language);

        foreach (var window in GameWindowLocator.ListVisibleWindows())
        {
            if (!_process.Items.Contains(window.ProcessName))
            {
                _process.Items.Add(window.ProcessName);
            }
        }

        // Set Text AFTER populating: adding items to a ComboBox resets Text, so assigning it first
        // silently blanked the stored process name and the field came up empty despite TL being saved.
        _process.Text = settings.Capture.WindowProcessName ?? string.Empty;

        _regionLabel.Text = settings.Game.Region;
        _regionLabel.ForeColor = Theme.SubtleText;
        _timezone.Text = settings.Game.ServerTimeZone;

        if (!string.IsNullOrWhiteSpace(settings.Game.ServerName))
        {
            // Show the stored name immediately; the live list replaces it once fetched.
            _server.Items.Add(new GameServer(settings.Game.ServerName, settings.Game.Region, "unknown"));
            _server.SelectedIndex = 0;
        }

        _ = LoadServersAsync();
        _alertMinutes.Text = string.Join(", ", settings.Game.BossAlertMinutes);
        _alertMinutes.PlaceholderText = "15, 5";
        _bossAlerts.Checked = settings.Game.BossAlertsEnabled;
        _bossOverlay.Checked = settings.Overlay.ShowBossCountdown;

        // The key field's placeholder is set by OnProviderChanged, since whether a key is stored is
        // a per-provider fact. Nothing here ever displays a stored key: showing it adds nothing and
        // puts a secret on screen.

        _status.Text = $"Settings file: {_store.FilePath}";
        _status.ForeColor = Theme.SubtleText;
    }

    private void PickWindow()
    {
        var windows = GameWindowLocator.ListVisibleWindows();

        using var picker = new WindowPicker(windows);

        if (picker.ShowDialog(this) == DialogResult.OK && picker.Selected is { } chosen)
        {
            // Store the process, not the title: titles change as the player moves through the game.
            _process.Text = chosen.ProcessName;
            _status.Text = $"Targeting process \"{chosen.ProcessName}\" (was showing \"{chosen.Title}\").";
        }
    }

    private void BrowseForExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the game executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _process.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            _status.Text = $"Targeting process \"{_process.Text}\".";
        }
    }

    private bool Persist()
    {
        var hotkey = Hotkey.TryParse(_hotkey.Text);

        if (hotkey is null)
        {
            MessageBox.Show(
                this,
                "That hotkey could not be parsed. Use something like Ctrl+Alt+S — at least one " +
                "modifier is required, otherwise the key would be captured globally in every " +
                "application.",
                "Loadstar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }

        var alerts = ParseAlertMinutes(_alertMinutes.Text);
        var settings = _store.Load();

        var language = (_language.SelectedItem as LanguageChoice)?.Language ?? settings.Language;
        var languageChanged = language != settings.Language;

        var provider = (_provider.SelectedItem as ProviderChoice)?.Kind ?? settings.Ai.Provider;

        // Never save a blank model — a combo box emptied by a handle recreation would otherwise
        // wipe a deliberate choice, the same way the process name once did.
        var model = string.IsNullOrWhiteSpace(_model.Text)
            ? AiProviderFactory.ResolveModel(provider, settings.Ai.Model)
            : _model.Text.Trim();

        _modelChoices[provider] = model;

        _store.Save(settings with
        {
            Language = language,
            CaptureConsentGiven = _consent.Checked,
            ConsentVersionAccepted = _consent.Checked ? ConsentPrompt.CurrentVersion : null,
            Game = settings.Game with
            {
                BuildUrl = string.IsNullOrWhiteSpace(_buildUrl.Text) ? null : _buildUrl.Text.Trim(),
                ServerName = (_server.SelectedItem as GameServer)?.Name ?? settings.Game.ServerName,
                Region = (_server.SelectedItem as GameServer)?.RegionSlug ?? settings.Game.Region,
                ServerTimeZone = string.IsNullOrWhiteSpace(_timezone.Text) ? settings.Game.ServerTimeZone : _timezone.Text.Trim(),
                BossAlertMinutes = alerts,
                BossAlertsEnabled = _bossAlerts.Checked,
            },
            Capture = settings.Capture with
            {
                // Never overwrite a stored process name with a blank. A rendering bug once left
                // this field empty on load, and saving then wiped a perfectly good setting — a
                // display fault turning into data loss.
                WindowProcessName = string.IsNullOrWhiteSpace(_process.Text)
                    ? settings.Capture.WindowProcessName
                    : _process.Text.Trim(),
            },
            Overlay = settings.Overlay with
            {
                CaptureHotkey = hotkey.Display,
                ShowBossCountdown = _bossOverlay.Checked,
            },
            Ai = settings.Ai with
            {
                Provider = provider,
                Model = model,
                ModelByProvider = _modelChoices.ToDictionary(
                    pair => pair.Key.ToString(),
                    pair => pair.Value),
            },
        });

        if (!string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            // Into the selected provider's slot, never a shared one. Saving a Gemini key over an
            // Anthropic one would fail authentication later with nothing on screen explaining why.
            _secrets.Save(provider, _apiKey.Text.Trim());
        }

        if (languageChanged)
        {
            Strings.Use(language);
            OfferRestartForLanguage();
        }

        return true;
    }

    /// <summary>
    /// Offers to restart now, which is what actually applies a new interface language.
    ///
    /// <para>Asking rather than restarting outright: the app may be mid-session with advice on screen
    /// that only exists in memory, and taking that away without warning to change a label is a poor
    /// trade. Declining is a real choice, so the message says what declining means.</para>
    ///
    /// <para>The language is already saved by the time this runs, so every path here ends with the
    /// setting persisted — the only question is when the windows catch up.</para>
    /// </summary>
    private void OfferRestartForLanguage()
    {
        var answer = MessageBox.Show(
            this,
            Strings.Get("settings.language.restart"),
            "Loadstar",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        if (!Program.Restart())
        {
            MessageBox.Show(
                this,
                Strings.Get("settings.language.restartFailed"),
                "Loadstar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>Wraps a language for the picker so it shows its own name rather than the enum.</summary>
    private sealed record LanguageChoice(AppLanguage Language)
    {
        public override string ToString() => AppLanguages.NativeName(Language);
    }

    private sealed record ProviderChoice(AiProviderKind Kind)
    {
        public override string ToString() => AiCatalog.For(Kind).DisplayName;
    }

    /// <summary>
    /// Parses "15, 5" into alert offsets. Malformed entries are dropped rather than rejected — a
    /// stray comma should not block saving every other setting on the page.
    /// </summary>
    private static IReadOnlyList<int> ParseAlertMinutes(string text) =>
        text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : -1)
            .Where(value => value is > 0 and <= 240)
            .Distinct()
            .OrderByDescending(value => value)
            .ToArray();
}

/// <summary>A list of running windows to choose from, so the target never has to be typed.</summary>
internal sealed class WindowPicker : ThemedForm
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public GameWindow? Selected => _list.SelectedItem as GameWindow;

    public WindowPicker(IReadOnlyList<GameWindow> windows)
    {
        Text = "Pick the game window";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 420);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;

        _list.Format += (_, e) =>
        {
            if (e.ListItem is GameWindow window)
            {
                e.Value = $"[{window.ProcessName}]   {window.Title}";
            }
        };

        foreach (var window in windows)
        {
            _list.Items.Add(window);
        }

        var ok = new Button { Text = "Select", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        Theme.MakePrimary(ok);
        Theme.MakeSecondary(cancel);

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 4) };
        frame.Controls.Add(_list);

        Controls.Add(frame);
        Controls.Add(CreateActionBar(ok, cancel));
        Controls.Add(CreateHeading("Running windows"));

        AcceptButton = ok;
        CancelButton = cancel;

        _list.DoubleClick += (_, _) => DialogResult = DialogResult.OK;
    }
}

