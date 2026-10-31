using System.Text;
using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnDocumentUnitsLinkTests
{
    // Independent fixture shape: original typed DD constructors/callbacks, not the
    // decoder's private alias array. All these containers have nonempty payloads.
    private static readonly Dictionary<(int, string), int> Refs = new()
    {
        [(2546,"m_appInfoData")]=2547, [(2547,"second")]=2541,
        [(2546,"m_data")]=2548, [(2548,"second")]=2549,
        [(2544,"m_requestToken")]=2545, [(696,"m_secondaryId")]=30,
        [(2546,"m_primary2SecondaryDataIdMap")]=2552, [(2552,"second")]=2553,
        [(2553,"m_dataIds")]=2544, [(1804,"m_arrLoadedFamilyInfo")]=1805,
        [(1805,"m_familyDocGUIDs")]=30, [(3609,"m_WPRMap")]=3610,
        [(3610,"second")]=3611, [(3611,"m_sketchGridId")]=20,
        [(1357,"m_symbols")]=1358, [(2561,"m_items")]=2562,
        [(2983,"m_topologyMap")]=2984, [(2984,"first")]=2985,
        [(2984,"second")]=20, [(2985,"m_zoneSchemeId")]=20,
        [(640,"m_gstyleData")]=641, [(641,"m_gstyleId")]=20,
        [(1359,"m_symbols")]=1360, [(1360,"m_elemIdSet")]=20,
        [(3399,"m_refTableData")]=3401, [(3401,"second")]=3397,
        [(3399,"m_refTableKeyData")]=3402, [(3402,"first")]=3403,
        [(3402,"second")]=3404, [(3403,"m_categoryId")]=20,
        [(3404,"m_refTableIds")]=20, [(24,"second")]=20,
        [(322,"second")]=20, [(185,"second")]=186,
        [(186,"m_elementIds")]=20, [(1479,"m_keyDataMap")]=1480,
        [(1480,"second")]=1477,
    };
    private static readonly ushort[] AppInfos = [1804,3609,1357,2561,2983,640,1359,3399,3534,2304,1479,3904];

    [Fact]
    public void NonemptyTypedGraphHasEveryExactFieldAndObjectBoundary()
    {
        var f = Fixture(); var link = TfBlockDeserializer.ExtractOwnDocumentUnitsLink(f.Data, out var failure);
        Assert.True(link != null, failure); Assert.NotNull(link);
        Assert.Equal(712u, link.UnitsElemId); Assert.Equal(f.TrackingStart, link.TrackingStart);
        Assert.Equal(f.Data.Length, link.End); Assert.Equal(f.Fields, link.Fields);
        Assert.Equal(f.Objects, link.Objects);
        foreach (var key in Refs.Keys)
            Assert.Contains(link.Fields, x => (x.Declaring, x.Name) == key && x.End > x.Start);
        // Nested owned Noble AppInfo is queued after all manager entries, not before UnitsTracking.
        Assert.Equal(2541, link.DeferredObjects[^1].ClassId);
        Assert.Equal(14, link.Objects.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueueOrderAndOwnerAreReadFromSourceNotModelConstants(bool reverse)
    {
        var f = Fixture(reverse: reverse, owner: 919);
        var link = Read(f.Data); Assert.NotNull(link); Assert.Equal(919u, link.UnitsElemId);
        Assert.Equal(f.Objects, link.Objects); Assert.Equal(f.Fields, link.Fields);
        Assert.Equal(AppInfos.Length + 4, link.Manager.UnitsTrackingQueueIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NonpositiveTrackingOwnerRejects(int id) => Assert.Null(Read(Fixture(owner:id).Data));

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(uint.MaxValue)]
    public void TrackingDocumentBackreferenceMustBeRoot(uint reference)
    {
        var f=Fixture(); BitConverter.GetBytes(reference).CopyTo(f.Data,f.TrackingStart);
        Assert.Null(Read(f.Data));
    }

    [Theory]
    [InlineData(2546,"m_pDoc")]
    [InlineData(27,"m_pADoc")]
    [InlineData(1357,"m_pADoc")]
    [InlineData(1359,"m_pADoc")]
    public void DeferredDocumentBackreferencesMustBeRoot(int cls,string name)
    {
        var f=Fixture(); var span=f.Fields.First(x=>x.Declaring==cls && x.Name==name);
        BitConverter.GetBytes(2u).CopyTo(f.Data,span.Start); Assert.Null(Read(f.Data));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(27)]
    [InlineData(65535)]
    public void NestedOwnedPointerMustHaveItsTypedClass(int cls)
    {
        var f=Fixture(); var span=f.Fields.Single(x=>x.Declaring==2547 && x.Name=="second");
        BitConverter.GetBytes((ushort)cls).CopyTo(f.Data,span.Start+4); Assert.Null(Read(f.Data));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void EmptyCollectionSentinelsAreEquivalent(uint empty)
    {
        var f=Fixture(empty:empty); Assert.NotNull(Read(f.Data));
    }

    [Theory]
    [InlineData(100001u)]
    [InlineData(uint.MaxValue-1)]
    public void EveryCollectionCountIsBounded(uint value)
    {
        var f=Fixture();
        foreach (var span in f.Fields.Where(x=>TfBlockDeserializer.Schemas[x.Declaring].Fields
            .Any(y=>y.Name==x.Name && y.Mods>>4==5)))
        {
            byte[] data=[..f.Data]; BitConverter.GetBytes(value).CopyTo(data,span.Start); Assert.Null(Read(data));
        }
    }

    [Fact]
    public void UnsupportedNonemptyEnqueuedNobleContainerRejectsWithoutWidthGuess()
    {
        var f=Fixture(); var span=f.Fields.Single(x=>x.Declaring==2546 && x.Name=="m_enqueuedData2primaryParents");
        BitConverter.GetBytes(1u).CopyTo(f.Data,span.Start); Assert.Null(Read(f.Data));
    }

    [Fact]
    public void EveryTruncationRejectsAndTailDoesNotAuthorizeResync()
    {
        var f=Fixture();
        for(int end=0;end<f.Data.Length;end++) Assert.Null(Read(f.Data[..end]));
        var result=Read([..f.Data,9,8,7]); Assert.NotNull(result); Assert.Equal(f.Data.Length,result.End);
        Assert.Null(Read([9,8,..f.Data]));
    }

    [Fact]
    public void DeferredStringsAreStrictAndEveryBoolCanonical()
    {
        var f=Fixture();
        foreach(var span in f.Fields.Where(x=>TfBlockDeserializer.Schemas[x.Declaring].Fields
            .Any(y=>y.Name==x.Name && (y.Type==8 || y.Mods>>4==6))))
        {
            byte[] data=[..f.Data]; data[span.Start+4]=0; data[span.Start+5]=0xD8; Assert.Null(Read(data));
            BitConverter.GetBytes(10001u).CopyTo(data,span.Start); Assert.Null(Read(data));
        }
        foreach(var span in f.Fields.Where(x=>TfBlockDeserializer.Schemas[x.Declaring].Fields.Any(y=>y.Name==x.Name && y.Type==1)))
        { byte[] data=[..f.Data]; data[span.Start]=2; Assert.Null(Read(data)); }
    }

    [Fact]
    public void SelectedOptionsRequireExactGlobalOwnerAndNeverFirstRoot()
    {
        var state=new ParsingState(); state.OwnUnitOptions[33]=(1,Options(33));
        state.OwnUnitOptions[712]=(1,Options(712)); Assert.Null(state.OwnDocumentUnitOptions);
        state.OwnDocumentUnitsElemId=Read(Fixture().Data)!.UnitsElemId;
        Assert.Same(state.OwnUnitOptions[712].Options,state.OwnDocumentUnitOptions);
        state.OwnDocumentUnitsElemId=919; Assert.Null(state.OwnDocumentUnitOptions);
    }

    [Theory]
    [InlineData(0,712,-1,1)]
    [InlineData(2,712,-1,1)]
    [InlineData(1,713,-1,1)]
    [InlineData(1,712,33,1)]
    [InlineData(1,712,-1,0)]
    [InlineData(1,712,-1,2)]
    public void DuplicateForeignFamilyOrDocumentCannotSupplyCurrentOptions(int count,uint owner,int family,int document)
    {
        var state=new ParsingState { OwnDocumentUnitsElemId=712 };
        state.OwnUnitOptions[712]=(count,Options(owner,family,document)); Assert.Null(state.OwnDocumentUnitOptions);
    }

    [Fact]
    public void RejectedOrLaterDuplicateRootRevokesCurrentOptions()
    {
        var state=new ParsingState { OwnDocumentUnitsElemId=712 };
        state.OwnUnitOptions[712]=(1,Options(712)); Assert.NotNull(state.OwnDocumentUnitOptions);
        state.OwnUnitOptions[712]=(2,null); Assert.Null(state.OwnDocumentUnitOptions);
        state.OwnUnitOptions[712]=(1,null); Assert.Null(state.OwnDocumentUnitOptions);
    }

    private static TfBlockDeserializer.UnitsElemOptions Options(uint owner,int family=-1,int document=1)
        => new(owner,family,document,85,105,new([],2,3,4,5));
    private static TfBlockDeserializer.DocumentUnitsLink? Read(byte[] data)
        => TfBlockDeserializer.ExtractOwnDocumentUnitsLink(data);

    private sealed record GraphFixture(byte[] Data,int TrackingStart,
        TfBlockDeserializer.DocumentGraphField[] Fields,TfBlockDeserializer.DocumentGraphObject[] Objects);
    private static GraphFixture Fixture(bool reverse=false,int owner=712,uint empty=0)
    {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        var fields=new List<TfBlockDeserializer.DocumentGraphField>(); var objects=new List<TfBlockDeserializer.DocumentGraphObject>();
        int Pos()=>(int)stream.Position;
        void Pointer(uint handle,ushort cls) { w.Write(handle); if(handle!=0)w.Write(cls); }
        w.Write((ushort)28); Pointer(0,0); w.Write(0u); Pointer(uint.MaxValue,821);
        w.Write(1u); Pointer(uint.MaxValue,338); Pointer(uint.MaxValue,3867); Pointer(0,0);
        Pointer(0,0); Pointer(0,0); Pointer(uint.MaxValue,2546);
        w.Write(-1);w.Write(-1);w.Write(17);w.Write(2662);w.Write(new byte[3]);w.Write(0u);w.Write(0u);Pointer(0,0);
        w.Write(1u);w.Write(0u); // ContentTable, empty
        w.Write(1u);w.Write((uint)AppInfos.Length+1);
        ushort[] children=reverse?AppInfos.AsEnumerable().Reverse().ToArray():AppInfos;
        foreach(var cls in children)Pointer(uint.MaxValue,cls);Pointer(uint.MaxValue,4030);
        void Record(int cls,int index,int depth=0)
        {
            Assert.True(depth<30); var chain=new List<int>();
            for(int? current=cls;current.HasValue;current=TfBlockDeserializer.Schemas[current.Value].Base)chain.Add(current.Value);
            foreach(int decl in chain.AsEnumerable().Reverse())
            foreach(var f in TfBlockDeserializer.Schemas[decl].Fields)
            {
                int start=Pos(); int? reference=Refs.TryGetValue((decl,f.Name),out int native)?native:f.Ref;
                void String() { const string value="я\U0001F600";w.Write((uint)value.Length);w.Write(Encoding.Unicode.GetBytes(value)); }
                void Scalar()
                {
                    switch(f.Type)
                    {
                        case 1:w.Write((byte)1);break;
                        case 2:case 3:w.Write((byte)17);break;
                        case 4:case 5:case 6:w.Write(-19);break;
                        case 7:case 10:w.Write(3.25);break;
                        case 8:String();break;
                        case 9:w.Write(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToByteArray());break;
                        case 13:w.Write(new byte[f.Cnt!.Value*8]);break;
                        case 14:
                            int lo=f.Mods&15;
                            if(lo==3)w.Write(1u);
                            else if(lo is 1 or 2)
                            { if(decl==2547)Pointer(uint.MaxValue,2541);else Pointer(0,0); }
                            else if(reference==20)w.Write(-31);
                            else if(reference.HasValue)Record(reference.Value,index,depth+1);
                            else w.Write(0u);
                            break;
                        default:throw new InvalidOperationException($"Unexpected fixture type {decl}.{f.Name}");
                    }
                }
                int hi=f.Mods>>4;
                if(hi==6)String();
                else if(hi==5)
                {
                    bool nonempty=decl!=3867 && !(decl==2546 && f.Name=="m_enqueuedData2primaryParents")
                        && (reference.HasValue || Refs.ContainsKey((decl,f.Name)));
                    w.Write(nonempty?1u:empty);if(nonempty)Scalar();
                }
                else if(hi==1) { for(int i=0;i<f.Cnt!.Value;i++)Scalar(); }
                else Scalar();
                fields.Add(new(decl,f.Name,start,Pos(),index));
            }
        }
        void Queued(ushort cls,int index) { int start=Pos();Record(cls,index);objects.Add(new(cls,index,start,Pos())); }
        Queued(3867,2);Queued(2546,3);
        for(int i=0;i<children.Length;i++)Queued(children[i],i+4);
        int tracking=Pos();w.Write(1u);w.Write(owner);
        return new(stream.ToArray(),tracking,fields.ToArray(),objects.ToArray());
    }
}
