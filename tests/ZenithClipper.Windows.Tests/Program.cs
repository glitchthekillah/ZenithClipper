using ZenithClipper.Windows;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Contains("--metadata-only"))return DurationMetadataChecks.Run();
        global::ApplicationConfiguration.Initialize();
        int failed=DurationMetadataChecks.Run();
        foreach(var (name,create) in new (string,Func<Form>)[]{("controls",()=>new PanelForm()),("editor",()=>new EditorForm(null))})
        {
            try
            {
                using var form=create();form.CreateControl();form.PerformLayout();
                if(form.Controls.Count==0 && form is not PanelForm) throw new Exception("No controls constructed"); // the panel is a backdrop; its widgets are owned windows created on show
                var split=form.Controls.OfType<SplitContainer>().FirstOrDefault();
                if(split!=null && split.Panel2.Width<100) throw new Exception("Editor controls panel has no usable width");
                Console.WriteLine($"PASS Windows {name} construction and layout: {form.ClientSize}");
            }
            catch(Exception ex) { failed++;Console.WriteLine($"FAIL Windows {name}: {ex}"); }
        }
        failed+=WidgetChecks.Run();
        failed+=CaptureBorderCheck();
        if(args.Length>0) failed+=Task.Run(()=>MediaChecks.RunAsync(args[0])).GetAwaiter().GetResult();
        return failed==0?0:1;
    }

    /// <summary>Windows' yellow capture border must stay off for everyone who builds from source.</summary>
    static int CaptureBorderCheck()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
        {
            string cpp=Path.Combine(d.FullName,"src","ZenithClipper.Native.Video","native","wgc_capture.cpp");
            if(!File.Exists(cpp)) continue;
            if(File.ReadAllText(cpp).Contains("IsBorderRequired(false)")){Console.WriteLine("PASS WGC capture disables the yellow border");return 0;}
            Console.WriteLine("FAIL wgc_capture.cpp no longer disables the yellow capture border");return 1;
        }
        Console.WriteLine("FAIL wgc_capture.cpp not found");return 1;
    }
}