using System.Windows;
using Compositor.Core;

namespace Compositor.App;

public partial class MainWindow
{
    private sealed record LayerRow(Layer Layer, int Depth, bool Visible, bool Expanded)
    {
        public Guid Id => Layer.Id;
        public string Label => (Layer.IsGroup ? "Group · " : "") + Layer.Name;
        public string Disclosure => Layer.IsGroup ? Expanded ? "−" : "+" : "";
        public bool CanExpand => Layer.IsGroup;
        public Visibility ExpandVisibility => Layer.IsGroup ? Visibility.Visible : Visibility.Hidden;
        public Thickness Indent => new(Depth * 14, 0, 0, 0);
        public double RowOpacity => Visible ? 1 : .45;
    }
    private sealed record ParentOption(Guid? Id, string Label);
    private readonly HashSet<Guid> collapsedGroups = [];
    private Guid? panelDocumentId;

    private void RefreshHierarchy()
    {
        if (panelDocumentId != session.Document.Id) { collapsedGroups.Clear(); panelDocumentId = session.Document.Id; }
        collapsedGroups.IntersectWith(session.Document.Layers.Where(l => l.IsGroup).Select(l => l.Id));
        // Undo or file load may select a child of a collapsed group; reveal that selection.
        {
            var byId = session.Document.Layers.ToDictionary(l => l.Id); var parent = session.ActiveLayer?.ParentId;
            while (parent is Guid id && byId.TryGetValue(id, out var group)) { collapsedGroups.Remove(id); parent = group.ParentId; }
        }
        var entries = LayerHierarchy.Entries(session.Document, topFirst: true, collapsed: collapsedGroups);
        var rows = entries.Select(e => new LayerRow(e.Layer, e.Depth, e.Visible, !collapsedGroups.Contains(e.Layer.Id))).ToArray();
        Layers.ItemsSource = rows; Layers.SelectedItem = rows.FirstOrDefault(r => r.Id == session.ActiveLayerId);
        var active = session.ActiveLayer;
        bool isGroup = active?.IsGroup == true;
        LayerBlend.IsEnabled = TransformPanel.IsEnabled = !isGroup && active is not null;
        GroupHint.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
        WrapGroupButton.IsEnabled = active is not null; UngroupButton.IsEnabled = isGroup;
        var choices = new List<ParentOption> { new(null, "Document root") };
        if (active is not null)
        {
            var excluded = LayerHierarchy.Subtree(session.Document, active.Id);
            choices.AddRange(LayerHierarchy.Entries(session.Document, topFirst: true).Where(e => e.Layer.IsGroup && !excluded.Contains(e.Layer.Id))
                .Select(e => new ParentOption(e.Layer.Id, new string(' ', e.Depth * 2) + e.Layer.Name)));
        }
        ParentGroup.ItemsSource = choices; ParentGroup.SelectedItem = choices.FirstOrDefault(p => p.Id == active?.ParentId);
        ParentGroup.IsEnabled = MoveToGroupButton.IsEnabled = active is not null;
        MoveOutButton.IsEnabled = active?.ParentId is not null;
    }
    private void ToggleGroup(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (sender is not FrameworkElement { Tag: Guid id }) return;
        if (!session.Document.Layers.Any(l => l.Id == id && l.IsGroup)) return;
        if (!collapsedGroups.Remove(id))
        {
            if (session.ActiveLayerId is Guid active && LayerHierarchy.Subtree(session.Document, id).Contains(active)) session.ActiveLayerId = id;
            collapsedGroups.Add(id);
        }
        Refresh(); e.Handled = true;
    });
    private Guid? InsertionParent => session.ActiveLayer is { IsGroup: true } group ? group.Id : session.ActiveLayer?.ParentId;
    private void AddGroup(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var group = Layer.Group($"Group {session.Document.Layers.Count(l => l.IsGroup) + 1}", session.Document.Width, session.Document.Height, InsertionParent);
        session.Apply(d => d with { Layers = d.Layers.Add(group) }); session.ActiveLayerId = group.Id; Refresh();
    });
    private void WrapInGroup(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { } layer) return;
        var group = Layer.Group($"Group {session.Document.Layers.Count(l => l.IsGroup) + 1}", session.Document.Width, session.Document.Height);
        session.Apply(d => LayerHierarchy.Wrap(d, layer.Id, group)); session.ActiveLayerId = group.Id; Refresh();
    });
    private void Ungroup(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { IsGroup: true } group) return;
        var child = session.Document.Layers.LastOrDefault(l => l.ParentId == group.Id);
        session.Apply(d => LayerHierarchy.Ungroup(d, group.Id));
        if (child is not null) session.ActiveLayerId = child.Id;
        Refresh();
    });
    private void MoveToGroup(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { } layer || ParentGroup.SelectedItem is not ParentOption parent) return;
        session.Apply(d => LayerHierarchy.Reparent(d, layer.Id, parent.Id));
        if (parent.Id is Guid id) collapsedGroups.Remove(id); Refresh();
    });
    private void MoveOutOfGroup(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { ParentId: Guid parent } layer) return;
        var group = session.Document.Layers.First(l => l.Id == parent);
        session.Apply(d => LayerHierarchy.Reparent(d, layer.Id, group.ParentId)); Refresh();
    });
}
