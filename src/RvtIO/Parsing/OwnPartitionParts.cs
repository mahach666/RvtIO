using System.Buffers.Binary;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace RvtIO.Parsing;

/// <summary>Saved format-9 deposits for walls present only in the initial null-key map.
/// This does not replace the legacy walker or admit geometry from a partial parse.</summary>
internal static class OwnPartitionParts
{
    internal sealed record WallParts(byte[] NodeSha256, byte[] GeometrySha256);

    internal static bool MatchesFrame(byte[] data, int start, uint size, byte[] digest)
        => start >= 0 && size <= int.MaxValue - 16 && (long)start + size + 16 <= data.Length
            && SHA256.HashData(data.AsSpan(start, (int)size + 16)).AsSpan().SequenceEqual(digest);

    internal static Dictionary<uint, WallParts>? Read(byte[] raw, out string? failure)
    {
        failure = null;
        try
        {
            var bytes = PagedGzipStream.RemoveSegments(raw);
            Require(bytes != null, "page-framing");
            var input = new Cursor(bytes!);
            Require(input.U32() == 9 && input.U32() == 0, "format");
            var nodes = new Dictionary<uint, byte[]>();
            var geometry = new Dictionary<uint, byte[]>();
            var ambiguous = new HashSet<uint>();
            var keys = new HashSet<string>();
            int map = 0;
            while (true)
            {
                Require(input.U16() == 817, "content-marker");
                uint pointer = input.U32();
                Require(pointer is 0 or uint.MaxValue, "content-key-pointer");
                if (pointer != 0) Require(input.U16() == 816, "content-key-class");
                uint expectedOwners = input.U32();
                string key = pointer == 0 ? "" : Convert.ToHexString(input.Bytes(16));
                if (expectedOwners == uint.MaxValue)
                {
                    Require(pointer == 0 && map > 0, "terminal-marker");
                    break;
                }
                Require((map != 0 || pointer == 0) && keys.Add(key), "map-key");
                HashSet<uint>[] owners = [new(), new(), new()];
                using var chain = new MemoryStream();
                uint? chainIndex = null;
                long countSum = 0, sizeSum = 0;
                while (true)
                {
                    Require(input.U16() == 3561, "segment-marker");
                    uint flags = input.U32(), count = input.U32(), size = input.U32(), unc = input.U32();
                    if (size == 0)
                    {
                        Require(flags == 0 && count == 0 && unc == 0 && chainIndex == null, "empty-segment");
                        break;
                    }
                    Require(size >= 8 && size <= int.MaxValue, "segment-length");
                    uint index = input.U32();
                    input.U32(); // Native deposit's opaque value is not an owner or a length.
                    Require(index is >= 101 and <= 103, "segment-index");
                    var payload = input.Bytes((int)size - 8);
                    Require(input.U16() == 3554 && input.U32() == size, "segment-checkback");
                    uint mode = flags & 3;
                    if (mode is 0 or 2) Require(chainIndex == null, "chain-start");
                    else Require(chainIndex == index, "chain-continuation");
                    chainIndex = index;
                    Inflate(payload, chain);
                    countSum += count;
                    sizeSum += unc;
                    if (mode is not (0 or 1)) continue;

                    var records = new Cursor(chain.GetBuffer().AsMemory(0, checked((int)chain.Length)));
                    long actualCount = 0, actualSize = 0;
                    while (records.Remaining > 0)
                    {
                        int begin = records.Position;
                        uint owner = records.U32();
                        if (index != 101) records.U32();
                        uint bodySize = records.U32();
                        Require(bodySize <= int.MaxValue, "record-length");
                        var body = records.Bytes((int)bodySize);
                        Require(records.U32() == bodySize, "record-footer");
                        actualCount++;
                        actualSize += bodySize;
                        if (owner == uint.MaxValue) continue;
                        Require(body.Length >= 2, "record-class");
                        owners[index - 101].Add(owner);
                        if (map != 0)
                        {
                            if (nodes.ContainsKey(owner)) ambiguous.Add(owner);
                            continue;
                        }
                        ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(body);
                        var selected = index == 102 ? nodes : index == 103 ? geometry : null;
                        if (selected == null) continue;
                        bool wanted = index == 102 ? kind is 3524 or 361 : kind == 2007;
                        // A repeated deposit replaces the previous one, including a different root class.
                        if (wanted) selected[owner] = SHA256.HashData(records.Slice(begin, records.Position - begin));
                        else selected.Remove(owner);
                    }
                    Require(actualCount == countSum && actualSize == sizeSum, "chain-counts");
                    chain.SetLength(0);
                    chain.Position = 0;
                    chainIndex = null;
                    countSum = sizeSum = 0;
                }
                Require(owners[0].Count == expectedOwners && owners[0].SetEquals(owners[1])
                    && owners[0].SetEquals(owners[2]), "owner-census");
                Require(input.U16() == 3580, "signature-marker");
                input.Skip(input.U32());
                input.Skip(2L * input.U32());
                map++;
            }
            return nodes.Where(pair => !ambiguous.Contains(pair.Key) && geometry.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => new WallParts(pair.Value, geometry[pair.Key]));
        }
        catch (Exception exception) when (exception is InvalidDataException or SharpZipBaseException
            or OverflowException)
        {
            failure = exception.Message;
            return null;
        }
    }

    private static void Inflate(ReadOnlySpan<byte> payload, MemoryStream chain)
    {
        Require(payload.Length >= 18 && payload[..10].SequenceEqual(
            new byte[] { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 0x0b }), "gzip-header");
        uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(payload[^8..]);
        uint expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(payload[^4..]);
        // shortcut: independently compressed members only; dictionary-dependent members reject
        // admission until their shared-window contract is implemented and tested.
        var inflater = new Inflater(noHeader: true);
        inflater.SetInput(payload[10..^8].ToArray());
        var crc = new Crc32();
        byte[] buffer = new byte[32768];
        long length = 0;
        while (!inflater.IsFinished)
        {
            int count = inflater.Inflate(buffer);
            Require(count > 0 || inflater.IsFinished, "gzip-deflate");
            length += count;
            Require(length <= expectedLength && chain.Length + count <= int.MaxValue, "gzip-length");
            crc.Update(new ArraySegment<byte>(buffer, 0, count));
            chain.Write(buffer, 0, count);
        }
        Require(inflater.RemainingInput == 0 && length == expectedLength && crc.Value == expectedCrc,
            "gzip-crc-length-or-extent");
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }

    private sealed class Cursor(ReadOnlyMemory<byte> bytes)
    {
        internal int Position { get; private set; }
        internal int Remaining => bytes.Length - Position;
        internal ReadOnlySpan<byte> Bytes(int count)
        {
            Require(count >= 0 && count <= Remaining, "bounds");
            var value = bytes.Span.Slice(Position, count);
            Position += count;
            return value;
        }
        internal ReadOnlySpan<byte> Slice(int start, int count) => bytes.Span.Slice(start, count);
        internal ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
        internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        internal void Skip(long count)
        {
            Require(count >= 0 && count <= Remaining, "signature-bounds");
            Position += (int)count;
        }
    }
}
