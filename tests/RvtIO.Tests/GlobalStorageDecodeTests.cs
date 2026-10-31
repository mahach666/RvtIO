using System.Reflection;
using OpenMcdf;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class GlobalStorageDecodeTests
{
    [SkippableTheory]
    [InlineData("Formats", "Latest")]
    [InlineData("Global", "Latest")]
    [InlineData("Global", "ContentDocuments")]
    public void ProductionStoragePathUsesTheCrcCheckedPagedBytes(string storage, string stream)
    {
        using var root = RootStorage.OpenRead(TestFileFixture.FindTestFile("TESTFILE.rvt"));
        var expected = PagedGzipStream.Read(root, storage, stream); Assert.NotNull(expected);
        using var log = new StringWriter();
        var chunks = Decompress(root, storage, log);
        Assert.Contains(chunks, bytes => bytes.AsSpan().SequenceEqual(expected));
        Assert.Contains($"{storage}/{stream}: exact paged decode", log.ToString());
    }

    [SkippableFact]
    public void MissingStorageKeepsTheExistingEmptyResult()
    {
        using var root = RootStorage.OpenRead(TestFileFixture.FindTestFile("TESTFILE.rvt"));
        Assert.Empty(Decompress(root, "NoSuchStorage", null));
    }

    private static List<byte[]> Decompress(RootStorage root, string name, TextWriter? log)
        => (List<byte[]>)typeof(RvtFileReader).GetMethod("DecompressStorage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new RvtFileReader(new RvtReaderOptions()), [root, name, log])!;
}
