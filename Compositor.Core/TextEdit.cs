using System.Collections.Immutable;
using System.Globalization;

namespace Compositor.Core;

/// <summary>Original point/paragraph text draft. Null LayerId creates a new layer.</summary>
public sealed record TextDraft(Guid DocumentId, Guid? LayerId, PointD Origin, LayerTextStyle Style, LayerTransform? Transform = null);

/// <summary>Prepare on a worker from immutable state, then apply once on the session's UI thread.</summary>
public static class TextEdit
{
    public sealed class Request
    {
        internal EditorSession Owner { get; }
        public Document Before { get; }
        public TextDraft Draft { get; }
        public bool NeedsRender { get; }
        internal Guid? ActiveId { get; }
        internal bool EditMask { get; }
        internal ImmutableHashSet<Guid> Selected { get; }
        internal long Generation { get; }
        internal Request(EditorSession owner, TextDraft draft, bool needsRender)
        {
            Owner = owner; Before = owner.Document; Draft = draft; NeedsRender = needsRender;
            ActiveId = owner.ActiveLayerId; EditMask = owner.EditMask;
            Selected = owner.SelectedLayerIds; Generation = owner.TransactionGeneration;
        }
        internal bool Matches(EditorSession session) => ReferenceEquals(Owner, session) && !session.InTransaction &&
            ReferenceEquals(Before, session.Document) && session.TransactionGeneration == Generation &&
            session.ActiveLayerId == ActiveId && session.EditMask == EditMask && session.SelectedLayerIds.SetEquals(Selected);
    }

    public sealed class Prepared
    {
        private readonly Request request;
        public Document Document { get; }
        public Guid? LayerId { get; }
        public bool Changed => !ReferenceEquals(Document, request.Before);
        internal Prepared(Request request, Document document, Guid? layerId) { this.request = request; Document = document; LayerId = layerId; }
        public bool Apply(EditorSession session, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!request.Matches(session)) throw new InvalidOperationException("The text edit belongs to an earlier document or editing state.");
            if (!Changed) return false;
            session.Apply(_ => { session.ActiveLayerId = LayerId; session.EditMask = false; return Document; });
            return true;
        }
    }

    public static Request Capture(EditorSession session, TextDraft draft)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        if (draft.DocumentId != session.Document.Id) throw new InvalidOperationException("The text draft belongs to another document.");
        draft.Style.Validate(); draft.Transform?.Validate();
        if (!double.IsFinite(draft.Origin.X) || !double.IsFinite(draft.Origin.Y) || Math.Abs(draft.Origin.X) > 1_000_000 || Math.Abs(draft.Origin.Y) > 1_000_000)
            throw new InvalidDataException("Invalid text origin.");
        bool render;
        if (draft.LayerId is { } id)
        {
            var layer = session.Document.Layers.FirstOrDefault(l => l.Id == id);
            if (layer?.Text is null) throw new InvalidOperationException("The target is no longer editable text.");
            render = layer.Text != draft.Style || (draft.Transform is { } transform && transform != layer.Transform);
        }
        else
        {
            render = !string.IsNullOrWhiteSpace(draft.Style.Content);
            if (render && session.Document.Layers.Length >= 10000) throw new InvalidDataException("Too many layers.");
        }
        return new(session, draft, render);
    }

    public static Prepared Prepare(Request request, Raster? pixels, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var document = request.Before; var draft = request.Draft;
        if (!request.NeedsRender) return new(request, document, draft.LayerId);
        ArgumentNullException.ThrowIfNull(pixels);
        Limits.CheckDimensions(pixels.Width, pixels.Height);
        if (draft.Style.BoxSize is { } box && (pixels.Width != Math.Ceiling(box.Width) || pixels.Height != Math.Ceiling(box.Height)))
            throw new InvalidDataException("Rendered text does not match its paragraph box.");
        document.Validate(); Document next; Guid layerId;
        if (draft.LayerId is { } id)
        {
            var original = document.Layers.First(l => l.Id == id);
            var transform = draft.Transform ?? original.Transform;
            if (draft.Transform is null || draft.Style.BoxSize is null)
            {
                var anchor = transform.ToDocument(new(0, 0), original.Pixels.Width, original.Pixels.Height);
                transform = transform with { Width = pixels.Width * transform.Width / original.Pixels.Width, Height = pixels.Height * transform.Height / original.Pixels.Height };
                var moved = transform.ToDocument(new(0, 0), pixels.Width, pixels.Height);
                transform = transform with { X = transform.X + anchor.X - moved.X, Y = transform.Y + anchor.Y - moved.Y };
            }
            // Re-rendering changes the source grid; it does not move or stretch
            // an existing mask. Future linked transforms can follow this placement.
            var mask = original.Mask;
            if (mask is { Placement: null }) mask = mask with { Placement = original.Transform };
            var edited = original with { Pixels = pixels, Transform = transform, Mask = mask, Text = draft.Style };
            next = document.Replace(edited); layerId = id;
        }
        else
        {
            var active = request.ActiveId is { } idActive ? document.Layers.FirstOrDefault(l => l.Id == idActive) : null;
            var transform = draft.Transform ?? new(draft.Origin.X, draft.Origin.Y, pixels.Width, pixels.Height);
            var layer = new Layer(Guid.NewGuid(), LayerName(draft.Style.Content), pixels, transform, ParentId: active?.IsGroup == true ? active.Id : active?.ParentId) { Text = draft.Style };
            next = document with { Layers = document.Layers.Insert(active is null ? document.Layers.Length : document.Layers.IndexOf(active) + 1, layer) };
            layerId = layer.Id;
        }
        next.Validate();
        if (EditorSession.UndoBytesRequired(document, next) > EditorSession.MaxHistoryBytes)
            throw new InvalidDataException("Text edit exceeds the Undo memory budget.");
        cancellation.ThrowIfCancellationRequested();
        return new(request, next, layerId);
    }

    private static string LayerName(string content)
    {
        string flat = string.Join(" ", content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length == 0) return "Text";
        var elements = StringInfo.ParseCombiningCharacters(flat);
        return elements.Length <= 40 ? flat : flat[..elements[40]];
    }
}
