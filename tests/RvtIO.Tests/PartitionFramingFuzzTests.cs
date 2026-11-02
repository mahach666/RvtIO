using OpenMcdf;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Truncated and mutated partition streams are rejected with a reason: no exception escapes, no run takes long.</summary>
public class PartitionFramingFuzzTests
{
    [SkippableFact]
    public void MutatedAndTruncatedStreamsAreRejectedWithoutAnException()
    {
        string path = TestFiles.Find("TESTFILE.rvt");
        using var root = RootStorage.OpenRead(path);
        var schema = FormatsSchema.TryRead(root)!;
        var storage = root.OpenStorage("Partitions");
        var random = new Random(12345);
        foreach (string name in new[] { "1", "3" })
        {
            using var stream = storage.OpenStream(name);
            var raw = new byte[stream.Length];
            stream.ReadExactly(raw);
            for (int i = 0; i < 400; i++)
            {
                var data = (byte[])raw.Clone();
                switch (i % 4)
                {
                    case 0: data = data[..random.Next(0, data.Length)]; break;
                    case 1: for (int k = 0; k <= random.Next(4); k++) data[random.Next(data.Length)] ^= (byte)(1 + random.Next(255)); break;
                    case 2: BitConverter.GetBytes(random.Next()).CopyTo(data, random.Next(0, Math.Min(data.Length - 4, 2000))); break;
                    default: BitConverter.GetBytes(uint.MaxValue).CopyTo(data, random.Next(0, data.Length - 4)); break;
                }
                var census = RecordRouting.Begin(schema, out _)!;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bool accepted = RecordRouting.Collect(census, 1, data, out string? failure);
                Assert.True(watch.ElapsedMilliseconds < 5000);
                Assert.True(accepted || failure != null);
            }
        }
    }
}
