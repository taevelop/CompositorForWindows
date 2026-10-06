using System.Windows;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void AddInvert(object sender,RoutedEventArgs e)=>Safe(()=>
    {
        var d=session.Document;var active=session.ActiveLayer;
        var layer=Layer.InvertLayer(d.Width,d.Height,active?.IsGroup==true?active.Id:active?.ParentId);
        int index=active is null?d.Layers.Length:d.Layers.IndexOf(active)+1;
        session.Apply(x=>x with {Layers=x.Layers.Insert(index,layer)});
        session.ActiveLayerId=layer.Id;Refresh();Canvas.Focus();
    });
    private void AddBlackWhite(object sender,RoutedEventArgs e)=>OpenBlackWhite(true);
    private void EditBlackWhite(object sender,RoutedEventArgs e)=>OpenBlackWhite(false);
    private void OpenBlackWhite(bool create)=>Safe(()=>
    {
        var dialog=new BlackWhiteWindow(session,create){Owner=this};
        try{dialog.ShowDialog();}finally{dialog.CancelEdit();Refresh();Canvas.Focus();}
    });
    private void AddColorBalance(object sender,RoutedEventArgs e)=>OpenExtended(false,true);
    private void EditColorBalance(object sender,RoutedEventArgs e)=>OpenExtended(false,false);
    private void AddGrain(object sender,RoutedEventArgs e)=>OpenExtended(true,true);
    private void EditGrain(object sender,RoutedEventArgs e)=>OpenExtended(true,false);
    private void OpenExtended(bool grain,bool create)=>Safe(()=>{var dialog=new ExtendedAdjustmentWindow(session,grain,create){Owner=this};try{dialog.ShowDialog();}finally{dialog.CancelEdit();Refresh();Canvas.Focus();}});}
