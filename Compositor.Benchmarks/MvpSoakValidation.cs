using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

internal static class MvpSoakValidation
{
    public static void Run(string reportPath, int iterations)
    {
        if (iterations < 200 || iterations > 100_000) throw new ArgumentOutOfRangeException(nameof(iterations));
        string report = Path.GetFullPath(reportPath);
        File.Delete(report); // Do not leave a stale successful report after a failed rerun.
        string root = Path.Combine(Path.GetDirectoryName(report)!, "mvp-soak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var clock = Stopwatch.StartNew();
        var session = new EditorSession(NewDocument());
        using var viewport = new ViewportRenderer();
        var samples = new List<object>(); int roundTrips = 0, resets = 0;
        long maxHistory = 0; int maxHistoryCount = 0;
        for (int i = 0; i < iterations; i++)
        {
            var before = session.Document;
            var layer = before.Layers[i % before.Layers.Length];
            session.ActiveLayerId = layer.Id;
            byte[] originalPixels = layer.Pixels.ToRgba();
            var stroke = new BrushStroke(layer, new(i % 9 == 0 ? 800 : 96, .35, .7,
                (byte)(i * 31 % 256), (byte)(i * 71 % 256), (byte)(i * 17 % 256), i % 5 == 0), 1024, 1024);
            session.Begin();
            for (int point = 0; point < 4; point++)
            {
                stroke.Append(new(50 + (i * 37 + point * 71) % 924, 50 + (i * 53 + point * 43) % 924));
                session.Preview(before.Replace(layer with { Pixels = stroke.Pixels }));
                CompareViewport(viewport, session.Document, i);
            }
            session.Commit();
            Require(originalPixels.SequenceEqual(layer.Pixels.ToRgba()), "Brush mutated the old snapshot.");
            var painted = session.Document;
            if (!ReferenceEquals(before, painted))
            {
                session.Undo(); Require(ReferenceEquals(session.Document, before), "Undo did not restore the snapshot.");
                int redo = session.RedoCount;
                session.Apply(d => d.Replace(d.Layers[0] with { Name = d.Layers[0].Name }));
                Require(session.RedoCount == redo, "No-op destroyed Redo.");
                session.Begin(); session.Preview(before.Replace(layer with { Name = "Canceled" })); session.Cancel();
                Require(ReferenceEquals(session.Document, before) && session.RedoCount == redo, "Cancel changed history.");
                session.Redo(); Require(ReferenceEquals(session.Document, painted), "Redo did not restore the snapshot.");
            }
            if (i % 7 == 0)
            {
                var current = session.Document.Layers[i % 3];
                session.Apply(d => d.Replace(current with { Opacity = .2 + (i % 8) / 10.0,
                    Blend = (BlendMode)(i % Enum.GetValues<BlendMode>().Length),
                    Transform = current.Transform with { X = i % 21, Y = -(i % 13), Width = 960 + i % 65,
                        Height = 970 + i % 55, Rotation = i % 3 == 0 ? 17 : 0 } }));
            }
            if (i % 11 == 0)
                session.Apply(d => d with { Layers = d.Layers.RemoveAt(0).Add(d.Layers[0]) });
            if (i % 13 == 0)
            {
                var temporary = Layer.Blank("Temporary", 1024, 1024);
                session.Apply(d => d with { Layers = d.Layers.Add(temporary) });
                session.Apply(d => d with { Layers = d.Layers.Remove(temporary) });
            }
            CompareViewport(viewport, session.Document, i);
            maxHistory = Math.Max(maxHistory, session.HistoryRetainedBytes);
            maxHistoryCount = Math.Max(maxHistoryCount, session.UndoCount + session.RedoCount);
            Require(maxHistory <= EditorSession.MaxHistoryBytes && maxHistoryCount <= 100, "History budget exceeded.");
            if ((i + 1) % 25 == 0)
            {
                string project = Path.Combine(root, "working.comp");
                ProjectStore.Save(session.Document, session.ActiveLayerId, project); session.MarkSaved();
                var loaded = ProjectStore.Load(project);
                Require(loaded.ActiveLayerId == session.ActiveLayerId, "Saved selection changed.");
                CompareDocuments(session.Document, loaded.Document);
                var reopened = new EditorSession(loaded.Document);
                Require(!reopened.IsModified && !reopened.CanUndo, "Reopened document inherited session history.");
                string png = Path.Combine(root, "output.png"), jpeg = Path.Combine(root, "output.jpg");
                ImageCodec.Export(loaded.Document, png, false); ImageCodec.Export(loaded.Document, jpeg, true);
                using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(loaded.Document);
                CompareBytes(Pixels(image), ImageCodec.Load(png).ToRgba(), 1, "PNG output");
                var jpg = ImageCodec.Load(jpeg);
                Require(jpg.Width == 1024 && jpg.Height == 1024, "JPEG output dimensions changed.");
                Require(!session.IsModified, "Export changed the saved state.");
                roundTrips++;
            }
            if ((i + 1) % 200 == 0)
            {
                session.Load(NewDocument()); CompareViewport(viewport, session.Document, i);
                Require(session.HistoryRetainedBytes == 0 && !session.CanUndo && !session.CanRedo, "New document retained history.");
                resets++;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                using var process = Process.GetCurrentProcess();
                samples.Add(new { iteration = i + 1, elapsedSeconds = clock.Elapsed.TotalSeconds,
                    managedMiB = GC.GetTotalMemory(true) / 1048576.0, workingSetMiB = process.WorkingSet64 / 1048576.0,
                    privateMiB = process.PrivateMemorySize64 / 1048576.0, handles = process.HandleCount });
                Console.WriteLine($"PASS {i + 1}/{iterations}; {clock.Elapsed.TotalSeconds:F1}s");
            }
        }
        File.WriteAllText(report, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
            os = RuntimeInformation.OSDescription, passed = true, iterations, roundTrips, resets,
            elapsedSeconds = clock.Elapsed.TotalSeconds, maxHistoryMiB = maxHistory / 1048576.0, maxHistoryCount,
            description = "1024-square three-layer deterministic model/CPU-render soak, four samples per brush; erase, transforms, seven blends, ordering, add/delete, Undo/Redo/no-op/cancel, save/reopen/PNG/JPEG every 25 edits, new document every 200. Exact fresh-vs-cached viewport comparison at three scales. Not physical input, WPF presentation, multi-monitor DPI, or an all-day usage test.",
            samples, artifacts = root }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Document NewDocument()
    {
        var doc = Document.Create(1024, 1024);
        return doc with { Layers = doc.Layers.Add(Layer.Blank("Middle", 1024, 1024)).Add(Layer.Blank("Top", 1024, 1024)) };
    }
    private static void CompareViewport(ViewportRenderer viewport, Document document, int iteration)
    {
        float scale = new[] { .5f, .75f, 1f }[iteration % 3];
        float x = iteration % 19, y = -(iteration % 23);
        using var cached = viewport.Render(document, 512, 512, scale, x, y);
        using var surface = SKSurface.Create(CanvasRenderer.Info(512, 512));
        surface.Canvas.Clear(); surface.Canvas.Translate(x, y); surface.Canvas.Scale(scale);
        using var renderer = new CanvasRenderer(); renderer.Draw(surface.Canvas, document);
        using var expected = surface.Snapshot();
        CompareBytes(Pixels(cached), Pixels(expected), 0, "Cached viewport");
    }
    private static byte[] Pixels(SKImage image)
    {
        using var bitmap = new SKBitmap(CanvasRenderer.Info(image.Width, image.Height));
        Require(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0), "ReadPixels failed.");
        return bitmap.GetPixelSpan().ToArray();
    }
    private static void CompareDocuments(Document expected, Document actual)
    {
        Require(actual.Id == expected.Id && actual.Width == expected.Width && actual.Height == expected.Height &&
            actual.Resolution == expected.Resolution && actual.Layers.Length == expected.Layers.Length, "Project metadata changed.");
        for (int i = 0; i < expected.Layers.Length; i++)
        {
            var a = actual.Layers[i]; var e = expected.Layers[i];
            Require((a with { Pixels = e.Pixels }) == e, "Saved layer metadata changed.");
            CompareBytes(e.Pixels.ToRgba(), a.Pixels.ToRgba(), 1, "Saved source pixels");
        }
    }
    private static void CompareBytes(byte[] a, byte[] b, int tolerance, string label)
    {
        Require(a.Length == b.Length, label + " size mismatch.");
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > tolerance)
                throw new InvalidOperationException($"{label} mismatch at byte {i}: {a[i]} != {b[i]} (tolerance {tolerance}).");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
