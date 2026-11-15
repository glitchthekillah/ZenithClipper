using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
namespace ZenithClipper.Editor.Desktop;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        if(Array.IndexOf(args,"--mlt-lab") is int lab and >=0){RenderLab.Run(args[lab+1],args[lab+2]);return;}
        // Motion-tracking helper: runs in its own process so the editor never loads OpenCV.
        // --track-rect <media> <width> <height> <fpsNum> <fpsDen> <sourceIn> <frames> <x> <y> <w> <h> <out.txt>
        if(Array.IndexOf(args,"--track-rect") is int tr and >=0)
        {
            Environment.SetEnvironmentVariable("ZENITHCLIPPER_MLT_OPENCV","1");
            var a=args[(tr+1)..];int I(int i)=>int.Parse(a[i],System.Globalization.CultureInfo.InvariantCulture);
            var fps=new ZenithClipper.Editing.FrameRate(I(3),I(4));
            var result=ZenithClipper.Mlt.MltAnalysis.TrackRect(a[0],I(1),I(2),fps,long.Parse(a[5]),long.Parse(a[6]),(I(7),I(8),I(9),I(10)),new Progress<double>(v=>Console.WriteLine("progress "+v.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture))));
            File.WriteAllLines(a[11],result.Select(r=>FormattableString.Invariant($"{r.Frame} {r.X} {r.Y} {r.W} {r.H}")));
            Console.WriteLine($"tracked {result.Count}");
            return;
        }
        if(Array.IndexOf(args,"--psd-dump") is int psd and >=0)
        {
            var (w,h,layers)=PsdReader.Read(args[psd+1]);Directory.CreateDirectory(args[psd+2]);Console.WriteLine($"{w}x{h} {layers.Count} layers");
            for(int i=0;i<layers.Count;i++){var l=layers[i];Console.WriteLine($"{i} '{l.Name}' {l.Left},{l.Top} {l.Width}x{l.Height} opacity {l.Opacity:0.###} visible {l.Visible}");PngWriter.Write(Path.Combine(args[psd+2],$"{i}.png"),l.Width,l.Height,l.Rgba);}
            return;
        }
        if(Array.IndexOf(args,"--showcase") is int sc and >=0){Showcase.Run(args[sc+1],args[sc+2]);return;}
        if(Array.IndexOf(args,"--user-flow") is int uf and >=0){UserFlowTest.Run(args[uf+1],args[uf+2]);return;}
        if(args.Contains("--metadata-regression-test")) { MediaMetadata.VerifyMetadata(); return; }
        if (args.Contains("--headless-smoke"))
        {
            AppBuilder.Configure<EditorApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            int ReadInt(string option,int fallback){int index=Array.IndexOf(args,option);return index>=0&&index+1<args.Length?int.Parse(args[index+1],System.Globalization.CultureInfo.InvariantCulture):fallback;}
            int width=ReadInt("--width",1440),height=ReadInt("--height",920); if(width<1000||height<650||width>3840||height>2160)throw new ArgumentException("Snapshot size must be 1000×650 through 3840×2160.");
            var scaleIndex=Array.IndexOf(args,"--scale");double scale=scaleIndex>=0?double.Parse(args[scaleIndex+1],System.Globalization.CultureInfo.InvariantCulture):1;if(!double.IsFinite(scale)||scale<.5||scale>2)throw new ArgumentException("Render scale must be 0.5–2.");
            if(int.TryParse(Environment.GetEnvironmentVariable("ZENITH_DEMO_TRACKHEIGHT"),out var th))EditorWindow.TrackHeight=th;
            var window = new EditorWindow([]){Width=width,Height=height,HeadlessVerification=true};
            window.VerifyHeadlessLayout();
            TitleRasterizer.VerifyRasterization();
            EditorWindow.VerifyAutoCaptions();
            EditorWindow.VerifyIconMotion();
            MediaMetadata.VerifyMetadata();
            // This virtual headless surface never opens a window on the user's desktop.
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
            window.VerifyTimelineAlignment();
            if (window.Content is null)
                throw new InvalidOperationException("Editor content was not created.");
            var snapshotIndex = Array.IndexOf(args, "--snapshot");
            if (snapshotIndex >= 0)
            {
                if (snapshotIndex + 1 >= args.Length || !Path.IsPathFullyQualified(args[snapshotIndex + 1]))
                    throw new ArgumentException("--snapshot requires an absolute PNG output path.");
                var path = Path.GetFullPath(args[snapshotIndex + 1]);
                if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Snapshot output must be a PNG file.");
                if(!args.Contains("--empty")){window.PopulateHeadlessDemo();MediaPreviews.WaitAll(TimeSpan.FromSeconds(60));}
                window.Measure(new Size(width, height));
                window.Arrange(new Rect(0, 0, width, height));
                Dispatcher.UIThread.RunJobs();
                window.VerifyTimelineAlignment();
                window.VerifyDesignBehavior();
                using var snapshot = new RenderTargetBitmap(new PixelSize((int)Math.Round(width*scale), (int)Math.Round(height*scale)), new Vector(96*scale, 96*scale));
                snapshot.Render(window);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                snapshot.Save(path, PngBitmapEncoderOptions.Default);
                Console.WriteLine("Headless UI snapshot saved: " + path);
            }
            Console.WriteLine("Editor headless UI smoke passed: window, media library, preview, inspector and timeline constructed.");
            window.Close();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<EditorApplication>().UsePlatformDetect().LogToTrace();
}
public sealed class EditorApplication : Application
{
    public override void Initialize()
    {
        var palette = new ColorPaletteResources { Accent = Avalonia.Media.Color.Parse("#FF6B4A"), RegionColor = Avalonia.Media.Color.Parse("#16171A") };
        var theme = new FluentTheme(); theme.Palettes[Avalonia.Styling.ThemeVariant.Dark] = palette; Styles.Add(theme);
        EditorWindow.FluentPalette = palette; EditorWindow.ApplyLook(ZenithClipper.Appearance.Load()); EditorWindow.WatchLook();
        EditorTheme.Apply(this);
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new EditorWindow(Program.Arguments);
        base.OnFrameworkInitializationCompleted();
    }
}
