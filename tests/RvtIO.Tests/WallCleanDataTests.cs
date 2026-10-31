using System.Reflection;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallCleanDataTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ControlJoinCleanDataIsInlineAndDoesNotCreateDeferredObjects(int count)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(2u);
        for (int end=0;end<2;end++)
        {
            writer.Write(count);
            for (int i=0;i<count;i++) {writer.Write(i==0 ? -1 : 508179);writer.Write(i);}
            writer.Write(1u);writer.Write(581277);writer.Write(2);writer.Write((byte)1);
            writer.Write(3);writer.Write(end);
        }
        var (position,queue,joins) = Read(stream.ToArray(),87,"m_controlJoinsSet");
        Assert.Equal(stream.Length,position);Assert.Empty(queue);
        Assert.Equal(new[] {0,1},joins.Select(x=>x.End));
        Assert.All(joins,x=>{Assert.Equal(581277,x.ElementId);Assert.Equal(2,x.JoinedEnd);Assert.Equal(3,x.JoinType);});
    }

    [Fact]
    public void DirectCDJoinInfoCleanFieldUsesConcreteLayout()
    {
        byte[] bytes=[1,0,0,0,0xff,0xff,0xff,0xff,2,0,0,0];
        var (position,queue,_) = Read(bytes,89,"m_cleanData");
        Assert.Equal(bytes.Length,position);Assert.Empty(queue);
    }

    [Theory]
    [InlineData(1u)] [InlineData(1_000_001u)]
    public void TruncatedOrUnboundedPairsFailInsteadOfBecomingHandles(uint count)
    {
        var error=Assert.Throws<TargetInvocationException>(()=>Read(BitConverter.GetBytes(count),89,"m_cleanData"));
        Assert.Equal("DecodeFail",error.InnerException!.GetType().Name);
    }

    [Fact]
    public void NullInlineCollectionSentinelDoesNotBecomeAnOwnedPolymorphicPointer()
    {
        var (position,queue,_) = Read(BitConverter.GetBytes(uint.MaxValue),89,"m_cleanData");
        Assert.Equal(4,position);Assert.Empty(queue);
    }

    private static (int Position,List<(ushort,uint)> Queue,List<TfBlockDeserializer.WallEndJoin> Joins)
        Read(byte[] bytes,int cls,string name)
    {
        Type t=typeof(TfBlockDeserializer);
        Type cursorType=t.GetNestedType("Cursor",BindingFlags.NonPublic)!;
        object cursor=Activator.CreateInstance(cursorType)!;
        cursorType.GetField("D")!.SetValue(cursor,bytes);
        cursorType.GetField("End")!.SetValue(cursor,bytes.Length);
        var joins=new List<TfBlockDeserializer.WallEndJoin>();
        cursorType.GetField("ControlJoins")!.SetValue(cursor,joins);
        var queue=new List<(ushort,uint)>();
        var field=TfBlockDeserializer.SerializationFields(cls)!.Single(x=>x.Name==name);
        t.GetMethod("ReadField",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[cursor,field,queue,0,null]);
        return ((int)cursorType.GetField("Pos")!.GetValue(cursor)!,queue,joins);
    }
}
