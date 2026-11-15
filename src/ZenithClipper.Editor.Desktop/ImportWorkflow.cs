using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace ZenithClipper.Editor.Desktop;
public sealed partial class EditorWindow
{
    internal string? LastImportError {get;private set;}
    internal int ImportErrorPresentations {get;private set;}
    internal async Task<IReadOnlyList<Guid>> ImportPathsAsync(IEnumerable<string> paths)
    {
        var destination=session;var imported=new List<Guid>();var failures=new List<string>();LastImportError=null;
        foreach(var path in paths)
        {
            if(!ReferenceEquals(destination,session)){status.Text="Import cancelled because another project was opened.";return imported;}
            if(string.Equals(Path.GetExtension(path),".psd",StringComparison.OrdinalIgnoreCase)&&await AskPsdLayers(path))
            {
                try{var ids=ImportPsdLayers(path);imported.AddRange(ids);status.Text=$"Imported {ids.Count} Photoshop layer(s) as the sequence \"{Path.GetFileNameWithoutExtension(path)}\".";continue;}
                catch(Exception ex)when(ex is InvalidDataException or EndOfStreamException or IOException){failures.Add(Path.GetFileName(path)+"\n"+ex.Message);continue;}
            }
            try{status.Text="Reading "+Path.GetFileName(path);var media=await Probe(path);CheckImportDestination(destination);AddImportedMedia(media);imported.Add(media.Id);library.SelectedItem=((IEnumerable<MediaRow>)library.ItemsSource!).FirstOrDefault(row=>row.Id==media.Id);}
            catch(OperationCanceledException){throw;}
            catch(Exception ex){if(!ReferenceEquals(destination,session)){status.Text="Import cancelled because another project was opened.";return imported;}failures.Add(Path.GetFileName(path)+"\n"+ex.Message);}
        }
        if(imported.Count>0)status.Text=$"Imported {imported.Count} file(s). Add to timeline to create linked video and audio tracks.";
        if(failures.Count>0){LastImportError=string.Join("\n\n",failures.Take(8));ImportErrorPresentations++;status.Text=$"{failures.Count} file(s) could not be imported.";if(!HeadlessVerification)await CreateImportErrorDialog(LastImportError).ShowDialog(this);}
        return imported;
    }
    /// <summary>Premiere's Import Layered File prompt: layers as a sequence, or one merged image.</summary>
    async Task<bool> AskPsdLayers(string path)
    {
        if(HeadlessVerification)return true;
        bool layers=true;var dialog=new Window{Title="Import layered file",Width=420,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,CanResize=false,Background=PanelBrush,Foreground=TextBrush};
        var panel=new StackPanel{Margin=new Thickness(20),Spacing=10};dialog.Content=panel;
        panel.Children.Add(new TextBlock{Text="Import "+Path.GetFileName(path),FontSize=15,FontWeight=FontWeight.SemiBold});
        panel.Children.Add(new TextBlock{Text="Bring in each Photoshop layer on its own track in a new sequence, or one flattened image.",TextWrapping=TextWrapping.Wrap,Foreground=SoftTextBrush,FontSize=12});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        var seq=Button("Layers as sequence",()=>{layers=true;dialog.Close();});seq.Background=AccentBrush;seq.Foreground=InkBrush;seq.Classes.Add("primary");
        buttons.Children.Add(seq);buttons.Children.Add(Button("Merged image",()=>{layers=false;dialog.Close();}));panel.Children.Add(buttons);
        await dialog.ShowDialog(this);return layers;
    }
    internal Window CreateImportErrorDialog(string message)
    {
        var dialog=new Window{Title="Media could not be imported",Width=620,Height=360,MinWidth=420,MinHeight=240,Background=CanvasBrush,Foreground=TextBrush,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(20)};
        layout.Children.Add(new TextBlock{Text="Check the file or media metadata",FontSize=17,FontWeight=FontWeight.SemiBold,Margin=new Thickness(0,0,0,14)});
        var details=new TextBlock{Text=message+"\n\nYour original files have not been changed.",TextWrapping=TextWrapping.Wrap,FontSize=12,LineHeight=19};var scroll=new ScrollViewer{Content=details};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        var close=Button("Close",()=>dialog.Close());close.HorizontalAlignment=HorizontalAlignment.Right;close.MinWidth=90;close.Margin=new Thickness(0,14,0,0);Grid.SetRow(close,2);layout.Children.Add(close);dialog.Content=layout;return dialog;
    }
    void UnlinkSelected()
    {
        if(selectedClip is not {} id)return;Change("Unlink video and audio",p=>ZenithClipper.Editing.EditCommands.Unlink(p,p.ActiveSequenceId,id));
    }
    void RelinkSelected()
    {
        if(selectedClip is not {} id)return;Change("Link source video and audio",p=>ZenithClipper.Editing.EditCommands.Relink(p,p.ActiveSequenceId,id));
    }
}
