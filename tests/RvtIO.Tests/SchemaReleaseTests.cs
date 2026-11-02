using OpenMcdf;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The release of the file's own schema, against the release saved in BasicFileInfo.</summary>
public class SchemaReleaseTests
{
    [Theory]
    [InlineData(817, 4307, 2022)]     // both agree
    [InlineData(null, 4307, 2022)]    // class count only
    [InlineData(817, 123, 2022)]      // marker only
    [InlineData(891, 4307, null)]     // the two name different releases: unknown
    [InlineData(null, 123, null)]     // neither is known
    [InlineData(null, 4757, 2027)]
    public void EvidenceMustAgreeOrTheReleaseIsUnknown(int? marker, int classCount, int? expected)
        => Assert.Equal(expected, SchemaRelease.FromEvidence(marker, classCount));

    [Theory]
    [InlineData(2025, 2022, 2022)]    // saved by a newer release, schema of an older one
    [InlineData(2022, 2025, 2022)]
    [InlineData(2024, 2024, 2024)]
    [InlineData(2024, null, 2024)]
    [InlineData(null, 2022, 2022)]
    [InlineData(null, null, null)]
    public void TheOlderReleaseDecidesTheLayout(int? saved, int? schema, int? expected)
        => Assert.Equal(expected, SchemaRelease.Effective(saved, schema));

    [Theory]
    [InlineData("2025", 2025)]
    [InlineData("25", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheSavedReleaseIsAFourDigitYear(string? text, int? expected)
        => Assert.Equal(expected, SchemaRelease.Saved(text));

    [Fact]
    public void TheDiagnosticsLineNamesBothReleases()
    {
        Assert.Equal("Release: saved by 2025, schema of 2022, the layout of release 2022 is used", SchemaRelease.Describe(2025, 2022));
        Assert.Equal("Release: saved by 2024, schema of 2024", SchemaRelease.Describe(2024, 2024));
        Assert.Equal("Release: saved by unknown, schema of unknown", SchemaRelease.Describe(null, null));
    }

    /// <summary>Public Autodesk and own sample files: the schema release equals the saved release and the element table rows are
    /// the size the file's schema declares (28 bytes up to 2023, 40 from 2024).</summary>
    [SkippableTheory]
    [InlineData("Test_2020.rvt", 2020, 28)]
    [InlineData("Test_2022.rvt", 2022, 28)]
    [InlineData("Test_2025.rvt", 2025, 40)]
    public void SampleFilesAgreeAndDecodeWithTheirOwnRowSize(string name, int release, int rowLength)
    {
        string path = TestFiles.Find(name);
        using var root = RootStorage.OpenRead(path);
        var schema = FormatsSchema.TryRead(root)!;
        Assert.Equal(release, SchemaRelease.FromSchema(schema));
        Assert.Equal(release, SchemaRelease.Saved(BasicFileInfoReader.Read(root).GetValueOrDefault("RevitVersion")));
        Assert.Equal(rowLength, ElemTableLayout.RowLength(schema));
        Assert.NotNull(ElemTableLayout.TryParse(PagedGzipStream.Read(root, "Global", "ElemTable")!, schema));
    }

    /// <summary>A copy of the sample whose BasicFileInfo names another saving release (the same four digits in every UTF-16 place), so the
    /// saved release and the schema release of the file differ.</summary>
    private static string CopyWithSavedRelease(string sample, int savedRelease)
    {
        string copy = Path.Combine(Path.GetTempPath(), $"schema-release-{Guid.NewGuid():N}.rvt");
        File.Copy(sample, copy);
        using (var root = RootStorage.Open(copy, FileMode.Open))
        {
            var info = BasicFileInfoReader.Read(root);
            string from = info["RevitVersion"], to = savedRelease.ToString();
            using var stream = root.OpenStream("BasicFileInfo");
            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            var oldBytes = System.Text.Encoding.Unicode.GetBytes(from);
            var newBytes = System.Text.Encoding.Unicode.GetBytes(to);
            for (int at = data.AsSpan().IndexOf(oldBytes); at >= 0; )
            {
                newBytes.CopyTo(data, at);
                int next = data.AsSpan(at + oldBytes.Length).IndexOf(oldBytes);
                at = next < 0 ? -1 : at + oldBytes.Length + next;
            }
            stream.Position = 0;
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        return copy;
    }

    /// <summary>Every layout choice reads the release in force, the older of the saved and the schema release: a Revit 2025 file that
    /// says it was saved by 2022 is read by the 2022 layout (4-byte ids), which its 8-byte rows do not have, so its element table is
    /// not decoded; a Revit 2022 file that says it was saved by 2025 keeps the 2022 layout it has.</summary>
    [SkippableTheory]
    [InlineData("Test_2025.rvt", 2025, 2025, true)]    // saved == schema: decoded as before
    [InlineData("Test_2025.rvt", 2022, 2022, false)]   // saved by an older release than the schema: the 2022 layout is in force
    [InlineData("TESTFILE.rvt", 2025, 2022, true)]     // saved by a newer release: the schema's older layout is in force
    [InlineData("TESTFILE.rvt", 2020, 2020, true)]     // same 4-byte family
    public void TheReleaseInForceDecidesTheElementTableLayout(string name, int saved, int inForce, bool decodes)
    {
        string copy = CopyWithSavedRelease(TestFiles.Find(name), saved);
        try
        {
            using var root = RootStorage.OpenRead(copy);
            Assert.Equal(saved, SchemaRelease.Saved(BasicFileInfoReader.Read(root).GetValueOrDefault("RevitVersion")));
            var schema = FormatsSchema.TryRead(root)!;
            Assert.Equal(inForce, schema.Release);
            Assert.Equal(inForce, FileSchema.Read(root)!.Release);
            Assert.Equal(decodes, ElemTableLayout.RowLength(schema) is not null);
            Assert.Equal(decodes, ElemTableLayout.IdWidth(schema) is not null);
            Assert.Equal(decodes, ElemTableLayout.TryParse(PagedGzipStream.Read(root, "Global", "ElemTable")!, schema) is not null);
            Assert.Equal(decodes, WideElementRecords.IdWidth(FileSchema.Read(root)!) is not null);
            Assert.Equal(decodes, RecordRouting.Begin(schema, out _) is not null);
            var elements = ElemTableReader.Read(root, out _, out _, out bool exact);
            Assert.Equal(decodes, exact);
            if (decodes) Assert.NotEmpty(elements);
        }
        finally { File.Delete(copy); }
    }
}
