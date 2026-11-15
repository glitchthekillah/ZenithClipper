using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Find and replace across titles and captions, and restyling every caption at once (Premiere's track style).</summary>
public sealed partial class EditorWindow
{
    /// <summary>Redraws every text clip whose media <paramref name="pick"/> returns new (text, size, color, background) for.</summary>
    int RedrawText(string label, Func<MediaAsset, bool, (string Text, double Size, string Color, string? Background)?> pick)
    {
        var project = ProjectCopy; var seq = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        var redrawn = new Dictionary<Guid, MediaAsset>();
        foreach (var track in seq.Tracks.Where(t => t.Kind == TrackKind.Video))
        {
            bool caption = track.Name.StartsWith("Captions", StringComparison.Ordinal);
            foreach (var clip in track.Clips)
            {
                if (redrawn.ContainsKey(clip.MediaId)) continue;
                var media = project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                if (media is not { Kind: MediaKind.Text, Text: not null }) continue;
                if (track.Locked) continue;
                if (pick(media, caption) is not { } next) continue;
                redrawn[media.Id] = TitleRasterizer.Create(next.Text, next.Size, next.Color, media.DurationSeconds, AssetDirectory, next.Background, TitleStyle.FromJson(media.TextStyle));
            }
        }
        if (redrawn.Count == 0) return 0;
        Change(label, p =>
        {
            var s = p.Sequences.First(x => x.Id == p.ActiveSequenceId);
            foreach (var asset in redrawn.Values) p.Media.Add(asset);
            foreach (var track in s.Tracks.Where(t => !t.Locked))
            foreach (var clip in track.Clips.Where(c => redrawn.ContainsKey(c.MediaId)))
            {
                var old = p.Media.First(m => m.Id == clip.MediaId); var asset = redrawn[clip.MediaId];
                // Keep the on-screen size proportional when the bitmap's size changes.
                if (old.Width > 0 && old.Height > 0) { clip.Transform.ScaleX *= (double)asset.Width / old.Width; clip.Transform.ScaleY *= (double)asset.Height / old.Height; }
                clip.MediaId = asset.Id; clip.Name = asset.Name;
            }
        });
        return redrawn.Count;
    }

    static (double Size, string Color, string? Background) StyleOf(MediaAsset media, bool caption) =>
        (media.FontSize ?? (caption ? 44 : 48), media.Color ?? "#ffffff", media.Background ?? (caption && media.FontSize is null ? "#B0000000" : null));

    async Task FindReplaceText()
    {
        var dialog = new Window { Title = "Find and replace", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Find and replace in titles and captions", FontSize = 15, FontWeight = FontWeight.SemiBold });
        var find = new TextBox { PlaceholderText = "Find" }; var replace = new TextBox { PlaceholderText = "Replace with" };
        var matchCase = new CheckBox { Content = "Match case", Foreground = SoftTextBrush }; var whole = new CheckBox { Content = "Whole words", Foreground = SoftTextBrush };
        panel.Children.Add(find); panel.Children.Add(replace); panel.Children.Add(matchCase); panel.Children.Add(whole);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var go = Button("Replace all", () => dialog.Close(true)); go.Background = AccentBrush; go.Foreground = InkBrush; go.Classes.Add("primary");
        buttons.Children.Add(go); buttons.Children.Add(Button("Cancel", () => dialog.Close(false))); panel.Children.Add(buttons);
        dialog.Opened += (_, _) => find.Focus();
        if (!await dialog.ShowDialog<bool>(this) || string.IsNullOrEmpty(find.Text)) return;
        string needle = find.Text, with = replace.Text ?? "";
        var options = System.Text.RegularExpressions.RegexOptions.CultureInvariant | (matchCase.IsChecked == true ? 0 : System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var pattern = new System.Text.RegularExpressions.Regex((whole.IsChecked == true ? @"\b" : "") + System.Text.RegularExpressions.Regex.Escape(needle) + (whole.IsChecked == true ? @"\b" : ""), options);
        int hits = 0;
        int changed = RedrawText("Find and replace", (media, caption) =>
        {
            var next = pattern.Replace(media.Text!, _ => { hits++; return with; });
            if (next == media.Text || string.IsNullOrWhiteSpace(next)) return null;
            var (size, color, background) = StyleOf(media, caption); return (next, size, color, background);
        });
        status.Text = changed == 0 ? $"No titles or captions contain \"{needle}\"." : $"Replaced {hits} match{(hits == 1 ? "" : "es")} in {changed} title{(changed == 1 ? "" : "s")}/caption{(changed == 1 ? "" : "s")}.";
    }

    async Task RestyleCaptions()
    {
        var sample = Project.Media.FirstOrDefault(m => m.Kind == MediaKind.Text && Sequence.Tracks.Any(t => t.Name.StartsWith("Captions", StringComparison.Ordinal) && t.Clips.Any(c => c.MediaId == m.Id)));
        if (sample is null) { status.Text = "There are no captions in this sequence yet."; return; }
        var (size0, color0, bg0) = StyleOf(sample, true);
        var dialog = new Window { Title = "Caption style", Width = 400, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Style every caption", FontSize = 15, FontWeight = FontWeight.SemiBold });
        var size = new TextBox { Text = size0.ToString("0", CultureInfo.InvariantCulture) }; var color = new TextBox { Text = color0 }; var background = new TextBox { Text = bg0 ?? "", PlaceholderText = "none" };
        foreach (var (label, box) in new[] { ("Font size (px)", size), ("Text color (#RRGGBB)", color), ("Background (#AARRGGBB, empty for none)", background) }) { panel.Children.Add(new TextBlock { Text = label, Foreground = SoftTextBrush, FontSize = 12 }); panel.Children.Add(box); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var go = Button("Apply to all captions", () => dialog.Close(true)); go.Background = AccentBrush; go.Foreground = InkBrush; go.Classes.Add("primary");
        buttons.Children.Add(go); buttons.Children.Add(Button("Cancel", () => dialog.Close(false))); panel.Children.Add(buttons);
        if (!await dialog.ShowDialog<bool>(this)) return;
        if (!double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || px < 12 || px > 256) { status.Text = "Font size must be 12 to 256."; return; }
        string fg = (color.Text ?? "").Trim(), bg = (background.Text ?? "").Trim();
        if (!Color.TryParse(fg, out _) || (bg.Length > 0 && !Color.TryParse(bg, out _))) { status.Text = "Colors look like #FFFFFF or #B0000000."; return; }
        int changed = RedrawText("Caption style", (media, caption) => caption ? (media.Text!, px, fg, bg.Length == 0 ? null : bg) : null);
        status.Text = $"Restyled {changed} caption{(changed == 1 ? "" : "s")}.";
    }
}

public sealed partial class EditorWindow
{
    internal void VerifyCaptionTools()
    {
        var fps = new FrameRate(30, 1); var dir = Path.Combine(Path.GetTempPath(), "zc-caption-check"); 
        var asset = TitleRasterizer.Create("hello world", 44, "#ffffff", 2, dir, "#B0000000");
        var seq = new EditSequence { Fps = fps, Tracks = [new EditTrack { Name = "Captions", Kind = TrackKind.Video }] };
        var clip = new TimelineClip { MediaId = asset.Id, Duration = 60, StreamIntent = ClipStreamIntent.VideoOnly, Transform = new ClipTransform { ScaleX = (double)asset.Width / seq.Width, ScaleY = (double)asset.Height / seq.Height } };
        seq.Tracks[0].Clips.Add(clip);
        var keep = session;
        try
        {
            session = new EditorSession(new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [asset] });
            int n = RedrawText("Find and replace", (m, caption) => caption ? (m.Text!.Replace("world", "there"), 88, "#ffff00", null) : null);
            var c = Sequence.Tracks[0].Clips[0]; var media = Project.Media.First(m => m.Id == c.MediaId);
            if (n != 1 || media.Text != "hello there" || media.FontSize != 88 || media.Color != "#ffff00" || Math.Abs(c.Transform.ScaleX - (double)media.Width / seq.Width) > 1e-6)
                throw new InvalidOperationException("Caption redraw did not replace text, keep style and rescale.");
            SplitCaptionsIntoWords();
            var words = Sequence.Tracks[0].Clips;
            if (words.Count != 2 || words[0].Start != 0 || words[1].End != 60 || words[0].End != words[1].Start || Project.Media.First(m => m.Id == words[1].MediaId).Text != "there")
                throw new InvalidOperationException("Single-word captions did not split evenly.");
        }
        finally { session = keep; Refresh(); try { Directory.Delete(dir, true); } catch (IOException) { } }
        Console.WriteLine("Caption tools smoke passed: find/replace redraw, style stored, size kept proportional.");
    }
}

public sealed partial class EditorWindow
{
    /// <summary>TikTok-style single-word captions: each caption becomes one clip per word, timed by word length.</summary>
    void SplitCaptionsIntoWords()
    {
        var project = ProjectCopy; var seq = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        var plan = new List<(Guid TrackId, Guid ClipId, List<(MediaAsset Media, long Start, long Length)> Words)>();
        foreach (var track in seq.Tracks.Where(t => t.Kind == TrackKind.Video && !t.Locked && t.Name.StartsWith("Captions", StringComparison.Ordinal)))
        foreach (var clip in track.Clips)
        {
            var media = project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
            if (media is not { Kind: MediaKind.Text, Text: { } text }) continue;
            var words = text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || words.Length > clip.Duration) continue;
            var (size, color, background) = StyleOf(media, true); var style = TitleStyle.FromJson(media.TextStyle);
            double total = words.Sum(w => w.Length + 2.0); long at = clip.Start; var pieces = new List<(MediaAsset, long, long)>();
            for (int i = 0; i < words.Length; i++)
            {
                long end = i == words.Length - 1 ? clip.End : clip.Start + (long)Math.Round(clip.Duration * words.Take(i + 1).Sum(w => w.Length + 2.0) / total);
                end = Math.Max(end, at + 1);
                pieces.Add((TitleRasterizer.Create(words[i], size, color, Math.Max(.05, seq.Fps.Seconds(end - at)), AssetDirectory, background, style), at, end - at)); at = end;
            }
            plan.Add((track.Id, clip.Id, pieces));
        }
        if (plan.Count == 0) { status.Text = "No multi-word captions to split."; return; }
        Change("Single-word captions", p =>
        {
            var s = p.Sequences.First(x => x.Id == p.ActiveSequenceId);
            foreach (var (trackId, clipId, words) in plan)
            {
                var track = s.Tracks.First(t => t.Id == trackId); var old = track.Clips.First(c => c.Id == clipId); track.Clips.Remove(old);
                var oldMedia = p.Media.First(m => m.Id == old.MediaId);
                foreach (var (media, start, length) in words)
                {
                    p.Media.Add(media); var c = ProjectValidation.Clone(old); c.Id = Guid.NewGuid(); c.MediaId = media.Id; c.Name = media.Name; c.Start = start; c.Duration = length; c.SourceIn = 0;
                    if (oldMedia.Width > 0 && oldMedia.Height > 0) { c.Transform.ScaleX = old.Transform.ScaleX * media.Width / oldMedia.Width; c.Transform.ScaleY = old.Transform.ScaleY * media.Height / oldMedia.Height; }
                    track.Clips.Add(c);
                }
                track.Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
            }
        });
        status.Text = $"Split {plan.Count} caption{(plan.Count == 1 ? "" : "s")} into single words.";
    }
}
