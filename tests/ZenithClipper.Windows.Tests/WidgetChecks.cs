using System.Reflection;
using ZenithClipper.Windows;
using ZenithClipper.Windows.Ui;

static class WidgetChecks
{
    public static int Run()
    {
        int failed=0;
        using var panel=new PanelForm();
        try
        {
            if (typeof(PanelForm).GetField("settingsBuilt",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(panel) is not false)
                throw new Exception("Hidden settings were built before the first popup paint.");
            Console.WriteLine("PASS panel defers hidden settings construction until requested");
        }
        catch(Exception ex){failed++;Console.WriteLine("FAIL deferred settings: "+ex.Message);}
        try
        {
            if (typeof(PanelForm).GetField("homeBuilt",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(panel) is not false)
                throw new Exception("Rail sections were constructed before the header painted.");
            Console.WriteLine("PASS panel defers rail sections until the header paints");
        }
        catch(Exception ex){failed++;Console.WriteLine("FAIL deferred home cards: "+ex.Message);}
        typeof(PanelForm).GetMethod("EnsureHome",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(panel,null);
        typeof(PanelForm).GetMethod("EnsureSettings",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(panel,null);
        foreach(string field in new[]{"railWidget","settingsWidget"})
        {
            var widget=(Widget)typeof(PanelForm).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel)!;
            try
            {
                // Lay the rail and the settings window out at a short 1080p-ish height without showing the overlay or starting its engine.
                widget.Location=new Point(-32000,-32000);widget.Opacity=0;
                widget.Size=new Size(widget.Width,Theme.S(860));widget.Show();widget.PerformLayout();
                var content=widget.Controls[0];content.PerformLayout();
                foreach(Control child in Descendants(content))
                {
                    if(!child.Visible||child.Parent==null||child.Parent is ScrollableControl {AutoScroll:true})continue;
                    if(child.Right>child.Parent.ClientSize.Width+1)
                        throw new Exception($"{child.GetType().Name} in {child.Parent.GetType().Name} overflows width: {child.Right}px / {child.Parent.ClientSize.Width}px.");
                }
                if(Environment.GetEnvironmentVariable("ZENITHCLIPPER_LAYOUT_ARTIFACTS") is {Length:>0} output)
                {
                    Directory.CreateDirectory(output);using var bitmap=new Bitmap(content.Width,content.Height);
                    content.DrawToBitmap(bitmap,new Rectangle(Point.Empty,content.Size));
                    bitmap.Save(Path.Combine(output,field+".png"),System.Drawing.Imaging.ImageFormat.Png);
                }
                Console.WriteLine($"PASS {field}: {widget.ClientSize.Width}x{widget.ClientSize.Height}, nothing overflows its row");
            }
            catch(Exception ex){failed++;Console.WriteLine($"FAIL {field}: {ex.Message}");}
            finally{widget.Hide();}
        }
        try
        {
            foreach(var (field,keys,expected) in new[]{("clipKey",Keys.Alt|Keys.K,"Alt+K"),("panelKey",Keys.Control|Keys.Shift|Keys.J,"Ctrl+Shift+J"),("panelKey",Keys.Shift|Keys.Oemtilde,"Shift+~")})
            {
                var box=(HotkeyBox)typeof(PanelForm).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(panel)!;
                typeof(HotkeyBox).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(box,[EventArgs.Empty]);
                object?[] arguments=[new Message(),keys];
                typeof(HotkeyBox).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(box,arguments);
                if(box.Value!=expected)throw new Exception($"{field} captured '{box.Value}' instead of '{expected}'.");
            }
            var settings=(ZenithClipper.Settings)typeof(PanelForm).GetMethod("BuildFromControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,null)!;
            if(settings.ClipHotkey!="Alt+K"||settings.PanelHotkey!="Shift+~")throw new Exception("Custom shortcuts did not reach settings.");
            Console.WriteLine("PASS both settings shortcut controls capture and save custom bindings");
        }
        catch(Exception ex){failed++;Console.WriteLine("FAIL custom shortcut controls: "+ex);}
        try
        {
            using var box=new HotkeyBox();int changed=0;box.ValueChanged+=(_,_)=>changed++;
            var click=typeof(HotkeyBox).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var wndProc=typeof(HotkeyBox).GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic)!;
            click.Invoke(box,[EventArgs.Empty]);
            object?[] forwarded=[Message.Create(0,ZenithClipper.WindowsHotkeyCapture.ForwardMessage,0,(0xC0<<16)|4)];
            wndProc.Invoke(box,forwarded);
            if(box.Value!="Shift+~"||changed!=1)throw new Exception("Forwarded registered shortcut was not committed exactly once.");
            forwarded=[Message.Create(0,ZenithClipper.WindowsHotkeyCapture.ForwardMessage,0,(0x43<<16)|1)];
            wndProc.Invoke(box,forwarded);
            if(box.Value!="Shift+~"||changed!=1)throw new Exception("A stale forwarded shortcut changed a picker that was not listening.");
            click.Invoke(box,[EventArgs.Empty]);object?[] escape=[new Message(),Keys.Escape];
            typeof(HotkeyBox).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(box,escape);
            wndProc.Invoke(box,forwarded);
            if(box.Value!="Shift+~"||changed!=1)throw new Exception("Escape did not stop forwarded shortcut capture.");
            Console.WriteLine("PASS registered shortcut WM_APP capture commits once and ignores stale/cancelled input without native registration");
        }
        catch(Exception ex){failed++;Console.WriteLine("FAIL forwarded registered shortcut control: "+ex);}
        try
        {
            using var header=new RailHeader{Width=Theme.S(400),Height=Theme.S(52)};
            using var bitmap=new Bitmap(header.Width,header.Height);header.DrawToBitmap(bitmap,new Rectangle(Point.Empty,header.Size));
            var settingsBox=(Rectangle)typeof(RailHeader).GetField("settingsBox",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(header)!;
            var click=typeof(RailHeader).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic)!;int opened=0;header.SettingsClicked+=()=>opened++;
            var input=new MouseEventArgs(MouseButtons.Left,1,settingsBox.X+settingsBox.Width/2,settingsBox.Y+settingsBox.Height/2,0);
            click.Invoke(header,[input]);if(opened!=0)throw new Exception("Connecting header opened settings before the recorder answered.");
            header.Ready=true;click.Invoke(header,[input]);if(opened!=1)throw new Exception("Ready header did not open settings.");
            Console.WriteLine("PASS rail header blocks settings until settings and status are ready");
        }
        catch(Exception ex){failed++;Console.WriteLine("FAIL startup readiness: "+ex);}
        var folder = Path.Combine(Path.GetTempPath(), "zc-recent-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            using var recent = new RecentList();
            var settings = new ZenithClipper.Settings { ClipDirectory = folder, FfmpegPath = Path.Combine(folder, "missing-ffmpeg.exe") };
            var counts = new List<int>();
            recent.Counted += (_, _, total, _) => counts.Add(total);
            recent.RefreshClips(settings, null);
            string clip = Path.Combine(folder, "Clip-hotkey.mp4");
            File.WriteAllBytes(clip, [0]);
            recent.RefreshClips(settings, clip);
            recent.RefreshClips(settings, clip);
            string secondFolder = Path.Combine(folder, "other");
            Directory.CreateDirectory(secondFolder);
            recent.RefreshClips(settings with { ClipDirectory = secondFolder }, clip);
            if (!counts.SequenceEqual([0, 1, 0]))
                throw new Exception("The recent list did not refresh exactly once per save or folder change.");
            Console.WriteLine("PASS recent clips refresh after an external save and a folder change");
        }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL recent clips: " + ex); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        return failed;
    }
    static IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var d in Descendants(c))yield return d;}}
}
