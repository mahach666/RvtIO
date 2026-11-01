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

    private static string? Sample(string name)
        => TestFiles.TryFind(name) is { } p && new FileInfo(p).Length > 1_000_000 ? p : null;

    /// <summary>Public Autodesk and own sample files: the schema release equals the saved release and the element table rows are
    /// the size the file's schema declares (28 bytes up to 2023, 40 from 2024).</summary>
    [Theory]
    [InlineData("Test_2020.rvt", 2020, 28)]
    [InlineData("Test_2022.rvt", 2022, 28)]
    [InlineData("Test_2025.rvt", 2025, 40)]
    public void SampleFilesAgreeAndDecodeWithTheirOwnRowSize(string name, int release, int rowLength)
    {
        if (Sample(name) is not { } path) return;
        using var root = RootStorage.OpenRead(path);
        var schema = FormatsSchema.TryRead(root)!;
        Assert.Equal(release, SchemaRelease.FromSchema(schema));
        Assert.Equal(release, SchemaRelease.Saved(BasicFileInfoReader.Read(root).GetValueOrDefault("RevitVersion")));
        Assert.Equal(rowLength, ElemTableLayout.RowLength(schema));
        Assert.NotNull(ElemTableLayout.TryParse(PagedGzipStream.Read(root, "Global", "ElemTable")!, schema));
    }
}
