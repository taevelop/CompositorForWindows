using System.Buffers.Binary;
using System.Text;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class OpenTypeFontNamesTests
{
    private static void U16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
    private static byte[] Names(string family, string face, string postScript)
    {
        string[] names = [family, face, postScript];ushort[] ids = [1, 2, 6];var encoded = names.Select(Encoding.BigEndianUnicode.GetBytes).ToArray();
        var bytes = new byte[42 + encoded.Sum(b => b.Length)];U16(bytes, 2, 3);U16(bytes, 4, 42);int offset = 0;
        for (int i = 0; i < 3; i++)
        { int p = 6 + i * 12;U16(bytes, p, 3);U16(bytes, p + 2, 1);U16(bytes, p + 4, 0x409);U16(bytes, p + 6, ids[i]);U16(bytes, p + 8, (ushort)encoded[i].Length);U16(bytes, p + 10, (ushort)offset);encoded[i].CopyTo(bytes, 42 + offset);offset += encoded[i].Length; }
        return bytes;
    }
    private static void Face(byte[] bytes, int offset, int namesOffset, byte[] names)
    { U32(bytes, offset, 0x00010000);U16(bytes, offset + 4, 1);U32(bytes, offset + 12, 0x6e616d65);U32(bytes, offset + 20, (uint)namesOffset);U32(bytes, offset + 24, (uint)names.Length);names.CopyTo(bytes, namesOffset); }
    [Fact]
    public void ReadsUnicodePostScriptNameAndSeparateCollectionFaces()
    {
        var names = Names("글꼴 Family", "Regular", "Family-Regular");var sfnt = new byte[28 + names.Length];Face(sfnt, 0, 28, names);
        using var stream = new MemoryStream(sfnt);var parsed = Assert.Single(OpenTypeFontNames.Read(stream));Assert.Equal("글꼴 Family", Assert.Single(parsed.Get(1)));Assert.Equal("Family-Regular", Assert.Single(parsed.Get(6)));
        var bold = Names("글꼴 Family", "Bold", "Family-Bold");var collection = new byte[76 + names.Length + bold.Length];
        U32(collection, 0, 0x74746366);U32(collection, 4, 0x00010000);U32(collection, 8, 2);U32(collection, 12, 20);U32(collection, 16, 48);
        Face(collection, 20, 76, names);Face(collection, 48, 76 + names.Length, bold);
        using var ttc = new MemoryStream(collection);var faces = OpenTypeFontNames.Read(ttc);Assert.Equal(2, faces.Count);Assert.Equal("Family-Regular", Assert.Single(faces[0].Get(6)));Assert.Equal("Family-Bold", Assert.Single(faces[1].Get(6)));
    }
    [Fact]
    public void RejectsTruncatedOffsetsInvalidCollectionAndOddUnicodeLength()
    {
        var names = Names("Family", "Regular", "Family-Regular");var sfnt = new byte[28 + names.Length];Face(sfnt, 0, 28, names);
        using var truncated = new MemoryStream(sfnt[..20]);Assert.Throws<InvalidDataException>(() => OpenTypeFontNames.Read(truncated));
        var corrupt = (byte[])sfnt.Clone();U32(corrupt, 20, uint.MaxValue);using var outside = new MemoryStream(corrupt);Assert.Throws<InvalidDataException>(() => OpenTypeFontNames.Read(outside));
        U16(sfnt, 28 + 6 + 8, 1);using var odd = new MemoryStream(sfnt);Assert.Throws<InvalidDataException>(() => OpenTypeFontNames.Read(odd));
        var header = new byte[12];U32(header, 0, 0x74746366);U32(header, 8, 1025);using var many = new MemoryStream(header);Assert.Throws<InvalidDataException>(() => OpenTypeFontNames.Read(many));
    }
}
