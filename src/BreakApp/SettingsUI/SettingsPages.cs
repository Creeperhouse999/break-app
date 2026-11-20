using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreakApp.Core.Commitment;
using BreakApp.Core.History;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Notifications;
using Screen = System.Windows.Forms.Screen;

namespace BreakApp.SettingsUI;

/// <summary>Builds every settings page. Each row registers its setting path for search and the coverage check.</summary>
public static class SettingsPages
{
    public static readonly (string Name, string Icon)[] Categories =
    [
        ("Rules", "\uE916"), ("Warnings", "\uEA8F"), ("Lock & Commitment", "\uE72E"), ("Pausing & Meetings", "\uE769"),
        ("Activity & Idle", "\uE945"), ("Appearance", "\uE790"), ("Texts", "\uE8D2"), ("Sounds", "\uE767"),
        ("Stats", "\uE9D2"), ("General", "\uE713"),
    ];

    public static StackPanel Build(string category, SettingsContext ctx, string? sub) => category switch
    {
        "Rules" => sub is not null && ctx.Edit.Rules.FirstOrDefault(r => r.Id.ToString() == sub) is { } rule ? RuleEditor(ctx, rule) : Rules(ctx),
        "Warnings" => Warnings(ctx),
        "Lock & Commitment" => Lock(ctx),
        "Pausing & Meetings" => Pausing(ctx),
        "Activity & Idle" => Activity(ctx),
        "Appearance" => Appearance(ctx),
        "Texts" => Texts(ctx),
        "Sounds" => Sounds(ctx),
        "Stats" => Stats(ctx),
        _ => General(ctx),
    };

    // ------------------------------------------------------------------ labels
    private static readonly (TimerMode, string)[] TimerModes =
        [(TimerMode.ActiveTime, "Active time"), (TimerMode.ClockTime, "Clock time"), (TimerMode.ClockAligned, "Fixed clock times")];
    private static readonly (BreakStyle, string)[] Styles =
        [(BreakStyle.Notification, "Notification"), (BreakStyle.Window, "Window"), (BreakStyle.Fullscreen, "Fullscreen")];
    private static readonly (LockLevel, string)[] Locks =
        [(LockLevel.Skippable, "Skippable"), (LockLevel.LockedWithKeybind, "Locked (escape key)"), (LockLevel.Total, "TOTAL LOCK")];
    private static readonly (FlashStyle, string)[] FlashStyles =
        [(FlashStyle.Glow, "Glow"), (FlashStyle.Pulse, "Pulse (grow/shrink)"), (FlashStyle.GlowAndPulse, "Glow + pulse")];
    private static readonly (Accent, string)[] Accents =
        [(Accent.Blue, "Blue"), (Accent.Indigo, "Indigo"), (Accent.Teal, "Teal"), (Accent.Violet, "Violet"), (Accent.Rose, "Rose")];
    private static readonly (ThemeMode, string)[] Themes = [(ThemeMode.Auto, "Follow Windows"), (ThemeMode.Light, "Light"), (ThemeMode.Dark, "Dark")];

    private static string Pct(double v) => $"{Math.Round(v * 100)} %";

    public static string Summary(BreakRule r) =>
        $"every {Format.Duration(r.Interval)} · {Format.Duration(r.Duration)} · {Styles.First(s => s.Item1 == r.Style).Item2}" +
        (r.Lock == LockLevel.Skippable ? "" : $" · {Locks.First(l => l.Item1 == r.Lock).Item2}");

    // ------------------------------------------------------------------ Rules
    private static StackPanel Rules(SettingsContext ctx)
    {
        var b = new PageBuilder(ctx, "Rules");
        b.Intro("Each rule is \"every X, take a break for Y\". Add as many as you like; each has its own style, warning and lock.");
        b.Group("Your rules");
        var rules = ctx.Edit.Rules;
        if (rules.Count == 0)
            b.Add(new TextBlock { Text = "No rules yet. Add one below.", Margin = new Thickness(0, 12, 0, 12) }.WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
        for (var i = 0; i < rules.Count; i++) b.Add(RuleCard(ctx, rules[i], i));

        var add = b.Button("+ Add rule", () => { }, "PrimaryButton");
        var menu = new ContextMenu();
        foreach (var (name, create) in RuleTemplates.All)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) =>
            {
                var r = create();
                r.Order = rules.Count;
                if (rules.Any(x => x.Name == r.Name)) r.Name += $" {rules.Count + 1}";
                rules.Add(r);
                ctx.MarkDirty();
                ctx.Navigate("Rules", r.Id.ToString());
            };
            menu.Items.Add(item);
        }
        add.ContextMenu = menu;
        add.Click += (_, _) => { menu.PlacementTarget = add; menu.IsOpen = true; };

