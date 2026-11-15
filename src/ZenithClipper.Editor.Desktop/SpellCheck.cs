using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>A spelling dictionary: which words in a text are unknown, and suggestions for one.</summary>
internal interface ISpellEngine
{
    IEnumerable<(int Start, int Length)> Errors(string text);
    IReadOnlyList<string> Suggest(string word);
    void Add(string word);
}

/// <summary>Windows' own spell checker (the one Word and Edge use), through its COM API. No download, no extra files.</summary>
internal sealed class WindowsSpellEngine : ISpellEngine
{
    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactory
    {
        [PreserveSig] int get_SupportedLanguages(out IEnumString value);
        [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string tag, out int value);
        [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string tag, out ISpellChecker value);
    }
    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellChecker
    {
        [PreserveSig] int get_LanguageTag(out IntPtr value);
        [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
        [PreserveSig] int Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString value);
        [PreserveSig] int Add([MarshalAs(UnmanagedType.LPWStr)] string word);
    }
    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingError { [PreserveSig] int Next(out ISpellingError value); }
    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingError
    {
        [PreserveSig] int get_StartIndex(out uint value);
        [PreserveSig] int get_Length(out uint value);
    }

    readonly ISpellChecker checker;
    WindowsSpellEngine(ISpellChecker checker) => this.checker = checker;

    /// <summary>The checker for the user's language (falling back to en-US), or null when Windows has none.</summary>
    public static WindowsSpellEngine? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")); if (type is null) return null;
            var factory = (ISpellCheckerFactory)Activator.CreateInstance(type)!;
            foreach (var tag in new[] { System.Globalization.CultureInfo.CurrentUICulture.Name, "en-US" })
                if (!string.IsNullOrEmpty(tag) && factory.IsSupported(tag, out var ok) == 0 && ok != 0 && factory.CreateSpellChecker(tag, out var checker) == 0)
                    return new WindowsSpellEngine(checker);
        }
        catch (COMException) { } catch (InvalidCastException) { }
        return null;
    }
    public IEnumerable<(int Start, int Length)> Errors(string text)
    {
        var list = new List<(int, int)>();
        if (checker.Check(text, out var errors) != 0) return list;
        while (errors.Next(out var e) == 0 && e is not null) { e.get_StartIndex(out var s); e.get_Length(out var n); list.Add(((int)s, (int)n)); }
        return list;
    }
    public IReadOnlyList<string> Suggest(string word)
    {
        var list = new List<string>();
        if (checker.Suggest(word, out var e) != 0 || e is null) return list;
        var one = new string[1];
        while (list.Count < 8 && e.Next(1, one, IntPtr.Zero) == 0) list.Add(one[0]);
        return list;
    }
    public void Add(string word) => checker.Add(word);
}

/// <summary>A plain word list (used by the self-check, and anywhere Windows' checker isn't available).</summary>
internal sealed class WordListSpellEngine(IEnumerable<string> words) : ISpellEngine
{
    readonly HashSet<string> known = new(words, StringComparer.OrdinalIgnoreCase);
    public IEnumerable<(int Start, int Length)> Errors(string text) =>
        Regex.Matches(text, @"\p{L}[\p{L}']*").Where(m => !known.Contains(m.Value)).Select(m => (m.Index, m.Length)).ToList();
    public IReadOnlyList<string> Suggest(string word) =>
        known.Select(k => (k, d: Distance(word.ToLowerInvariant(), k.ToLowerInvariant()))).Where(x => x.d <= 2).OrderBy(x => x.d).ThenBy(x => x.k, StringComparer.Ordinal).Take(8).Select(x => x.k).ToList();
    public void Add(string word) => known.Add(word);
    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i; for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++) for (int j = 1; j <= b.Length; j++) d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}

public sealed partial class EditorWindow
{
    static string UserDictionaryPath => Path.Combine(SettingsDirectory, "dictionary.txt");
    ISpellEngine? spellEngine;
    ISpellEngine? SpellEngine()
    {
        if (spellEngine is not null) return spellEngine;
        spellEngine = WindowsSpellEngine.TryCreate();
        if (spellEngine is not null && File.Exists(UserDictionaryPath)) foreach (var w in File.ReadAllLines(UserDictionaryPath).Where(l => l.Trim().Length > 0)) spellEngine.Add(w.Trim());
        return spellEngine;
    }

