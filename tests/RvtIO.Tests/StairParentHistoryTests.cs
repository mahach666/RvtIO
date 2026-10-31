using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class StairParentHistoryTests
{
    [Theory]
    [InlineData(2829524)] [InlineData(2832395)]
    public void ExactCurrentHistoryReachesOwnHelperWithoutSearching(int owner)
    {
        var ids = Decode(Block(owner));
        Assert.NotNull(ids); Assert.Equal(owner, ids.OwnerId);
        Assert.Equal(new[] { owner + 1, owner + 3, owner + 5 }, ids.RunsAndLandings);
        Assert.Empty(ids.Supports);
    }

    [Theory]
    [InlineData(2829524)] [InlineData(2832395)]
    public void LaterGeometryIsNotClaimedOrUsedByHelperPrefix(int owner)
    {
        byte[] original = Block(owner);
        var extended = original[..^4].Concat(Enumerable.Repeat((byte)0xAB, 19)).Concat(new byte[4]).ToArray(); Frame(extended);
        var before = Decode(original)!; var after = Decode(extended)!;
        Assert.Equal(before.OwnerId, after.OwnerId); Assert.Equal(before.RunsAndLandings, after.RunsAndLandings);
        Assert.Equal(before.Supports, after.Supports); Assert.Equal(before.RegisteredRailings, after.RegisteredRailings);
        Assert.Equal(before.MultistoryUpToLevelId, after.MultistoryUpToLevelId);
    }

    [Theory]
    [InlineData(2829524)] [InlineData(2832395)]
    public void EveryTruncatedPrefixRejectsWithoutPartialMembership(int owner)
    {
        var original = Block(owner);
        for (int length = 14; length < original.Length - 4; length++)
        {
            var shortBlock = original[..length].Concat(new byte[4]).ToArray(); Frame(shortBlock);
            Assert.Null(Decode(shortBlock));
        }
    }

    [Theory]
    [InlineData("header")] [InlineData("footer")] [InlineData("owner")]
    [InlineData("document")] [InlineData("named_id")]
    [InlineData("history_count")] [InlineData("history_duplicate")]
    [InlineData("history_id")] [InlineData("history_marker")]
    public void InvalidCurrentHistoryOrOwnFrameFailsClosed(string mode)
    {
        byte[] bytes = Block(2829524);
        switch (mode)
        {
            case "header": bytes[8] ^= 1; break;
            case "footer": bytes[^1] ^= 1; break;
            case "owner": bytes[0] ^= 1; break;
            case "document": BitConverter.GetBytes(2u).CopyTo(bytes, 54); break;
            case "named_id": BitConverter.GetBytes(42).CopyTo(bytes, 58); break;
            case "history_count": BitConverter.GetBytes(100001u).CopyTo(bytes, 3102); break;
            case "history_duplicate":
                Assert.True(BitConverter.ToUInt32(bytes, 3102) > 1);
                Array.Copy(bytes, 3106, bytes, 3122, 4); break;
            case "history_id": BitConverter.GetBytes(-1).CopyTo(bytes, 3106); break;
            case "history_marker": BitConverter.GetBytes(0u).CopyTo(bytes, 11466 + 12); break;
        }
        Assert.Null(Decode(bytes));
    }

    [Theory]
    [InlineData(9961)] [InlineData(9969)]
    public void CleanPairAffectedTagsAreExactBoundedNestedRecords(int offset)
    {
        var bytes = Block(2832395); BitConverter.GetBytes(100001u).CopyTo(bytes, offset);
        Assert.Null(Decode(bytes));
    }

    private static byte[] Block(int owner) => Convert.FromBase64String(owner == 2829524
        ? StairParentHistoryFixtures.Parent2829524 : StairParentHistoryFixtures.Parent2832395);
    private static TfBlockDeserializer.StairComponentIds? Decode(byte[] block)
        => TfBlockDeserializer.ExtractStairComponentIds(block, 0, BitConverter.ToUInt32(block, 8), 3681);
    private static void Frame(byte[] block)
    {
        uint size = checked((uint)(block.Length - 16));
        BitConverter.GetBytes(size).CopyTo(block, 8); BitConverter.GetBytes(size).CopyTo(block, block.Length - 4);
    }
}
