using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

public sealed record LoadedProject(Document Document, Guid? ActiveLayerId);

public static class ProjectStore
{
    private const int ManifestLimit = 4 * 1024 * 1024;
    private static readonly string[] RootFields = ["format", "version", "colorSpace", "resolution", "documentID", "width", "height", "activeLayerID", "layers", "guides"];
    private static readonly string[] LayerFields = ["id", "name", "isVisible", "transform", "imageFile", "parentID", "isGroup", "opacity", "blendMode",
        "maskFile", "maskEnabled", "maskSourceID", "adjustment", "maskPlacement", "maskLinked", "shape", "effects", "text"];
    private static readonly string[] UnsupportedFields = ["parentID", "maskSourceID", "adjustment", "maskPlacement", "shape", "effects", "text"];

    public static LoadedProject LoadRecovery(string path)
    {
        if (!Path.GetFullPath(path).EndsWith(".comp.recovery", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a .comp.recovery folder.");
        return Load(path);
    }

    public static LoadedProject Load(string path)
    {
        string root = Path.GetFullPath(path);
        CheckNoLinks(root);
        string manifest = Path.Combine(root, "manifest.json");
        CheckFile(manifest, ManifestLimit);
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(manifest), new() { MaxDepth = 32 });
            var m = json.RootElement; CheckFields(m, RootFields);
            if (m.GetProperty("format").GetString() != "com.compositor.project" || m.GetProperty("colorSpace").GetString() != "sRGB")
                throw new InvalidDataException("Unsupported project identifier or color space.");
            int version = m.GetProperty("version").GetInt32();
            if (version is < 1 or > 8) throw new NotSupportedException($"Project version {version} is not supported.");
            if (m.TryGetProperty("guides", out var guides) && guides.ValueKind != JsonValueKind.Null && guides.GetArrayLength() > 0)
                throw new NotSupportedException("This project contains guides, which this Windows build cannot preserve yet.");
            int width = m.GetProperty("width").GetInt32(), height = m.GetProperty("height").GetInt32();
            Limits.CheckDimensions(width, height);
            var entries = m.GetProperty("layers");
            if (entries.GetArrayLength() > 10_000) throw new InvalidDataException("Too many layers.");
            // Reject unsupported features in every layer before decoding any pixels.
            foreach (var l in entries.EnumerateArray())
            {
                CheckFields(l, LayerFields);
                foreach (string field in UnsupportedFields)
                    if (l.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null)
                        throw new NotSupportedException($"Layer '{l.GetProperty("name").GetString()}' contains unsupported {field}. Nothing was opened or changed.");
                string? maskFile = OptionalString(l, "maskFile");
                bool? enabled = OptionalBool(l, "maskEnabled"), linked = OptionalBool(l, "maskLinked");
                if (maskFile is null && (enabled is not null || linked is not null)) throw new InvalidDataException("Mask metadata requires a mask file.");
                if (maskFile is not null && version < 4) throw new InvalidDataException("Masks require project version 4 or later.");
                if (linked == false) throw new NotSupportedException("Unlinked masks are not supported in this Windows build.");
                if (l.TryGetProperty("isGroup", out var group) && group.ValueKind != JsonValueKind.Null && group.GetBoolean())
                    throw new NotSupportedException("Layer groups are not supported in this Windows build.");
            }
            var layers = ImmutableArray.CreateBuilder<Layer>(); long usedPixels = 0, usedMaskPixels = 0;
            foreach (var l in entries.EnumerateArray())
            {
                var id = l.GetProperty("id").GetGuid();
                string name = l.GetProperty("name").GetString() ?? throw new InvalidDataException("Missing layer name.");
                var t = l.GetProperty("transform");
                CheckFields(t, ["origin", "size", "rotation", "flipX", "flipY", "sampling"]);
                var origin = Pair(t.GetProperty("origin")); var size = Pair(t.GetProperty("size"));
                string samplingName = t.GetProperty("sampling").GetString()!;
                Sampling sampling = samplingName switch { "Nearest" => Sampling.Nearest, "Smooth" => Sampling.Smooth, "High quality" => Sampling.High,
                    _ => throw new NotSupportedException($"Unknown sampling mode: {samplingName}.") };
                var transform = new LayerTransform(origin[0], origin[1], size[0], size[1], t.GetProperty("rotation").GetDouble(),
                    t.GetProperty("flipX").GetBoolean(), t.GetProperty("flipY").GetBoolean(), sampling);
                transform.Validate();
                string blendName = OptionalString(l, "blendMode") ?? "Normal";
                if (!Enum.TryParse<BlendMode>(blendName, false, out var blend) || !Enum.IsDefined(blend) || blend.ToString() != blendName)
                    throw new NotSupportedException($"Blend mode '{blendName}' is not supported yet.");
                double opacity = OptionalDouble(l, "opacity", 1);
                if (version < 3 && (opacity != 1 || blend != BlendMode.Normal)) throw new InvalidDataException("Layer appearance conflicts with format version.");
                Raster raster;
                string? asset = OptionalString(l, "imageFile");
                if (asset is not null)
                {
                    if (asset != id.ToString().ToUpperInvariant() + ".png" && asset != id.ToString() + ".png")
                        throw new InvalidDataException("Invalid or unsafe image asset path.");
                    string file = Path.Combine(root, "images", asset); CheckFile(file, ImageCodec.MaxEncodedBytes);
                    raster = ImageCodec.Load(file, Limits.MaxPixels - usedPixels, pngOnly: true);
                    usedPixels += (long)raster.Width * raster.Height;
                }
                else
                {
                    // Empty Mac layers can have arbitrarily enlarged transform bounds; allocate lazily.
                    raster = new(Math.Min(30_000, (int)Math.Ceiling(transform.Width)), Math.Min(30_000, (int)Math.Ceiling(transform.Height)));
                }
                LayerMask? mask = null;
                string? maskAsset = OptionalString(l, "maskFile");
                if (maskAsset is not null)
                {
                    if (maskAsset != id.ToString().ToUpperInvariant() + ".mask.png" && maskAsset != id.ToString() + ".mask.png")
                        throw new InvalidDataException("Invalid or unsafe mask asset path.");
                    string file = Path.Combine(root, "images", maskAsset); CheckFile(file, ImageCodec.MaxEncodedBytes);
                    mask = MaskCodec.Load(file, Limits.MaxPixels - usedMaskPixels) with { Enabled = OptionalBool(l, "maskEnabled") ?? true };
                    usedMaskPixels += (long)mask.Pixels.Width * mask.Pixels.Height;
                }
                layers.Add(new(id, name, raster, transform, l.GetProperty("isVisible").GetBoolean(), opacity, blend, mask));
            }
            var document = new Document(m.GetProperty("documentID").GetGuid(), width, height, OptionalDouble(m, "resolution", 72), layers.ToImmutable());
            document.Validate();
            Guid? active = m.TryGetProperty("activeLayerID", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetGuid() : null;
            if (active is not null && !document.Layers.Any(l => l.Id == active)) throw new InvalidDataException("Invalid active layer.");
            return new(document, active);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException)
        { throw new InvalidDataException("Damaged or invalid project metadata.", e); }
    }

    public static void Save(Document document, Guid? active, string path) => SaveInternal(document, active, path, null);

    internal static void SaveInternal(Document document, Guid? active, string path, Action? beforePublish)
    {
        document.Validate();
        if (active is not null && !document.Layers.Any(l => l.Id == active)) throw new InvalidDataException("Invalid active layer.");
        string destination = Path.GetFullPath(path);
        if (!destination.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Projects must use the .comp extension.");
        CheckNoLinks(destination);
        if (File.Exists(destination)) throw new IOException("A .comp project is a folder, not a regular file.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string backup = destination + ".recovery", staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
        string lockPath = destination + ".write-lock"; CheckNoLinks(lockPath);
        using var writeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Directory.Exists(backup) || File.Exists(backup)) throw new IOException($"A previous save has a recovery copy at {backup}. Preserve it before saving again.");
        if (Directory.Exists(destination)) _ = Load(destination); // Never overwrite an unrelated or unsupported package.
        bool movedOriginal = false;
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "images"));
            var records = new JsonArray();
            foreach (var l in document.Layers)
            {
                string id = l.Id.ToString().ToUpperInvariant(); string? file = null;
                if (l.Pixels.Tiles.Count != 0 || l.Mask is not null) { file = id + ".png"; ImageCodec.SaveRaster(l.Pixels, Path.Combine(staging, "images", file)); }
                string? maskFile = null;
                if (l.Mask is { } mask) { maskFile = id + ".mask.png"; MaskCodec.Save(mask, Path.Combine(staging, "images", maskFile)); }
                var t = l.Transform;
                records.Add(new JsonObject
                {
                    ["id"] = id, ["name"] = l.Name, ["isVisible"] = l.Visible, ["imageFile"] = file,
                    ["maskFile"] = maskFile, ["maskEnabled"] = l.Mask is null ? null : JsonValue.Create(l.Mask.Enabled),
                    ["maskLinked"] = l.Mask is null ? null : JsonValue.Create(true),
                    ["opacity"] = l.Opacity, ["blendMode"] = l.Blend.ToString(),
                    ["transform"] = new JsonObject
                    {
                        ["origin"] = new JsonArray(t.X, t.Y), ["size"] = new JsonArray(t.Width, t.Height),
                        ["rotation"] = t.Rotation, ["flipX"] = t.FlipX, ["flipY"] = t.FlipY,
                        ["sampling"] = t.Sampling == Sampling.High ? "High quality" : t.Sampling.ToString()
                    }
                });
            }
            var manifest = new JsonObject
            {
                ["format"] = "com.compositor.project", ["version"] = 8, ["colorSpace"] = "sRGB",
                ["documentID"] = document.Id.ToString().ToUpperInvariant(), ["width"] = document.Width, ["height"] = document.Height,
                ["resolution"] = document.Resolution, ["activeLayerID"] = active?.ToString().ToUpperInvariant(), ["layers"] = records
            };
            byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
            if (metadata.Length > ManifestLimit) throw new InvalidDataException("Manifest exceeds 4 MiB.");
            using (var file = File.Create(Path.Combine(staging, "manifest.json"))) { file.Write(metadata); file.Flush(true); }
            _ = Load(staging); // Verify the complete package before touching the original.
            if (Directory.Exists(destination)) { Directory.Move(destination, backup); movedOriginal = true; }
            beforePublish?.Invoke();
            Directory.Move(staging, destination);
        }
        catch
        {
            if (movedOriginal && !Directory.Exists(destination)) Directory.Move(backup, destination);
            throw;
        }
        finally { if (Directory.Exists(staging)) DeleteGeneratedDirectory(staging, Path.GetDirectoryName(destination)!); }
        // Once published, cleanup failure is not a failed save. Keep the recovery copy instead.
        if (movedOriginal) { try { DeleteGeneratedDirectory(backup, Path.GetDirectoryName(destination)!); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static void DeleteGeneratedDirectory(string path, string parent)
    {
        string full = Path.GetFullPath(path), allowed = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || full == allowed.TrimEnd(Path.DirectorySeparatorChar)) throw new IOException("Unsafe cleanup path.");
        CheckNoLinks(full);
        foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)) CheckNoLinks(entry);
        Directory.Delete(full, recursive: true);
    }
    internal static void CheckNoLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symbolic links and junctions are not supported for project assets.");
            current = Path.GetDirectoryName(current);
        }
    }
    private static void CheckFile(string path, long max)
    {
        CheckNoLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length > max) throw new InvalidDataException("A project asset is missing or too large.");
    }
    private static void CheckFields(JsonElement element, string[] allowed)
    {
        var names = new HashSet<string>();
        foreach (var p in element.EnumerateObject())
        {
            if (!names.Add(p.Name)) throw new InvalidDataException($"Duplicate metadata field: {p.Name}.");
            if (!allowed.Contains(p.Name)) throw new NotSupportedException($"Unknown project field '{p.Name}'; refusing to discard it.");
        }
    }
    private static double[] Pair(JsonElement e)
    {
        if (e.GetArrayLength() != 2) throw new InvalidDataException("Invalid point or size.");
        return [e[0].GetDouble(), e[1].GetDouble()];
    }
    private static bool? OptionalBool(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetBoolean() : null;
    private static string? OptionalString(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;
    private static double OptionalDouble(JsonElement e, string key, double fallback) => e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetDouble() : fallback;
}
