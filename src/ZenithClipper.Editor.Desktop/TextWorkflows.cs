using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    void AddImportedMedia(MediaAsset media)
    {
        EnsureEditable();bool adopted=false;session.Execute("Import media",p=>{adopted=MediaMetadata.TryAdoptFirstVideo(p,media,formatChosen);p.Media.Add(media);});if(adopted)formatChosen=true;Refresh(true);
    }
    string AssetDirectory => projectPath is { } path
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path) + ".assets")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithClipper", "EditorAssets", Project.Id.ToString("N"));

    /// <summary>Premiere's Roll: a title that scrolls from below the frame to above it over its duration.</summary>
    async Task CreateCreditRoll()
    {
        var options = await TitleOptionsDialog("Create credit roll", "Directed by\nYou\n\nEdited with\nZenith Clipper", 10);
        if (options is null)
            return;
        var asset = TitleRasterizer.Create(options.Text, options.FontSize, options.Color, options.Duration, AssetDirectory, options.Background, options.Style);
        Change("Create credit roll", p =>
        {
            var sequence = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
            var track = OverlayTrack(sequence, "Titles");
            var clip = TextClip(asset, sequence, ProgramPlayhead, sequence.Fps.Frames(options.Duration), false);
            double travel = .5 + clip.Transform.ScaleY / 2 + .02;
            clip.Transform.Y = 0;
            clip.Motion.Add(new Keyframe { Parameter = "y", Frame = 0, Value = travel, Interpolation = "linear" });
            clip.Motion.Add(new Keyframe { Parameter = "y", Frame = clip.Duration, Value = -travel, Interpolation = "linear" });
            p.Media.Add(asset);
            track.Clips.Add(clip); track.Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
            selectedTrack = track.Id;
            selectedClip = clip.Id;
        });
    }

    async Task CreateTitle()
    {
        var options = await TitleOptionsDialog("Create title", "Your title", Prefs.StillSeconds);
        if (options is null)
            return;
        var asset = TitleRasterizer.Create(options.Text, options.FontSize, options.Color, options.Duration, AssetDirectory, options.Background, options.Style);
        Change("Create title", p =>
        {
            var sequence = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
            var track = OverlayTrack(sequence, "Titles");
            var clip = TextClip(asset, sequence, ProgramPlayhead, sequence.Fps.Frames(options.Duration), false);
            p.Media.Add(asset);
            track.Clips.Add(clip);
            selectedTrack = track.Id;
            selectedClip = clip.Id;
        });
    }

    async Task EditSelectedText()
    {
        var selected = Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClip);
        if (selected is null)
            return;
        var old = Project.Media.First(m => m.Id == selected.MediaId);
        if (old.Kind != MediaKind.Text)
            return;
        var options = await TitleOptionsDialog("Edit title / caption", old.Text ?? "", Sequence.Fps.Seconds(selected.Duration), old);
        if (options is null)
            return;
        var asset = TitleRasterizer.Create(options.Text, options.FontSize, options.Color, options.Duration, AssetDirectory, options.Background, options.Style);
        Change("Edit title text", p =>
        {
            var sequence = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
            var track = sequence.Tracks.First(t => t.Clips.Any(c => c.Id == selected.Id));
            if (track.Locked)
                throw new InvalidOperationException("Unlock the track before editing its text.");
            var clip = track.Clips.First(c => c.Id == selected.Id);
            var replacement = TextClip(asset, sequence, clip.Start, sequence.Fps.Frames(options.Duration), track.Name.StartsWith("Captions", StringComparison.Ordinal));
            p.Media.Add(asset);
            clip.MediaId = asset.Id;
            clip.Name = asset.Name;
            clip.SourceIn = 0;
            clip.Duration = replacement.Duration;
            clip.Speed = 1;
            clip.Reverse = false;
            clip.Transform.ScaleX = replacement.Transform.ScaleX;
            clip.Transform.ScaleY = replacement.Transform.ScaleY;
        });
    }

    async Task ImportCaptions()
    {
        var destination = session;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Import SRT captions",
            FileTypeFilter = [new("SubRip subtitles") { Patterns = ["*.srt"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
            return;
        if (new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new IOException("Caption files must be smaller than 4 MB.");
        var cues = SubtitleCodec.ParseSrt(await File.ReadAllTextAsync(path));
        CheckImportDestination(destination);
        var assetFolder = AssetDirectory;
        if (cues.Count == 0)
            throw new InvalidDataException("The subtitle file contains no cues.");
        if (cues.Count > 2000)
            throw new InvalidDataException("Import up to 2,000 captions at once; split a longer subtitle file first.");
        var rendered = new List<(SubtitleCue Cue, MediaAsset Asset)>();
        try
        {
            foreach (var cue in cues)
            {
                CheckImportDestination(destination);
                var asset = TitleRasterizer.Create(cue.Text, 48, "#ffffff", cue.EndSeconds - cue.StartSeconds, assetFolder);
                rendered.Add((cue, asset));
                if (rendered.Count % 25 == 0)
                {
                    status.Text = $"Rendering captions {rendered.Count}/{cues.Count}…";
                    await Task.Yield();
                }
            }
            CheckImportDestination(destination);
            EnsureEditable();
            session.Execute("Import captions", p =>
            {
                var sequence = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
                var track = OverlayTrack(sequence, "Captions");
                foreach (var (cue, asset) in rendered)
                {
                    var start = sequence.Fps.Frames(cue.StartSeconds);
                    var end = sequence.Fps.Frames(cue.EndSeconds);
                    p.Media.Add(asset);
                    track.Clips.Add(TextClip(asset, sequence, start, Math.Max(1, end - start), true));
                }
                selectedTrack = track.Id;
                selectedClip = track.Clips.FirstOrDefault()?.Id;
            });
            Refresh(true);
            status.Text = $"Imported {cues.Count} captions. Text is editable in the clip inspector.";
        }
        catch
        {
            foreach (var (_, asset) in rendered)
                File.Delete(asset.Path);
            throw;
        }
    }

    void CheckImportDestination(EditorSession destination)
    {
        if (editorClosed || !ReferenceEquals(session, destination))
            throw new OperationCanceledException("The project changed during import.");
    }

    async Task ExportCaptions()
    {
        var project = ProjectCopy;
        var cues = CaptionCues(project, 0, long.MaxValue);
        if (cues.Count == 0)
            throw new InvalidOperationException("There are no captions to export. Import SRT captions first.");
        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Export editable captions as SRT",
            SuggestedFileName = Project.Name + ".srt",
            DefaultExtension = "srt",
            FileTypeChoices = [new("SubRip subtitles") { Patterns = ["*.srt"] }]
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        if (project.Media.Where(m => !string.IsNullOrWhiteSpace(m.Path)).Any(m => string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new IOException("Choose a caption output different from your source media.");
        await File.WriteAllTextAsync(path, SubtitleCodec.WriteSrt(cues), new UTF8Encoding(false));
        status.Text = $"Exported {cues.Count} captions to {path}";
    }

    /// <summary>Caption cues of the active sequence inside [from, to), timed from <paramref name="from"/>.</summary>
    internal static List<SubtitleCue> CaptionCues(EditProject project, long from, long to)
    {
        var sequence = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        return sequence.Tracks.Where(t => t.Name.StartsWith("Captions", StringComparison.Ordinal) && !t.Hidden)
            .SelectMany(t => t.Clips).Where(c => c.Enabled && c.End > from && c.Start < to)
            .Select(c => (Clip: c, Asset: project.Media.First(m => m.Id == c.MediaId)))
            .Where(pair => pair.Asset.Kind == MediaKind.Text && !string.IsNullOrWhiteSpace(pair.Asset.Text))
            .OrderBy(pair => pair.Clip.Start)
            .Select(pair => new SubtitleCue(sequence.Fps.Seconds(Math.Max(pair.Clip.Start, from) - from), sequence.Fps.Seconds(Math.Min(pair.Clip.End, to) - from), pair.Asset.Text!)).ToList();
    }
    /// <summary>The project to export for a caption choice: burned in as is, or with caption tracks hidden.</summary>
    internal static EditProject WithoutCaptions(EditProject project)
    {
        var copy = ProjectValidation.Clone(project);
        foreach (var t in copy.Sequences.First(s => s.Id == copy.ActiveSequenceId).Tracks.Where(t => t.Name.StartsWith("Captions", StringComparison.Ordinal))) t.Hidden = true;
        return copy;
    }
    static EditTrack OverlayTrack(EditSequence sequence, string name)
    {
        var track = sequence.Tracks.LastOrDefault(t => t.Kind == TrackKind.Video && t.Name == name && !t.Locked);
        if (track is not null)
            return track;
        track = new EditTrack { Kind = TrackKind.Video, Name = name };
        sequence.Tracks.Add(track);
        return track;
    }

    static TimelineClip TextClip(MediaAsset asset, EditSequence sequence, long start, long duration, bool caption) => new()
    {
        MediaId = asset.Id,
        Name = asset.Name,
        Start = start,
        Duration = Math.Max(1, duration),
        Transform = new ClipTransform
        {
            ScaleX = Math.Max((double)asset.Width / sequence.Width, (double)asset.Height / sequence.Height),
            ScaleY = Math.Max((double)asset.Width / sequence.Width, (double)asset.Height / sequence.Height),
            Y = caption ? .36 : 0
        }
    };

    async Task<TitleOptions?> TitleOptionsDialog(string title, string initialText, double duration, MediaAsset? existing = null)
    {
        var style0 = TitleStyle.FromJson(existing?.TextStyle);
        var dialog = new Window { Title = title, Width = 560, Height = 680, MinWidth = 400, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 9 };
        dialog.Content = new ScrollViewer { Content = panel };
        var text = new TextBox { Text = initialText, AcceptsReturn = true, Height = 150, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var size = new TextBox { Text = (existing?.FontSize ?? 64).ToString("0", CultureInfo.InvariantCulture) };
        var color = new TextBox { Text = existing?.Color ?? "#ffffff" };
        var fonts = new[] { "(Default)" }.Concat(Avalonia.Media.FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n).Distinct()).ToList();
        var font = new ComboBox { ItemsSource = fonts, SelectedItem = string.IsNullOrEmpty(style0.Font) || !fonts.Contains(style0.Font) ? "(Default)" : style0.Font, HorizontalAlignment = HorizontalAlignment.Stretch };
        var bold = new CheckBox { Content = "Bold", IsChecked = style0.Bold };
        var align = new ComboBox { ItemsSource = new[] { "Left", "Center", "Right" }, SelectedItem = style0.Align, HorizontalAlignment = HorizontalAlignment.Stretch };
        var tracking = new TextBox { Text = style0.Tracking.ToString("0.#", CultureInfo.InvariantCulture) };
        var leading = new TextBox { Text = style0.Leading.ToString("0.##", CultureInfo.InvariantCulture) };
        var stroke = new TextBox { Text = style0.Stroke ?? "", PlaceholderText = "none, or #000000" };
        var strokeWidth = new TextBox { Text = style0.StrokeWidth.ToString("0.#", CultureInfo.InvariantCulture) };
        var shadow = new CheckBox { Content = "Drop shadow", IsChecked = style0.Shadow };
        var gradient = new TextBox { Text = style0.GradientTo ?? "", PlaceholderText = "none, or #FF6B4A" };
        var background = new TextBox { Text = existing?.Background ?? "", PlaceholderText = "none, or #B0000000" };
        var seconds = new TextBox { Text = duration.ToString("0.###", CultureInfo.InvariantCulture) };
        // Text styles (Essential Graphics "Create Text Style"): saved looks you can reuse on any title.
        var styles = LoadTextStyles();
        var stylePick = new ComboBox { ItemsSource = new[] { "(Custom)" }.Concat(styles.Select(x => x.Name)).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var styleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") }; styleRow.Children.Add(stylePick);
        Button? saveStyle = null;
        panel.Children.Add(new TextBlock { Text = "Text style" }); panel.Children.Add(styleRow);
        panel.Children.Add(new TextBlock { Text = "Text (up to 4,000 characters)" });
        panel.Children.Add(text);
        panel.Children.Add(new TextBlock { Text = "Font size in pixels (12–256)" });
        panel.Children.Add(size);
        panel.Children.Add(new TextBlock { Text = "Text color (#RRGGBB or #AARRGGBB)" });
        panel.Children.Add(color);
        panel.Children.Add(new TextBlock { Text = "Duration in seconds" });
        panel.Children.Add(seconds);
        panel.Children.Add(new TextBlock { Text = "Font" }); panel.Children.Add(font); panel.Children.Add(bold);
        panel.Children.Add(new TextBlock { Text = "Alignment" }); panel.Children.Add(align);
        panel.Children.Add(new TextBlock { Text = "Tracking (extra px between letters) and leading (line spacing ×, 0 = auto)" });
        var tl = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") }; Grid.SetColumn(leading, 2); tl.Children.Add(tracking); tl.Children.Add(leading); panel.Children.Add(tl);
        panel.Children.Add(new TextBlock { Text = "Stroke color and width (px)" });
        var sw = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") }; Grid.SetColumn(strokeWidth, 2); sw.Children.Add(stroke); sw.Children.Add(strokeWidth); panel.Children.Add(sw);
        panel.Children.Add(shadow);
        panel.Children.Add(new TextBlock { Text = "Gradient fill: bottom color (top is the text color)" }); panel.Children.Add(gradient);
        panel.Children.Add(new TextBlock { Text = "Background box" }); panel.Children.Add(background);
        stylePick.SelectionChanged += (_, _) =>
        {
            if (stylePick.SelectedIndex <= 0) return; var st = styles[stylePick.SelectedIndex - 1];
            size.Text = st.FontSize.ToString("0", CultureInfo.InvariantCulture); color.Text = st.Color; background.Text = st.Background ?? "";
            var t = TitleStyle.FromJson(st.Style); font.SelectedItem = string.IsNullOrEmpty(t.Font) || !fonts.Contains(t.Font) ? "(Default)" : t.Font; bold.IsChecked = t.Bold; align.SelectedItem = t.Align;
            tracking.Text = t.Tracking.ToString("0.#", CultureInfo.InvariantCulture); leading.Text = t.Leading.ToString("0.##", CultureInfo.InvariantCulture); stroke.Text = t.Stroke ?? ""; strokeWidth.Text = t.StrokeWidth.ToString("0.#", CultureInfo.InvariantCulture); shadow.IsChecked = t.Shadow; gradient.Text = t.GradientTo ?? "";
        };
        saveStyle = Button("Save style…", () => Run(async () =>
        {
            var name = await Prompt("Save text style", "Name this look so you can reuse it on other titles.", "My style"); if (string.IsNullOrWhiteSpace(name)) return;
            double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px);
            double.TryParse(tracking.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tr); double.TryParse(leading.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ld); double.TryParse(strokeWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var swd);
            var look = new TitleStyle(font.SelectedItem as string is { } f && f != "(Default)" ? f : "", bold.IsChecked == true, align.SelectedItem as string ?? "Center", tr, ld, string.IsNullOrWhiteSpace(stroke.Text) ? null : stroke.Text.Trim(), swd, shadow.IsChecked == true, string.IsNullOrWhiteSpace(gradient.Text) ? null : gradient.Text.Trim());
            styles.RemoveAll(x => x.Name == name.Trim()); styles.Add(new SavedTextStyle { Name = name.Trim(), FontSize = px is >= 12 and <= 256 ? px : 64, Color = color.Text ?? "#ffffff", Background = string.IsNullOrWhiteSpace(background.Text) ? null : background.Text.Trim(), Style = look.ToJson() });
            SaveTextStyles(styles); stylePick.ItemsSource = new[] { "(Custom)" }.Concat(styles.Select(x => x.Name)).ToList(); stylePick.SelectedItem = name.Trim();
        }));
        Grid.SetColumn(saveStyle, 2); styleRow.Children.Add(saveStyle);
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        buttons.Children.Add(Button("Cancel", () => dialog.Close()));
        buttons.Children.Add(Button("Create / apply", () =>
        {
            if (!double.TryParse(size.Text, CultureInfo.InvariantCulture, out var fontSize) || fontSize is < 12 or > 256 ||
                !double.TryParse(seconds.Text, CultureInfo.InvariantCulture, out var length) || !double.IsFinite(length) || length <= 0 || length > 86400 ||
                string.IsNullOrWhiteSpace(text.Text) || text.Text.Length > 4000)
            {
                error.Text = "Check the text, font size and duration.";
                return;
            }
            try
            {
                _ = Avalonia.Media.Color.Parse(color.Text ?? "");
            }
            catch (FormatException) { error.Text = "Enter a valid text color, such as #ffffff."; return; }
            double.TryParse(tracking.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var track);
            double.TryParse(leading.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lead);
            double.TryParse(strokeWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var strokePx);
            string? strokeColor = string.IsNullOrWhiteSpace(stroke.Text) ? null : stroke.Text.Trim(), bg = string.IsNullOrWhiteSpace(background.Text) ? null : background.Text.Trim();
            string? gradientTo = string.IsNullOrWhiteSpace(gradient.Text) ? null : gradient.Text.Trim();
            if ((strokeColor is not null && !Avalonia.Media.Color.TryParse(strokeColor, out _)) || (bg is not null && !Avalonia.Media.Color.TryParse(bg, out _)) || (gradientTo is not null && !Avalonia.Media.Color.TryParse(gradientTo, out _))) { error.Text = "Stroke, gradient and background need colors like #000000 or #B0000000."; return; }
            var style = new TitleStyle(font.SelectedItem as string is { } f && f != "(Default)" ? f : "", bold.IsChecked == true, align.SelectedItem as string ?? "Center", Math.Clamp(track, -10, 100), Math.Clamp(lead, 0, 4), strokeColor, Math.Clamp(strokePx, 0, 20), shadow.IsChecked == true, gradientTo);
            dialog.Close(new TitleOptions(text.Text, fontSize, color.Text!, length, bg, style));
        }));
        return await dialog.ShowDialog<TitleOptions?>(this);
    }

    internal sealed class SavedTextStyle { public string Name { get; set; } = ""; public double FontSize { get; set; } = 64; public string Color { get; set; } = "#ffffff"; public string? Background { get; set; } public string? Style { get; set; } }
    static string TextStylesPath => Path.Combine(SettingsDirectory, "text-styles.json");
    static List<SavedTextStyle> LoadTextStyles() { try { return File.Exists(TextStylesPath) ? System.Text.Json.JsonSerializer.Deserialize<List<SavedTextStyle>>(File.ReadAllText(TextStylesPath)) ?? [] : []; } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return []; } }
    static void SaveTextStyles(List<SavedTextStyle> styles) { Directory.CreateDirectory(SettingsDirectory); File.WriteAllText(TextStylesPath, System.Text.Json.JsonSerializer.Serialize(styles)); }
    sealed record TitleOptions(string Text, double FontSize, string Color, double Duration, string? Background = null, TitleStyle? Style = null);
}
