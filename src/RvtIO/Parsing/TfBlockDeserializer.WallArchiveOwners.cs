namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallArchiveOwnerReference(int QueueIndex, ushort ClassId,
        uint Reference, int? RootOwnerId);
    internal sealed record OwnWallArchiveOwnerLinks(int OwnerId, uint ExternalPointerCount,
        uint RootArchiveKey, WallArchiveOwnerReference List,
        IReadOnlyList<WallArchiveOwnerReference> Steps, int End);

    /// <summary>Root links in the fresh non-null-document ElemRec.getElement archive context. The database constructs a
    /// new context and selects one document; the utility layer installs that external entry before the root
    /// (externalCount + 1). This is not an ElementId encoding or a resolver for arbitrary or reused archive contexts.
    /// Unknown, null and document references remain raw and unresolved. Later FIFO, runtime geometry and
    /// current/active faces are outside this prefix.</summary>
    internal static OwnWallArchiveOwnerLinks? ExtractOwnWallArchiveOwnerLinks(
        byte[] data, int start, uint size, ushort classId, out string? failure)
    {
        var value = ExtractOwnWallSixPhaseGeometryHistories(data, start, size, classId, out failure);
        if (value?.Histories is not { Complete: true } histories || failure != null) return null;
        // The own root reader already requires matching positive header/Element ids,
        // document-access key 1 and the named owned GeomStepList pointer.
        const uint externalCount = 1;
        uint rootKey = externalCount + 1;
        var list = histories.List;
        return new(list.OwnerId, externalCount, rootKey,
            Link(list.ListPointer.QueueIndex!.Value, 2086, list.OwnerReference),
            histories.Steps.Select(step => Link(step.QueueIndex, step.ClassId, step.OwnerReference)).ToArray(),
            histories.End);

        WallArchiveOwnerReference Link(int index, ushort cls, uint reference)
            => new(index, cls, reference, reference == rootKey ? list.OwnerId : null);
    }
}
