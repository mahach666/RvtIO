namespace RvtIO.Internal;

/// <summary>Exact values serialized by one named CompoundStructureLayer record.
/// Kept separate from the normalized/public material-layer model so parsing details
/// cannot change existing geometry or material export behavior.</summary>
internal sealed record RawCompoundStructureLayer(
    long LayerWidthBits,
    int RawFunction,
    int RawEmbeddingType,
    int RawMaterialId,
    int RawProfileId,
    int RawLayerId,
    byte RawCapFlag);
