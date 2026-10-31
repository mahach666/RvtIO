using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class SourceElementGRepLookupTests
{
    static readonly SourceElementCell[] Cells = [new(10,500),new(20,600),new(int.MaxValue,700)];

    [Theory]
    [InlineData(int.MinValue)] [InlineData(-1)]
    public void NegativeIdsStopBeforeAlternateAndCache(int id)
    {
        var p=Read(id,true,[new(2,-1)],new(-1,true,true));Assert.Equal("negative-id-null",p.Outcome);
        Assert.False(p.RequiresAlternateBackend);Assert.False(p.DeleteHashEntry);
    }

    [Fact]
    public void AlternateBackendIsExplicitAndNotNativeNull()
    {
        var p=Read(20,true,null,null);Assert.True(p.RequiresAlternateBackend);
        Assert.Equal("alternate-virtual3-required",p.Outcome);Assert.False(p.CurrentCellAuthorityClosed);
    }

    [Fact]
    public void CachedGrepWinsBeforeChangedMissingOrMalformedCells()
    {
        var p=Read(20,false,[new(20,-1)],new(999,true,true));Assert.Equal("cached-grep",p.Outcome);
        Assert.Equal(999,p.RecordIndex);Assert.False(p.CallElemRecGetGRep);Assert.False(p.DeleteHashEntry);
    }

    [Fact]
    public void ExpandedNullCacheStopsBeforeCellLookup()
    {
        var p=Read(20,false,Cells,new(999,false,true));Assert.Equal("cached-expanded-null",p.Outcome);
        Assert.Null(p.RecordIndex);Assert.False(p.AddHashEntry);Assert.False(p.CallElemRecGetGRep);
    }

    [Theory]
    [InlineData(0)] [InlineData(9)] [InlineData(11)] [InlineData(19)] [InlineData(21)]
    public void LowerBoundNeighbourDoesNotBecomeCurrentRecord(int id)
    {
        var p=Read(id,false,Cells,new(999,false,false));Assert.Null(p.RecordIndex);
        Assert.Equal("no-exact-cell-null",p.Outcome);Assert.True(p.DeleteHashEntry);Assert.False(p.AddHashEntry);
    }

    [Fact]
    public void MissingTreeAndEmptyTreeBothRetainExplicitNullOutcome()
    {
        Assert.Equal("no-exact-cell-null",Read(20,false,null,null).Outcome);
        Assert.Equal("no-exact-cell-null",Read(20,false,[],null).Outcome);
    }

    [Fact]
    public void ExactCellIdentityIsNotElementIdAndRequiresExisting201Expansion()
    {
        var p=Read(20,false,Cells,null);Assert.Equal(600,p.RecordIndex);
        Assert.True(p.AddHashEntry);Assert.False(p.DeleteHashEntry);Assert.True(p.CallElemRecGetGRep);
        Assert.False(p.CurrentCellAuthorityClosed);
    }

    [Fact]
    public void DifferentColdCacheIsDeletedAndReplaced()
    {
        var p=Read(20,false,Cells,new(999,false,false));Assert.Equal(600,p.RecordIndex);
        Assert.True(p.DeleteHashEntry);Assert.True(p.AddHashEntry);Assert.True(p.CallElemRecGetGRep);
    }

    [Fact]
    public void SameColdCacheIsNotRemovedOrAddedAgain()
    {
        var p=Read(20,false,Cells,new(600,false,false));Assert.False(p.DeleteHashEntry);
        Assert.False(p.AddHashEntry);Assert.True(p.CallElemRecGetGRep);
    }

    [Fact]
    public void ZeroAndSignedExtremeKeysAreComparedLiterally()
    {
        Assert.Equal(22,Read(0,false,[new(int.MinValue,1),new(0,22),new(int.MaxValue,33)],null).RecordIndex);
        Assert.Equal(700,Read(int.MaxValue,false,Cells,null).RecordIndex);
    }

    [Fact]
    public void UnsortedAndDuplicateCellsAreNotRepairedWithInventedWinner()
    {
        Assert.Null(PlanSourceElementGRepLookup(20,false,[new(20,1),new(10,2)],null,out var why));Assert.NotNull(why);
        Assert.Null(PlanSourceElementGRepLookup(20,false,[new(20,1),new(20,2)],null,out why));Assert.NotNull(why);
    }

    [Fact]
    public void InvalidIdentityIsUnsupportedNotSuccessfulEmptyGeometry()
    {
        Assert.Null(PlanSourceElementGRepLookup(20,false,Cells,new(-1,false,false),out var why));Assert.NotNull(why);
        Assert.Null(PlanSourceElementGRepLookup(20,false,[new(20,-1)],null,out why));Assert.NotNull(why);
        Assert.Null(PlanSourceElementGRepLookup(20,false,[new(10,5),new(20,5)],null,out why));Assert.NotNull(why);
    }

    static SourceElementGRepLookupPlan Read(int id,bool alternate,IReadOnlyList<SourceElementCell>? cells,SourceElementGRepCache? cache)
    {
        var result=PlanSourceElementGRepLookup(id,alternate,cells,cache,out var why);Assert.Null(why);Assert.NotNull(result);return result;
    }
}