    /// <summary>Unknown words across titles and captions in the active sequence: word → (one sentence of context, how many).</summary>
    internal List<(string Word, string Context, int Count)> Misspellings(ISpellEngine engine, ISet<string> ignored)
    {
        var found = new Dictionary<string, (string Context, int Count)>(StringComparer.Ordinal);
        var project = ProjectCopy; var seq = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        var ids = seq.Tracks.Where(t => t.Kind == TrackKind.Video).SelectMany(t => t.Clips).Select(c => c.MediaId).ToHashSet();
        foreach (var media in project.Media.Where(m => ids.Contains(m.Id) && m is { Kind: MediaKind.Text, Text: not null }))
            foreach (var (start, length) in engine.Errors(media.Text!))
            {
                string word = media.Text!.Substring(start, length); if (ignored.Contains(word) || word.Any(char.IsDigit)) continue;
                found[word] = found.TryGetValue(word, out var f) ? (f.Context, f.Count + 1) : (media.Text!.Replace('\n', ' '), 1);
            }
        return found.Select(kv => (kv.Key, kv.Value.Context, kv.Value.Count)).ToList();
    }
    /// <summary>Replaces a whole word in every title and caption, keeping each one's style.</summary>
    internal int ReplaceWord(string word, string with)
    {
        var pattern = new Regex(@"(?<![\p{L}'])" + Regex.Escape(word) + @"(?![\p{L}'])", RegexOptions.CultureInvariant);
        return RedrawText("Spelling", (media, caption) =>
        {
            var next = pattern.Replace(media.Text!, with);
            if (next == media.Text || string.IsNullOrWhiteSpace(next)) return null;
            var (size, color, background) = StyleOf(media, caption); return (next, size, color, background);
        });
    }

    /// <summary>Captions → Check spelling: walks each unknown word with suggestions; Change all, Ignore, or Add to dictionary.</summary>
    async Task CheckSpelling()
    {
        if (SpellEngine() is not { } engine) { status.Text = "Spell check uses Windows' built-in dictionary, which isn't available on this system."; return; }
        var ignored = new HashSet<string>(StringComparer.Ordinal); int changed = 0, seen = 0;
        while (Misspellings(engine, ignored).FirstOrDefault() is { Word: not null } next)
        {
            seen++;
            var dialog = new Window { Title = "Check spelling", Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 }; dialog.Content = panel;
            panel.Children.Add(new TextBlock { Text = $"Not in dictionary: \"{next.Word}\"{(next.Count > 1 ? $" ({next.Count} times)" : "")}", FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = next.Context.Length > 160 ? next.Context[..160] + "…" : next.Context, Foreground = SoftTextBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var suggestions = engine.Suggest(next.Word);
            var replacement = new TextBox { Text = suggestions.FirstOrDefault() ?? next.Word, PlaceholderText = "Change to" }; panel.Children.Add(replacement);
            if (suggestions.Count > 0)
            {
                var list = new ListBox { ItemsSource = suggestions, MaxHeight = 160, SelectedIndex = 0 };
                list.SelectionChanged += (_, _) => { if (list.SelectedItem is string s) replacement.Text = s; }; panel.Children.Add(list);
            }
            else panel.Children.Add(new TextBlock { Text = "No suggestions.", Foreground = FaintBrush, FontSize = 12 });
            string action = "stop";
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            Button B(string text, string act, bool primary = false) { var b = Button(text, () => { action = act; dialog.Close(); }); b.Margin = new Thickness(4, 0, 0, 4); if (primary) { b.Background = AccentBrush; b.Foreground = InkBrush; b.Classes.Add("primary"); } buttons.Children.Add(b); return b; }
            B("Change all", "change", true); B("Ignore", "ignore"); B("Add to dictionary", "add"); B("Stop", "stop");
            panel.Children.Add(buttons);
            await dialog.ShowDialog(this);
            if (action == "stop") break;
            if (action == "ignore") ignored.Add(next.Word);
            else if (action == "add")
            {
                engine.Add(next.Word); ignored.Add(next.Word);
                try { Directory.CreateDirectory(SettingsDirectory); File.AppendAllLines(UserDictionaryPath, [next.Word]); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            else
            {
                string to = replacement.Text ?? ""; if (to.Length == 0 || to == next.Word) { ignored.Add(next.Word); continue; }
                ReplaceWord(next.Word, to); changed++;
                if (Misspellings(engine, ignored).Any(m => m.Word == next.Word)) ignored.Add(next.Word); // locked tracks keep it
            }
        }
        status.Text = seen == 0 ? "No spelling problems in titles or captions." : changed == 0 ? "Spell check done." : $"Spell check done: changed {changed} word{(changed == 1 ? "" : "s")}.";
    }

    internal void VerifySpelling()
    {
        var engine = new WordListSpellEngine(["hello", "there", "world", "the", "quick"]);
        var seq = new EditSequence { Tracks = [new EditTrack { Name = "Captions", Kind = TrackKind.Video }] };
        var asset = new MediaAsset { Kind = MediaKind.Text, Text = "hello wrold, the quick wrold", Path = "/tmp/none.png", Width = 10, Height = 10, DurationSeconds = 2 };
        seq.Tracks[0].Clips.Add(new TimelineClip { MediaId = asset.Id, Duration = 10, StreamIntent = ClipStreamIntent.VideoOnly });
        var keep = session;
        try
        {
            session = new EditorSession(new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [asset] });
            var miss = Misspellings(engine, new HashSet<string>());
            if (miss.Count != 1 || miss[0].Word != "wrold" || miss[0].Count != 2) throw new InvalidOperationException("Spell check did not find the misspelling.");
            if (engine.Suggest("wrold").FirstOrDefault() != "world") throw new InvalidOperationException("Spell check did not suggest the right word.");
        }
        finally { session = keep; }
    }
}