        b.Group("When rules overlap");
        b.Choice("Collision", "Overlapping breaks", "What happens when two rules are due at about the same time.",
            () => ctx.Edit.Collision, v => ctx.Edit.Collision = v,
            [(CollisionPolicy.BiggerWins, "Bigger break wins"), (CollisionPolicy.Sequential, "Run both, shortest first"), (CollisionPolicy.ResetAll, "Any break resets all timers")],
            tip: "Bigger wins: the 20 s eye rest is skipped when the 5 min stretch happens at the same time. Reset all: after any break every timer starts from zero.");
        b.Duration("CollisionWindow", "Overlap window", "Rules due within this time of each other are treated as one.",
            () => ctx.Edit.CollisionWindow, v => ctx.Edit.CollisionWindow = v, TimeSpan.Zero, TimeSpan.FromMinutes(10), advanced: true,
            tip: "Example: 1 min — if the eye rest is due 40 s after the stretch starts, it is merged into the stretch.");
        b.AddLoose(PageBuilder.Buttons(add));
        return b.Panel;
    }

    private static FrameworkElement RuleCard(SettingsContext ctx, BreakRule r, int index)
    {
        var rules = ctx.Edit.Rules;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var enabled = new CheckBox { IsChecked = r.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Enabled" };
        enabled.SetResourceReference(FrameworkElement.StyleProperty, "Switch");
        enabled.Click += (_, _) => { r.Enabled = enabled.IsChecked == true; ctx.MarkDirty(); };
        grid.Children.Add(enabled);

        var text = new StackPanel { Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = r.Name, FontSize = 14, FontWeight = FontWeights.SemiBold }.WithRef(TextBlock.ForegroundProperty, "Text1Brush"));
        text.Children.Add(new TextBlock { Text = Summary(r), FontSize = 12 }.WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Button Icon(string glyph, string tip, Action a, bool enabledBtn = true)
        {
            var btn = new Button { Content = glyph, ToolTip = tip, IsEnabled = enabledBtn };
            btn.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
            btn.Click += (_, _) => a();
            return btn;
        }
        var edit = new Button { Content = "Edit", Margin = new Thickness(0, 0, 6, 0) };
        edit.Click += (_, _) => ctx.Navigate("Rules", r.Id.ToString());
        actions.Children.Add(edit);
        actions.Children.Add(Icon("\uE768", "Preview this break (10 s, never locks)", () => Preview(ctx, r)));
        actions.Children.Add(Icon("\uE74A", "Move up", () => { Swap(rules, index, index - 1); ctx.MarkDirty(); ctx.Rebuild(); }, index > 0));
        actions.Children.Add(Icon("\uE74B", "Move down", () => { Swap(rules, index, index + 1); ctx.MarkDirty(); ctx.Rebuild(); }, index < rules.Count - 1));
        actions.Children.Add(Icon("\uE8C8", "Duplicate", () =>
        {
            var copy = SettingsStore.CloneRule(r);
            copy.Id = Guid.NewGuid();
            copy.Name = r.Name + " copy";
            rules.Insert(index + 1, copy);
            ctx.MarkDirty();
            ctx.Rebuild();
        }));
        actions.Children.Add(Icon("\uE74D", "Delete", () =>
        {
            if (MessageBox.Show($"Delete the rule '{r.Name}'?", "Delete rule", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            rules.Remove(r);
            ctx.MarkDirty();
            ctx.Rebuild();
        }));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        grid.Margin = new Thickness(0, 12, 0, 12);
        return grid;
    }

    private static void Swap(List<BreakRule> list, int a, int b)
    {
        (list[a], list[b]) = (list[b], list[a]);
        for (var i = 0; i < list.Count; i++) list[i].Order = i;
    }

    private static void Preview(SettingsContext ctx, BreakRule r)
    {
        var copy = SettingsStore.CloneRule(r);
        copy.Duration = TimeSpan.FromSeconds(Math.Min(10, r.Duration.TotalSeconds));
        ctx.C.Presenter.Preview(copy, ctx.Edit);
    }

    private static StackPanel RuleEditor(SettingsContext ctx, BreakRule r)
    {
        var b = new PageBuilder(ctx, "Rules", r.Id.ToString());
        var back = b.Button("← All rules", () => ctx.Navigate("Rules", null), "GhostButton");
        var preview = b.Button("Preview (10 s)", () => Preview(ctx, r));
        b.AddLoose(PageBuilder.Buttons(back, preview));
        BuildRuleRows(b, ctx, r, "Rules[].");
        return b.Panel;
    }

    /// <summary>All rule settings. Also used once (for the first rule) to index rule settings for search.</summary>
    public static void BuildRuleRows(PageBuilder b, SettingsContext ctx, BreakRule r, string p)
    {
        b.Group("Basics");
        b.Text(p + "Name", "Name", "Shown on the break screen and in notifications.", () => r.Name, v => r.Name = v);
        b.Toggle(p + "Enabled", "Enabled", "Turn this rule on or off without deleting it.", () => r.Enabled, v => r.Enabled = v);
        b.Duration(p + "Interval", "Every", "How often this break comes.", () => r.Interval, v => r.Interval = v,
            TimeSpan.FromSeconds(10), TimeSpan.FromHours(24), tip: "Example: 20 minutes → a break every 20 minutes of (active) time.");
        b.Duration(p + "Duration", "Break length", "How long the break lasts.", () => r.Duration, v => r.Duration = v,
            TimeSpan.FromSeconds(5), TimeSpan.FromHours(4));
        b.Choice(p + "TimerMode", "Timer counts", "What time counts toward the next break.", () => r.TimerMode, v => r.TimerMode = v, TimerModes,
            tip: "Active time: only while you use the PC (time away resets it). Clock time: always counts. Fixed clock times: at :00, :20, :40… from midnight.");

        b.Group("How it looks");
        b.Choice(p + "Style", "Break style", "Notification card, a window, or fullscreen on every monitor.", () => r.Style, v => r.Style = v, Styles);
        b.Toggle(p + "Flash.Enabled", "Flash to get attention", "The notification flashes and/or pulses so you notice it.", () => r.Flash.Enabled, v => r.Flash.Enabled = v);
        b.Choice(p + "Flash.Style", "Flash style", "Glow blinks a colour; pulse makes the card grow and shrink.", () => r.Flash.Style, v => r.Flash.Style = v, FlashStyles);
        b.Slider(p + "Flash.PulseAmount", "Pulse size", "How much bigger the card gets when pulsing.", () => r.Flash.PulseAmount, v => r.Flash.PulseAmount = Math.Round(v),
            1, 10, v => $"{Math.Round(v)} %");
        b.Slider(p + "Flash.SpeedHz", "Flash speed", "Flashes per second.", () => r.Flash.SpeedHz, v => r.Flash.SpeedHz = Math.Round(v, 1), 0.5, 6, v => $"{v:0.0}/s");
        b.Number(p + "Flash.Count", "Number of flashes", "0 = keep flashing until the notification closes.", () => r.Flash.Count, v => r.Flash.Count = (int)v, 0, 100);
        b.Color(p + "Flash.Color", "Flash colour", "Leave on Theme to use the accent colour.", () => r.Flash.Color, v => r.Flash.Color = v, emptyLabel: "Accent");

        b.Group("Lock");
        b.Choice(p + "Lock", "Lock level", "Window and fullscreen breaks only. Notifications can't lock.", () => r.Lock, v => r.Lock = v, Locks,
            tip: "Skippable: Skip/Postpone buttons. Locked: keyboard and mouse blocked, your escape key combo ends it. TOTAL LOCK: no way out until the timer ends (Ctrl+Alt+Del still works — Windows never allows blocking it).",
            confirm: v => v != LockLevel.Total || SettingsWindow.ConfirmDanger(
                "Enable TOTAL LOCK?",
                $"ARE YOU SURE?\n\nDuring '{r.Name}' you will not be able to use your PC at all until the {Format.Duration(r.Duration)} break ends. There is no button and no key to get out.",
                "I understand I can't end this break early"));
        b.Toggle(p + "EndOnMeeting", "End break when a meeting starts", "A call, quiet time or manual pause ends a running break (never a TOTAL LOCK).",
            () => r.EndOnMeeting, v => r.EndOnMeeting = v, advanced: true);

        b.Group("Skip & postpone");
        b.Toggle(p + "AllowSkip", "Allow skipping", "Show a Skip button (Skippable lock only).", () => r.AllowSkip, v => r.AllowSkip = v);
        b.Toggle(p + "AllowPostpone", "Allow postponing", "Show a Postpone button.", () => r.AllowPostpone, v => r.AllowPostpone = v);
        b.Duration(p + "PostponeLength", "Postpone by", "How long Postpone delays the break.", () => r.PostponeLength, v => r.PostponeLength = v,
            TimeSpan.FromSeconds(30), TimeSpan.FromHours(2));
        b.Number(p + "MaxPostpones", "Max postpones in a row", "After this many, the break can't be postponed again.", () => r.MaxPostpones, v => r.MaxPostpones = (int)v, 0, 20);

        b.Group("Warning before the break");
        WarningRows(b, r, p);

        b.Group("Messages");
        b.Lines(p + "Messages", "Break messages", "One per line. Shown on the break screen (ideas, reminders…).", () => r.Messages, v => r.Messages = v);
        b.Choice(p + "MessageOrder", "Order", "Pick a random message each time, or go through them in order.", () => r.MessageOrder, v => r.MessageOrder = v,
            [(MessageOrder.Random, "Random"), (MessageOrder.Sequential, "In order")]);

        b.Group("Sounds");
        b.Sound(p + "StartSound", "Break start sound", "Played when the break begins.", () => r.StartSound, v => r.StartSound = v);
        b.Sound(p + "EndSound", "Break end sound", "Played when the break is over.", () => r.EndSound, v => r.EndSound = v);

        b.Group("Schedule", ctx.Advanced ? "Only run this rule at certain times. Leave empty to run it always." : null);
        var windows = new TimeWindowsEditor(r.ActiveWindows, ctx.MarkDirty, "Add active time");
        b.Row(p + "ActiveWindows", "Active hours", "e.g. weekdays 09:00–17:00 only. Empty = always.", windows, advanced: true, wideControl: true,
            tip: "Outside these windows the rule pauses and doesn't count.");

        b.Group("Advanced");
        b.Choice<Accent?>(p + "Appearance.Accent", "Accent colour", "Override the app accent for this rule.", () => r.Appearance?.Accent,
            v => (r.Appearance ??= new()).Accent = v, [(null, "Same as app"), .. Accents.Select(a => ((Accent?)a.Item1, a.Item2))], advanced: true);
        b.Choice<ThemeMode?>(p + "Appearance.Theme", "Theme", "Override light/dark for this rule's break.", () => r.Appearance?.Theme,
            v => (r.Appearance ??= new()).Theme = v, [(null, "Same as app"), .. Themes.Select(t => ((ThemeMode?)t.Item1, t.Item2))], advanced: true);
        b.Choice<double?>(p + "Appearance.Opacity", "Fullscreen opacity", "Override how see-through the fullscreen break is.", () => r.Appearance?.Opacity,
            v => (r.Appearance ??= new()).Opacity = v, [(null, "Same as app"), (0.6, "60 %"), (0.75, "75 %"), (0.85, "85 %"), (0.95, "95 %"), (1.0, "100 % (solid)")], advanced: true);
        b.File(p + "Appearance.BackgroundImage", "Background image", "Picture behind this rule's fullscreen break.", () => r.Appearance?.BackgroundImage,
            v => (r.Appearance ??= new()).BackgroundImage = v, "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*", advanced: true);
        b.Choice<bool?>(p + "CommitmentOverride", "Commitment mode", "Use the global setting or force it for this rule.", () => r.CommitmentOverride,
            v => r.CommitmentOverride = v, [(null, "Use global setting"), (true, "Always on"), (false, "Always off")], advanced: true,
            tip: "Commitment mode stops you from skipping/pausing/quitting shortly before a strict rule (no skip or TOTAL LOCK).");
    }

    private static void WarningRows(PageBuilder b, BreakRule r, string p)
    {
        b.Toggle(p + "Warning.ShowNotification", "Show a warning notification", "A notification card before the break starts.",
            () => r.Warning.ShowNotification, v => r.Warning.ShowNotification = v);
        b.Toggle(p + "Warning.PlaySound", "Play a warning sound", "Uses the warning sound from Sounds.", () => r.Warning.PlaySound, v => r.Warning.PlaySound = v);
        b.Duration(p + "Warning.LeadTime", "Warn this long before", "0 = no warning.", () => r.Warning.LeadTime, v => r.Warning.LeadTime = v,
            TimeSpan.Zero, TimeSpan.FromMinutes(10));
        b.Toggle(p + "Warning.ShowStartNowButton", "\"Start now\" button", "Start the break right away from the warning.",
            () => r.Warning.ShowStartNowButton, v => r.Warning.ShowStartNowButton = v);
        b.Toggle(p + "Warning.ShowPostponeButton", "\"Postpone\" button", "Only shown when postponing is allowed.",
            () => r.Warning.ShowPostponeButton, v => r.Warning.ShowPostponeButton = v);
        b.Toggle(p + "Warning.ShowSkipNextButton", "\"Skip\" button", "Skip the coming break. Only shown when skipping is allowed.",
            () => r.Warning.ShowSkipNextButton, v => r.Warning.ShowSkipNextButton = v);
    }

    // ------------------------------------------------------------------ Warnings
    private static StackPanel Warnings(SettingsContext ctx)
    {
        var b = new PageBuilder(ctx, "Warnings");
        b.Intro("A heads-up before each break. Every rule has its own warning settings.");
        foreach (var r in ctx.Edit.Rules)
        {
            b.Group(r.Name);
            WarningRows(b, r, "Rules[].");
        }
        b.Group("Sound & test");
        b.Sound("Sound.WarningSound", "Warning sound", "Played with warnings (when the rule has the sound on).", () => ctx.Edit.Sound.WarningSound, v => ctx.Edit.Sound.WarningSound = v);
        b.Add(b.Button("Test a warning", () => ctx.C.Notifications.Show(new NotificationRequest("preview-warning", NotificationKind.Info,
            Templates.Render(ctx.Edit.Texts.WarningTitle, new Dictionary<string, string> { ["rule"] = "Stretch", ["time"] = "30 s" }),
            Templates.Render(ctx.Edit.Texts.WarningBody, new Dictionary<string, string> { ["duration"] = "5 min", ["style"] = ctx.Edit.Texts.StyleFullscreen }),
            [new("now", ctx.Edit.Texts.StartNowButton, true), new("skip", ctx.Edit.Texts.SkipButton)], TimeSpan.FromSeconds(8),
            Flash: ctx.Edit.Rules.FirstOrDefault()?.Flash), appearanceOverride: ctx.Edit.Appearance)));
        return b.Panel;
    }

    // ------------------------------------------------------------------ Lock & Commitment
    private static StackPanel Lock(SettingsContext ctx)
    {
        var b = new PageBuilder(ctx, "Lock & Commitment");
        b.Group("Input lock");
        b.Hotkey("Lock.EscapeHotkey", "Escape key combo", "Ends a Locked break early. TOTAL LOCK ignores it.", () => ctx.Edit.Lock.EscapeHotkey, v => ctx.Edit.Lock.EscapeHotkey = v,
            tip: "Pick something you won't press by accident, e.g. Ctrl+Shift+Alt+B.");
        b.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Text = "The lock is best effort and needs no admin rights. Ways out that always work: Ctrl+Alt+Del → Task Manager; admin windows that were already open; UAC prompts.",
        }.WithRef(TextBlock.ForegroundProperty, "Text2Brush"));

        b.Group("Commitment mode", "Stops you from dodging strict rules (no skip or TOTAL LOCK) just before they start.");
        b.Toggle("Commitment.Enabled", "Commitment mode", "Blocks Skip next, Pause, disabling the rule and Quit shortly before a strict break.",
            () => ctx.Edit.Commitment.Enabled, v => ctx.Edit.Commitment.Enabled = v, danger: true,
            confirm: v => !v || SettingsWindow.ConfirmDanger("Turn on commitment mode?",
                "Shortly before strict breaks you won't be able to skip, pause or quit. Making strict rules weaker (and turning this off) will only take effect after the cooldown.",
                "I understand"));
        b.Duration("Commitment.Window", "Blocking window", "How long before a strict break the blocking starts.", () => ctx.Edit.Commitment.Window, v => ctx.Edit.Commitment.Window = v,
            TimeSpan.FromMinutes(1), TimeSpan.FromHours(2));
        b.Duration("Commitment.Cooldown", "Cooldown for weaker settings", "Changes that make strict rules easier wait this long before applying.",
            () => ctx.Edit.Commitment.Cooldown, v => ctx.Edit.Commitment.Cooldown = v, TimeSpan.Zero, TimeSpan.FromHours(24));
        if (ctx.C.Guard.PendingAppliesAt is DateTime at)
            b.Add(new TextBlock { Text = $"⏳ Pending changes apply at {at:HH:mm}.", FontWeight = FontWeights.SemiBold }.WithRef(TextBlock.ForegroundProperty, "WarningBrush"));
        return b.Panel;
    }

    // ------------------------------------------------------------------ Pausing & Meetings
    private static StackPanel Pausing(SettingsContext ctx)
    {
        var s = ctx.Edit.Pause;
        var b = new PageBuilder(ctx, "Pausing & Meetings");
        b.Group("Meetings & calls");
        b.Toggle("Pause.DetectMicCamera", "Pause while the mic or camera is in use", "Catches Google Meet in the browser, Zoom, Teams, Discord calls…",
            () => s.DetectMicCamera, v => s.DetectMicCamera = v, tip: "Uses Windows' privacy indicator data — the same thing that shows the mic icon in the taskbar.");
        b.Lines("Pause.MeetingTitlePatterns", "Meeting window titles", "Pause while a window title contains any of these (one per line).",
            () => s.MeetingTitlePatterns, v => s.MeetingTitlePatterns = v, advanced: true, minLines: 2);
        b.Lines("Pause.MeetingProcesses", "Pause while these apps are running", "Process names, one per line (e.g. obs64). Breaks pause as long as the app is open.",
            () => s.MeetingProcesses, v => s.MeetingProcesses = v, advanced: true, minLines: 2);

        b.Group("Exceptions");
        b.Lines("Pause.ForegroundExceptions", "Pause while these apps are in front", "Process names, one per line. Breaks wait while one of them is the active window.",
            () => s.ForegroundExceptions, v => s.ForegroundExceptions = v, minLines: 2,
            tip: "Example: obs64, Photoshop, vlc. Find a name in Task Manager → Details. Games are NOT paused unless you list them here.");
        var current = b.Button("Add the app I used last", () =>
        {
            var name = ctx.C.LastForegroundApp;
            if (name is null || s.ForegroundExceptions.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            s.ForegroundExceptions.Add(name);
            ctx.MarkDirty();
            ctx.Rebuild();
        });
        current.ToolTip = "Adds the last app (other than Break App) that was in front: " + (ctx.C.LastForegroundApp ?? "none yet");
        b.Add(current);

        b.Group("Quiet times", "No breaks during these times — lessons, meetings, gaming nights…");
        b.Row("Pause.QuietTimes", "Quiet times", "Weekly. Pick days, start, end and an optional label.",
            new TimeWindowsEditor(s.QuietTimes, ctx.MarkDirty, "Add quiet time"), wideControl: true);

        b.Group("Other");
        b.Toggle("Pause.PauseDuringFullscreen", "Pause during fullscreen apps", "Off: breaks still appear over games and videos.", () => s.PauseDuringFullscreen, v => s.PauseDuringFullscreen = v);
        b.Toggle("Pause.RespectDoNotDisturb", "Respect Windows Do Not Disturb", "Hold breaks while Do Not Disturb / Focus is on.", () => s.RespectDoNotDisturb, v => s.RespectDoNotDisturb = v);
        b.Choice("Pause.TimersDuringPause", "Timers during a pause", "What happens to break timers while paused.", () => s.TimersDuringPause, v => s.TimersDuringPause = v,
            [(PauseTimerPolicy.Freeze, "Freeze, then continue"), (PauseTimerPolicy.Reset, "Start over after the pause")]);
        return b.Panel;
    }

    // ------------------------------------------------------------------ Activity & Idle
    private static StackPanel Activity(SettingsContext ctx)
    {
        var s = ctx.Edit.Idle;
        var b = new PageBuilder(ctx, "Activity & Idle");
        b.Group("Natural breaks");
        b.Toggle("Idle.NaturalBreaks", "Count time away as a break", "If you were away at least as long as a break, that break is done.",
            () => s.NaturalBreaks, v => s.NaturalBreaks = v, tip: "Example: away 6 min → the 5 min stretch counts as taken and its timer restarts.");
        b.Duration("Idle.Threshold", "Away after", "No activity for this long = you're away.", () => s.Threshold, v => s.Threshold = v,
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(60));
        b.Group("What counts as activity");
        b.Toggle("Idle.DetectKeyboardMouse", "Keyboard and mouse", "", () => s.DetectKeyboardMouse, v => s.DetectKeyboardMouse = v);
        b.Toggle("Idle.DetectGamepad", "Game controller", "Xbox-compatible controllers, so gaming counts as active.", () => s.DetectGamepad, v => s.DetectGamepad = v);
        b.Toggle("Idle.DetectMedia", "Watching video", "Apps keeping the screen on (video players, YouTube) count as active.", () => s.DetectMedia, v => s.DetectMedia = v);
        return b.Panel;
    }

    // ------------------------------------------------------------------ Appearance
    private static StackPanel Appearance(SettingsContext ctx)
    {
        var a = ctx.Edit.Appearance;
        var b = new PageBuilder(ctx, "Appearance");
        b.Group("Theme");
        b.Choice("Appearance.Theme", "Theme", "Light, dark, or follow Windows.", () => a.Theme, v => a.Theme = v, Themes, appearance: true);
        b.Choice("Appearance.Accent", "Accent colour", "Main colour for buttons, rings and flashes.", () => a.Accent, v => a.Accent = v, Accents, appearance: true);
        b.Color("Appearance.CustomAccent", "Custom accent", "Any colour instead of the presets above.", () => a.CustomAccent, v => a.CustomAccent = v, emptyLabel: "Preset");

        b.Group("Colours");
        b.Color("Appearance.BreakBackgroundColor", "Break screen background", "Fullscreen break background.", () => a.BreakBackgroundColor, v => a.BreakBackgroundColor = v);
        b.Color("Appearance.BreakTextColor", "Break screen text", "Countdown and message colour.", () => a.BreakTextColor, v => a.BreakTextColor = v);
        b.Color("Appearance.NotificationBackgroundColor", "Notification background", "Card colour of notifications.", () => a.NotificationBackgroundColor, v => a.NotificationBackgroundColor = v);

        b.Group("Break screen");
        b.Choice("Appearance.Countdown", "Countdown style", "How the remaining time is shown.", () => a.Countdown, v => a.Countdown = v,
            [(CountdownFormat.Ring, "Ring"), (CountdownFormat.Bar, "Bar"), (CountdownFormat.MinSec, "Big numbers")], appearance: true);
        b.Slider("Appearance.Opacity", "Fullscreen opacity", "Lower = see your desktop through the break.", () => a.Opacity, v => a.Opacity = Math.Round(v, 2), 0.3, 1, Pct, appearance: true);
        b.File("Appearance.BackgroundImage", "Background image", "Picture behind fullscreen breaks.", () => a.BackgroundImage, v => a.BackgroundImage = v,
            "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*", appearance: true);
        b.Choice("Appearance.Monitors", "Show on", "Which monitors get the fullscreen break.", () => a.Monitors, v => a.Monitors = v,
            [(MonitorMode.All, "All monitors"), (MonitorMode.Primary, "Main monitor only"), (MonitorMode.Specific, "Chosen monitors")], after: ctx.Rebuild);
        var monitors = new StackPanel();
        foreach (var scr in Screen.AllScreens)
        {
            var cb = new CheckBox { Content = $"{scr.DeviceName.Replace(@"\\.\", "")} — {scr.Bounds.Width}×{scr.Bounds.Height}{(scr.Primary ? " (main)" : "")}",
                IsChecked = a.SpecificMonitors.Contains(scr.DeviceName), Margin = new Thickness(0, 3, 0, 3) };
            var name = scr.DeviceName;
            cb.Click += (_, _) =>
            {
                a.SpecificMonitors.Remove(name);
                if (cb.IsChecked == true) a.SpecificMonitors.Add(name);
                ctx.MarkDirty();
            };
            monitors.Children.Add(cb);
        }
        b.Row("Appearance.SpecificMonitors", "Chosen monitors", "Used when \"Show on\" is \"Chosen monitors\".", monitors, wideControl: true, advanced: a.Monitors != MonitorMode.Specific);

        b.Group("Notifications & text");
        b.Choice("Appearance.NotificationCorner", "Notification corner", "Where notification cards appear.", () => a.NotificationCorner, v => a.NotificationCorner = v,
            [(Corner.BottomRight, "Bottom right"), (Corner.TopRight, "Top right"), (Corner.BottomLeft, "Bottom left"), (Corner.TopLeft, "Top left")]);
        b.Slider("Appearance.FontScale", "Text size", "Scales text in notifications and breaks.", () => a.FontScale, v => a.FontScale = Math.Round(v, 2), 0.8, 1.6, Pct, appearance: true);
        b.Toggle("Appearance.ReduceMotion", "Reduce motion", "No slide, pop or flash animations.", () => a.ReduceMotion, v => a.ReduceMotion = v);

        b.Group("Preview");
        b.Add(PageBuilder.Buttons(
            b.Button("Preview notification", () => ctx.C.Notifications.Show(new NotificationRequest("preview-notification", NotificationKind.Warning,
                "Eye rest", "Look at something 6 metres away.", [new("skip", ctx.Edit.Texts.SkipButton)], TimeSpan.FromSeconds(6),
                Flash: ctx.Edit.Rules.FirstOrDefault()?.Flash ?? new FlashSettings()), appearanceOverride: ctx.Edit.Appearance)),
            b.Button("Preview fullscreen break (5 s)", () =>
            {
                var r = RuleTemplates.Stretch();
                r.Duration = TimeSpan.FromSeconds(5);
                ctx.C.Presenter.Preview(r, ctx.Edit);
            })));
        return b.Panel;
    }

    // ------------------------------------------------------------------ Texts
    private static readonly (string Prop, string Title, string Desc)[] TextRows =
    [
        (nameof(TextSettings.BrandName), "App name", "Small label on notifications."),
        (nameof(TextSettings.WarningTitle), "Warning title", "{rule} = rule name, {time} = time left."),
        (nameof(TextSettings.WarningBody), "Warning text", "{duration} = break length, {style} = style name."),
        (nameof(TextSettings.StartNowButton), "\"Start now\" button", ""),
        (nameof(TextSettings.PostponeButton), "\"Postpone\" button", "{time} = postpone length."),
        (nameof(TextSettings.SkipButton), "\"Skip\" button (notifications)", ""),
        (nameof(TextSettings.SkipBreakButton), "\"Skip\" button (break screen)", ""),
        (nameof(TextSettings.AlsoCovers), "Merged breaks line", "{rules} = smaller breaks included in this one."),
        (nameof(TextSettings.HintSkip), "Hint: skippable", "Leave empty to hide."),
        (nameof(TextSettings.HintLocked), "Hint: locked", "{hotkey} = escape key combo. Empty = hidden."),
        (nameof(TextSettings.HintTotal), "Hint: TOTAL LOCK", "Empty = hidden."),
        (nameof(TextSettings.DefaultMessage), "Default message", "Used when a rule has no messages."),
        (nameof(TextSettings.StyleNotification), "Name: notification style", "Used for {style}."),
        (nameof(TextSettings.StyleWindow), "Name: window style", "Used for {style}."),
        (nameof(TextSettings.StyleFullscreen), "Name: fullscreen style", "Used for {style}."),
    ];

    private static StackPanel Texts(SettingsContext ctx)
    {
        var t = ctx.Edit.Texts;
        var b = new PageBuilder(ctx, "Texts");
        b.Intro("Change any text the app shows. Words in {braces} are filled in automatically. Clear a box and save to get the default back.");
        b.Group("Notifications & buttons");
        foreach (var (prop, title, desc) in TextRows)
        {
            if (prop == nameof(TextSettings.AlsoCovers)) b.Group("Break screen");
            if (prop == nameof(TextSettings.StyleNotification)) b.Group("Style names");
            var pi = typeof(TextSettings).GetProperty(prop)!;
            b.Text("Texts." + prop, title, desc, () => (string?)pi.GetValue(t), v => pi.SetValue(t, v), width: 300);
        }
        b.Group("Reset");
        b.Add(b.Button("Reset all texts", () => { ctx.Edit.Texts = new TextSettings(); ctx.MarkDirty(); ctx.Rebuild(); }));
        return b.Panel;
    }

    // ------------------------------------------------------------------ Sounds
    private static StackPanel Sounds(SettingsContext ctx)
    {
        var s = ctx.Edit.Sound;
        var b = new PageBuilder(ctx, "Sounds");
        b.Group("Volume");
        b.Toggle("Sound.Mute", "Mute all sounds", "", () => s.Mute, v => s.Mute = v);
        b.Slider("Sound.Volume", "Volume", "Applies to every sound.", () => s.Volume, v => s.Volume = Math.Round(v, 2), 0, 1, Pct);
        b.Group("Sounds");
        b.Sound("Sound.WarningSound", "Warning sound", "Before breaks (if the rule's warning sound is on).", () => s.WarningSound, v => s.WarningSound = v);
        foreach (var r in ctx.Edit.Rules)
        {
            b.Sound("Rules[].StartSound", $"{r.Name}: start", "When this break begins.", () => r.StartSound, v => r.StartSound = v);
            b.Sound("Rules[].EndSound", $"{r.Name}: end", "When this break is over.", () => r.EndSound, v => r.EndSound = v);
        }
        return b.Panel;
    }

    // ------------------------------------------------------------------ Stats
    private static StackPanel Stats(SettingsContext ctx)
    {
        var b = new PageBuilder(ctx, "Stats");
        IReadOnlyList<HistoryEntry> entries;
        try { entries = ctx.C.History.ReadAll(); }
        catch (IOException) { entries = []; }
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = Core.History.Stats.PerDay(entries, today.AddDays(-13), today);

        b.Group("Last 14 days");
        var streak = Core.History.Stats.CurrentStreakDays(entries, today);
        b.Add(new TextBlock { FontSize = 13, Text = $"🔥 Current streak: {streak} day{(streak == 1 ? "" : "s")} with breaks taken and none skipped" }
            .WithRef(TextBlock.ForegroundProperty, "Text1Brush"));
        b.Add(Chart(days));
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, key) in new[] { ("Taken", "Accent1Brush"), ("Away (natural)", "SuccessBrush"), ("Postponed", "WarningBrush"), ("Skipped", "ErrorBrush") })
        {
            legend.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0) }.WithRef(Border.BackgroundProperty, key));
            legend.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 16, 0) }.WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
        }
        b.Add(legend);

        b.Group("Per rule (all time)");
        var table = new Grid();
        foreach (var w in new[] { 2.0, 1, 1, 1 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        void Cell(string text, int row, int col, bool head = false)
        {
            var tb = new TextBlock { Text = text, FontSize = 12.5, FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 3, 0, 3) };
            tb.SetResourceReference(TextBlock.ForegroundProperty, head ? "Text3Brush" : "Text1Brush");
            Grid.SetRow(tb, row); Grid.SetColumn(tb, col);
            table.Children.Add(tb);
        }
        var per = Core.History.Stats.PerRule(entries);
        for (var i = 0; i <= per.Count; i++) table.RowDefinitions.Add(new RowDefinition());
        Cell("Rule", 0, 0, true); Cell("Taken", 0, 1, true); Cell("Skipped", 0, 2, true); Cell("Postponed", 0, 3, true);
        for (var i = 0; i < per.Count; i++)
        {
            Cell(per[i].Rule, i + 1, 0); Cell(per[i].Taken.ToString(), i + 1, 1); Cell(per[i].Skipped.ToString(), i + 1, 2); Cell(per[i].Postponed.ToString(), i + 1, 3);
        }
        b.Add(per.Count == 0 ? new TextBlock { Text = "No breaks recorded yet." }.WithRef(TextBlock.ForegroundProperty, "Text2Brush") : table);

        b.Group("History file");
        b.Add(PageBuilder.Buttons(
            b.Button("Export CSV…", () =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = $"breaks-{today:yyyy-MM-dd}.csv" };
                if (dlg.ShowDialog() == true) ctx.C.History.ExportCsv(dlg.FileName);
            }),
            b.Button("Clear history", () =>
            {
                if (MessageBox.Show("Delete all break history? This can't be undone.", "Clear history", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                ctx.C.History.Clear();
                ctx.Rebuild();
            }, "DangerButton")));
        return b.Panel;
    }

    private static FrameworkElement Chart(IReadOnlyList<DayStat> days)
    {
        var max = Math.Max(1, days.Max(d => d.Taken + d.Natural + d.Postponed + d.Skipped));
        var grid = new Grid { Height = 170, Margin = new Thickness(0, 10, 0, 6) };
        foreach (var _ in days) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var col = new DockPanel { Margin = new Thickness(3, 0, 3, 0) };
            var label = new TextBlock { Text = d.Day.ToString("ddd")[..2], FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Text3Brush");
            DockPanel.SetDock(label, Dock.Bottom);
            col.Children.Add(label);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, ToolTip = $"{d.Day:ddd d MMM}: {d.Taken} taken, {d.Natural} natural, {d.Postponed} postponed, {d.Skipped} skipped" };
            foreach (var (n, key) in new[] { (d.Skipped, "ErrorBrush"), (d.Postponed, "WarningBrush"), (d.Natural, "SuccessBrush"), (d.Taken, "Accent1Brush") })
                if (n > 0) stack.Children.Add(new Border { Height = 140.0 * n / max, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 1, 0, 0) }.WithRef(Border.BackgroundProperty, key));
            col.Children.Add(stack);
            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        return grid;
    }

    // ------------------------------------------------------------------ General
    private static StackPanel General(SettingsContext ctx)
    {
        var g = ctx.Edit.General;
        var b = new PageBuilder(ctx, "General");
        b.Group("Startup & display");
        b.Toggle("General.StartWithWindows", "Start with Windows", "Launch Break App when you sign in.", () => g.StartWithWindows, v => g.StartWithWindows = v);
        b.Toggle("General.Use24HourClock", "24-hour clock", "Off: times like 10:45 AM.", () => g.Use24HourClock, v => g.Use24HourClock = v);
        b.Toggle("General.AdvancedMode", "Show advanced settings", "Shows every setting (also the switch at the top).", () => g.AdvancedMode, v => g.AdvancedMode = v, after: ctx.Rebuild);

        b.Group("Keyboard shortcuts", "Work anywhere in Windows. Backspace clears a shortcut.");
        b.Hotkey("General.PauseHotkey", "Pause / resume", "Toggles a pause until you resume.", () => g.PauseHotkey, v => g.PauseHotkey = v, allowEmpty: true);
        b.Hotkey("General.BreakNowHotkey", "Take a break now", "Starts the first enabled rule's break.", () => g.BreakNowHotkey, v => g.BreakNowHotkey = v, allowEmpty: true);

        b.Group("Settings file");
        b.Add(PageBuilder.Buttons(
            b.Button("Export settings…", () =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Break App settings|*.json", FileName = "break-app-settings.json" };
                if (dlg.ShowDialog() == true) SettingsStore.Export(ctx.Edit, dlg.FileName);
            }),
            b.Button("Import settings…", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Break App settings|*.json|All files|*.*" };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    ctx.Edit = SettingsStore.Import(dlg.FileName);
                    ctx.MarkAppearance();
                    ctx.Rebuild();
                }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or NotSupportedException or UnauthorizedAccessException)
                {
                    MessageBox.Show("Couldn't read that file:\n" + e.Message, "Import settings", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }),
            b.Button("Open data folder", () => Process.Start(new ProcessStartInfo(SettingsStore.AppDataDir) { UseShellExecute = true })),
            b.Button("Reset everything", () =>
            {
                if (MessageBox.Show("Reset all settings to the defaults? (Not saved until you press Save.)", "Reset", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                ctx.Edit = AppSettings.CreateDefault();
                ctx.MarkAppearance();
                ctx.Rebuild();
            }, "DangerButton")));

        b.Group("About");
        b.Add(new TextBlock { Text = $"Break App {typeof(SettingsPages).Assembly.GetName().Version?.ToString(3)} · settings in {SettingsStore.AppDataDir}", TextWrapping = TextWrapping.Wrap }
            .WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
        return b.Panel;
    }
}
