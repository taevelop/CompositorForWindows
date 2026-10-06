using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Compositor.Core;
namespace Compositor.App;
public sealed class ExtendedAdjustmentWindow : Window
{
    private readonly EditorSession session;
    private readonly bool pixelEdit;
    private readonly Document original, editing;
    private readonly Layer layer;
    private readonly bool grain;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private bool ready,closed,finished;
    internal readonly List<Slider> Sliders=[];
    private readonly List<TextBox> inputs=[];
    internal readonly CheckBox Preserve=new(){Content="Preserve luminosity",IsChecked=true,Margin=new(4)};
    internal readonly TextBox SeedInput=new(){Width=140,HorizontalAlignment=HorizontalAlignment.Left};
    internal readonly CheckBox PreviewEnabled=new(){Content="Preview changes",IsChecked=true,Margin=new(4,10,4,4)};
    private readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4),MinHeight=32};
    public ExtendedAdjustmentWindow(EditorSession session,bool grain,bool create, bool pixelEdit = false)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        this.pixelEdit=pixelEdit; this.session=session;this.grain=grain;original=session.Document;
        if(create)
        {
            var active=session.ActiveLayer;
            layer=grain?Layer.GrainLayer(original.Width,original.Height,active?.IsGroup==true?active.Id:active?.ParentId):Layer.ColorBalanceLayer(original.Width,original.Height,active?.IsGroup==true?active.Id:active?.ParentId);
            int index=active is null?original.Layers.Length:original.Layers.IndexOf(active)+1;
            editing=original with {Layers=original.Layers.Insert(index,layer)};editing.Validate();
        }
        else
        {
            layer=session.ActiveLayer??throw new InvalidOperationException("Select an adjustment layer.");
            if(grain?layer.Grain is null:layer.ColorBalance is null)throw new InvalidOperationException("Select the matching adjustment.");
            editing=original;
        }
        Title=grain?"Grain":"Color balance";Width=450;Height=grain?530:690;MinWidth=360;MinHeight=350;
        Preserve.Foreground=PreviewEnabled.Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Ink");
        Style=(Style)Application.Current.FindResource(typeof(Window));ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new(18)};Content=root;
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        footer.Children.Add(PreviewEnabled);footer.Children.Add(feedback);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);
        void Button(string title,Action action,bool cancel=false)
        {
            var b=new Button{Content=title,IsCancel=cancel,Style=(Style)FindResource("CompactButton")};b.Click+=(_,_)=>action();actions.Children.Add(b);
        }
        Button("Reset",()=>{if(grain)SetSettings(new GrainAdjustment());else SetSettings(new ColorBalanceAdjustment());});
        Button("Cancel",Close,true);Button("Apply",ApplyEdit);
        var body=new StackPanel();root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        body.Children.Add(new TextBlock{Text=Title,FontWeight=FontWeights.SemiBold,Margin=new(4,0,4,10)});
        string[] labels=grain?["Amount","Size","Roughness"]:["Shadows · Cyan / Red","Shadows · Magenta / Green","Shadows · Yellow / Blue",
            "Midtones · Cyan / Red","Midtones · Magenta / Green","Midtones · Yellow / Blue",
            "Highlights · Cyan / Red","Highlights · Magenta / Green","Highlights · Yellow / Blue"];
        for(int i=0;i<labels.Length;i++)
        {
            var slider=new Slider{Minimum=grain?(i==1?.5:0):-100,Maximum=grain?(i==1?20:100):100,Style=(Style)FindResource("EditorSlider")};
            Sliders.Add(slider);
            var line=new DockPanel();body.Children.Add(line);line.Children.Add(new TextBlock{Text=labels[i]});
            var input=new TextBox{Width=70,HorizontalAlignment=HorizontalAlignment.Right};
            input.SetBinding(TextBox.TextProperty,new Binding("Value"){Source=slider,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=true,StringFormat="0.##"});
            inputs.Add(input);line.Children.Add(input);body.Children.Add(slider);input.TextChanged+=(_,_)=>Queue();
        }
        if(grain)
        {
            body.Children.Add(new TextBlock{Text="Seed",Margin=new(4,8,4,0)});body.Children.Add(SeedInput);
            var random=new Button{Content="New pattern",HorizontalAlignment=HorizontalAlignment.Left,Style=(Style)FindResource("CompactButton")};
            random.Click+=(_,_)=>SeedInput.Text=((uint)Random.Shared.NextInt64(0,1L<<32)).ToString(CultureInfo.InvariantCulture);
            body.Children.Add(random);SeedInput.TextChanged+=(_,_)=>Queue();SetSettings(layer.Grain!);
        }
        else {body.Children.Add(Preserve);Preserve.Click+=(_,_)=>Preview();SetSettings(layer.ColorBalance!);}
        PreviewEnabled.Click+=(_,_)=>Preview();
        session.Begin();session.ActiveLayerId=layer.Id;ready=true;timer.Tick+=(_,_)=>Preview();Closed+=(_,_)=>CancelEdit();Preview();
    }
    private void Queue(){if(!ready||closed)return;timer.Stop();timer.Start();}
    private Layer Read()
    {
        if(inputs.Any(Validation.GetHasError))throw new InvalidOperationException("Enter valid numbers.");
        if(grain)
        {
            if(!uint.TryParse(SeedInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out uint seed))throw new InvalidOperationException("Seed must be 0..4294967295.");
            var s=new GrainAdjustment(Sliders[0].Value,Sliders[1].Value,Sliders[2].Value,seed);s.Validate();return layer with {Grain=s};
        }
        var v=Sliders.Select(s=>s.Value).ToArray();
        var settings=new ColorBalanceAdjustment(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],Preserve.IsChecked==true);settings.Validate();
        return layer with {ColorBalance=settings};
    }
    internal void SetSettings(GrainAdjustment s){Sliders[0].Value=s.Amount;Sliders[1].Value=s.Size;Sliders[2].Value=s.Roughness;SeedInput.Text=s.Seed.ToString(CultureInfo.InvariantCulture);if(ready)Preview();}
    internal void SetSettings(ColorBalanceAdjustment s){for(int i=0;i<9;i++)Sliders[i].Value=s.Values[i];Preserve.IsChecked=s.PreserveLuminosity;if(ready)Preview();}
    internal bool Preview()
    {
        timer.Stop();if(!ready||closed)return false;
        try{var current=Read();session.Preview(PreviewEnabled.IsChecked==true?editing.Replace(current):original);feedback.Text=pixelEdit ? "Apply changes image pixels; Undo restores them." : "Source pixels stay unchanged.";return true;}
        catch(Exception e){session.Preview(original);feedback.Text=e.Message;return false;}
    }
    internal void ApplyEdit(){if(!Preview())return;try { session.Preview(editing.Replace(Read()));session.Commit();finished=true;Close(); } catch(Exception error){session.Preview(original);feedback.Text=error.Message;}}
    internal void CancelEdit(){if(closed)return;closed=true;timer.Stop();if(!finished&&ready)session.Cancel();}
}
