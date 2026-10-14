using System.Buffers.Binary;
using System.Text;

namespace Compositor.Imaging;

public sealed record OpenTypeFontFaceNames(IReadOnlyDictionary<ushort, string[]> Names)
{
    public IEnumerable<string> Get(ushort id) => Names.TryGetValue(id, out var values) ? values : [];
}

/// <summary>Reads SFNT/TTC name records without loading glyph data or embedding the font.</summary>
public static class OpenTypeFontNames
{
    public static IReadOnlyList<OpenTypeFontFaceNames> Read(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("A seekable font stream is required.");
        byte[] ReadAt(long offset, int count)
        {
            if (offset < 0 || count < 0 || offset > stream.Length - count) throw new InvalidDataException("Truncated font data.");
            stream.Position = offset;var bytes = new byte[count];stream.ReadExactly(bytes);return bytes;
        }
        static ushort U16(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16BigEndian(bytes);
        static uint U32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes);
        var header = ReadAt(0, 12);uint[] offsets;
        if (U32(header) == 0x74746366)
        {
            uint count = U32(header.AsSpan(8));if (count is 0 or > 1024) throw new InvalidDataException("Invalid font collection size.");
            var directory = ReadAt(12, checked((int)count * 4));offsets = new uint[count];for (int i = 0; i < offsets.Length; i++) offsets[i] = U32(directory.AsSpan(i * 4));
        }
        else offsets = [0];
        var result = new List<OpenTypeFontFaceNames>();
        foreach (uint offset in offsets)
        {
            var sfnt = ReadAt(offset, 12);uint signature = U32(sfnt);
            if (signature is not (0x00010000 or 0x4F54544F or 0x74727565)) throw new InvalidDataException("Unsupported SFNT signature.");
            int count = U16(sfnt.AsSpan(4));if (count > 4096) throw new InvalidDataException("Too many font tables.");
            var tables = ReadAt((long)offset + 12, count * 16);byte[]? names = null;
            for (int i = 0; i < count; i++)
            {
                var entry = tables.AsSpan(i * 16, 16);if (U32(entry) != 0x6E616D65) continue;
                uint length = U32(entry[12..]);if (length > 4 * 1024 * 1024) throw new InvalidDataException("Font names exceed limit.");
                names = ReadAt(U32(entry[8..]), checked((int)length));break;
            }
            var values = new Dictionary<ushort, HashSet<string>>();
            if (names is not null)
            {
                if (names.Length < 6 || U16(names) > 1) throw new InvalidDataException("Invalid name table.");
                int records = U16(names.AsSpan(2)), strings = U16(names.AsSpan(4));
                if (6L + records * 12 > names.Length || strings < 6 + records * 12 || strings > names.Length) throw new InvalidDataException("Invalid font name records.");
                for (int i = 0; i < records; i++)
                {
                    var record = names.AsSpan(6 + i * 12, 12);ushort platform = U16(record), encoding = U16(record[2..]), id = U16(record[6..]);
                    if (id is not (1 or 2 or 4 or 6 or 16 or 17)) continue;
                    int length = U16(record[8..]), start = strings + U16(record[10..]);
                    if (start > names.Length - length) throw new InvalidDataException("Invalid font string offset.");
                    var bytes = names.AsSpan(start, length);string value;
                    if (platform is 0 or 3)
                    { if (length % 2 != 0) throw new InvalidDataException("Invalid Unicode font name.");value = Encoding.BigEndianUnicode.GetString(bytes); }
                    else if (platform == 1 && encoding == 0 && bytes.IndexOfAnyInRange((byte)128, (byte)255) < 0) value = Encoding.ASCII.GetString(bytes);
                    else continue;
                    if (string.IsNullOrEmpty(value)) continue;
                    if (!values.TryGetValue(id, out var set)) values[id] = set = new(StringComparer.OrdinalIgnoreCase);set.Add(value);
                }
            }
            result.Add(new(values.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())));
        }
        return result;
    }
}
