using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnElementClassTests
{
    [Theory]
    [InlineData(3708)] // Run
    [InlineData(3686)] // Landing
    [InlineData(3727)] // Support
    [InlineData(3739)] // Service symbol: class identity does not make it an IFC component
    [InlineData(157)]
    public void OwnElementPrefix_IsNotAFullSubtypeOrDeferredGeometryValidation(ushort cls)
    {
        byte[] bytes = Frame(cls);
        Assert.True(TfBlockDeserializer.TryExtractOwnElementClass(bytes, 0, Size(bytes), cls, out int owner));
        Assert.Equal(123, owner);
    }

    [Theory]
    [InlineData(2007)] // GElement, independent geometry frame
    [InlineData(0)]
    [InlineData(65000)]
    public void NonElementOrUnknownClass_Rejects(ushort cls)
        => Reject(Frame(cls), cls);

    [Theory]
    [InlineData(0)] // header owner
    [InlineData(8)] // size
    [InlineData(12)] // class tag
    [InlineData(46)] // project document reference
    [InlineData(50)] // named Element.m_id
    public void ConflictingFrameOrIdentity_Rejects(int offset)
    {
        byte[] bytes = Frame(3708);
        bytes[offset] ^= 1;
        Reject(bytes, 3708);
    }

    [Fact]
    public void WrongFooter_Rejects()
    {
        byte[] bytes = Frame(3708);
        bytes[^1] ^= 1;
        Reject(bytes, 3708);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x80000000u)]
    public void NonPositiveSignedHeaderOwner_Rejects(uint owner)
    {
        byte[] bytes = Frame(3708);
        BitConverter.GetBytes(owner).CopyTo(bytes, 0);
        Reject(bytes, 3708);
    }

    [Fact]
    public void EveryTruncatedFrame_Rejects()
    {
        byte[] bytes = Frame(3708);
        for (int end = 0; end < bytes.Length; end++)
        {
            Assert.False(TfBlockDeserializer.TryExtractOwnElementClass(bytes[..end], 0, Size(bytes), 3708, out int owner));
            Assert.Equal(-1, owner);
        }
    }

    [Fact]
    public void OversizedConstraintArray_Rejects()
    {
        byte[] bytes = Frame(3708);
        BitConverter.GetBytes(uint.MaxValue - 1).CopyTo(bytes, 38);
        Reject(bytes, 3708);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void InvalidStart_Rejects(int start)
    {
        byte[] bytes = Frame(3708);
        Assert.False(TfBlockDeserializer.TryExtractOwnElementClass(bytes, start, Size(bytes), 3708, out int owner));
        Assert.Equal(-1, owner);
    }

    [Fact]
    public void ValidSubspan_DoesNotRequireTheOuterBufferEnd()
    {
        byte[] bytes = Frame(3708);
        byte[] outer = [11, 12, 13, .. bytes, 99];
        Assert.True(TfBlockDeserializer.TryExtractOwnElementClass(outer, 3, Size(bytes), 3708, out int owner));
        Assert.Equal(123, owner);
    }

    private static uint Size(byte[] bytes) => BitConverter.ToUInt32(bytes, 8);

    private static void Reject(byte[] bytes, ushort cls)
    {
        Assert.False(TfBlockDeserializer.TryExtractOwnElementClass(bytes, 0, Size(bytes), cls, out int owner));
        Assert.Equal(-1, owner);
    }

    private static byte[] Frame(ushort cls)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(123u); writer.Write(0u); writer.Write(0u); writer.Write(cls);
        for (int index = 0; index < 6; index++) writer.Write(0u);
        writer.Write(0u); // constraints
        writer.Write(0u); // cell list
        writer.Write(1u); // project document
        writer.Write(123);
        for (int index = 1; index < 8; index++) writer.Write(-1);
        writer.Write(new byte[3]);
        uint size = (uint)stream.Length - 12;
        writer.Write(size);
        stream.Position = 8; writer.Write(size);
        return stream.ToArray();
    }
}
