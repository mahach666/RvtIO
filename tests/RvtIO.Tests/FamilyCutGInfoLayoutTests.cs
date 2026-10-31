using System.Reflection;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class FamilyCutGInfoLayoutTests
{
    [Theory]
    [InlineData(-1,0u)] [InlineData(0,uint.MaxValue)] [InlineData(1,4u)] [InlineData(int.MaxValue,4u)]
    public void ExactOwnFrameRejectsInvalidBounds(int start,uint size)
    {
        Assert.Null(TfBlockDeserializer.ExtractOwnFamilySymbolCutLoops(new byte[16],start,size,1829,out var count));
        Assert.Null(count);
    }

    [Theory]
    [InlineData(0,0u)] [InlineData(42,0u)] [InlineData(-1,uint.MaxValue)]
    [InlineData(42,0x080004u)] [InlineData(0,0x088405u)]
    public void OwnFamilyCutGNodeReadsFourRawWordsWithoutFlagOrNullGuess(int tag,uint flags)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(tag);writer.Write(0);writer.Write(-1);writer.Write(flags);
        Assert.Equal(16,Read(stream.ToArray(),familyCut:true));
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(15)]
    public void TruncatedOwnGInfoCannotBecomeACompactNull(int length)
    {
        var error=Assert.Throws<TargetInvocationException>(()=>Read(new byte[length],familyCut:true));
        Assert.Equal("DecodeFail",error.InnerException!.GetType().Name);
    }

    [Fact]
    public void UnrelatedLegacyNullPathIsUnchanged()=>Assert.Equal(4,Read(new byte[16],familyCut:false));

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void ExactCutDirectionRejectsNonfiniteValuesWithoutChangingLegacy(double value)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(1);writer.Write(0d);writer.Write(value);writer.Write(0d);
        byte[] bytes=stream.ToArray();
        var error=Assert.Throws<TargetInvocationException>(()=>Read(bytes,true,1830,"m_cutDirs"));
        Assert.Equal("DecodeFail",error.InnerException!.GetType().Name);
        Assert.Equal(28,Read(bytes,false,1830,"m_cutDirs"));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void MatrixRowsAreCountedInlineDoubleArrays(int count)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(count);
        for(int row=0;row<count;row++)
        {
            writer.Write(3);
            writer.Write(-1.8640783361334456e-15);writer.Write(1d);writer.Write(0d);
        }
        Assert.Equal(stream.Length,Read(stream.ToArray(),true,1722,"m_rows"));
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(4)] [InlineData(7)] [InlineData(8)] [InlineData(31)]
    public void TruncatedInlineMatrixRowFails(int length)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(1);writer.Write(3);writer.Write(0d);writer.Write(1d);writer.Write(0d);
        var error=Assert.Throws<TargetInvocationException>(()=>
            Read(stream.ToArray()[..length],true,1722,"m_rows"));
        Assert.Equal("DecodeFail",error.InnerException!.GetType().Name);
    }

    [Fact]
    public void NullMatrixArrayKeepsTheExistingCountSentinel()=>
        Assert.Equal(4,Read(BitConverter.GetBytes(uint.MaxValue),true,1722,"m_rows"));

    [Fact]
    public void UnrelatedMatrixPolymorphicPathIsUnchanged()=>
        Assert.Equal(8,Read([1,0,0,0,3,0,0,0],false,1722,"m_rows"));

    [Theory]
    [InlineData("m_faceHistTable",3)] [InlineData("m_curveHistTableSet",5)]
    [InlineData("m_edgeHistTable",4)] [InlineData("m_edgeHistTableReverse",4)]
    public void GeneratorHistoryEntriesAreInlineEvenForZeroIds(string field,int keys)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(2);
        foreach(int id in new[]{0,-1})
        {
            writer.Write(id);
            for(int key=0;key<keys;key++)writer.Write(key-2);
        }
        Assert.Equal(stream.Length,Read(stream.ToArray(),true,97,field));
        var error=Assert.Throws<TargetInvocationException>(()=>
            Read(stream.ToArray()[..^1],true,97,field));
        Assert.Equal("DecodeFail",error.InnerException!.GetType().Name);
    }

    private static int Read(byte[] bytes,bool familyCut,ushort classId=1246,string fieldName="m_GInfo")
    {
        Type type=typeof(TfBlockDeserializer),cursorType=type.GetNestedType("Cursor",BindingFlags.NonPublic)!;
        object cursor=Activator.CreateInstance(cursorType)!;
        cursorType.GetField("D")!.SetValue(cursor,bytes);
        cursorType.GetField("End")!.SetValue(cursor,bytes.Length);
        if(familyCut)
        {
            cursorType.GetField("FamilyCutLoopQueueIndices")!.SetValue(cursor,new List<int>());
            cursorType.GetField("ExactFamilyCutInputs")!.SetValue(cursor,true);
        }
        var field=TfBlockDeserializer.SerializationFields(classId)!.Single(f=>f.Name==fieldName);
        type.GetMethod("ReadField",BindingFlags.NonPublic|BindingFlags.Static)!
            .Invoke(null,[cursor,field,new List<(ushort,uint)>(),0,null]);
        return (int)cursorType.GetField("Pos")!.GetValue(cursor)!;
    }
}
