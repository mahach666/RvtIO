namespace RvtIO.Compact;

/// <summary>
/// Built-in parameters whose label or group the current profile prints differently from the Revit 2022 sources behind
/// <see cref="CompactLabels"/> (a caption renamed after 2022, for example "Length" -> "System Length" and "OmniClass Number" ->
/// "Classification Number", or a group renamed or moved).
/// </summary>
internal static class CompactLabelChanges
{
    internal static bool IsChanged(int builtinId) => Changed.Contains(builtinId) && !CompactLabels.HasOverride(builtinId);

    private static readonly HashSet<int> Changed =
    [
        -1155217, -1155216, -1155206, -1155205, -1155204, -1155203, -1155147, -1155128, -1155115, -1155114, -1155110,
        -1155109, -1155108, -1155104, -1155103, -1155102, -1154691, -1154690, -1153542, -1153541, -1153539, -1153538,
        -1153533, -1153532, -1140988, -1140987, -1140985, -1140978, -1140919, -1140918, -1140410, -1140240, -1140239,
        -1140160, -1140158, -1140125, -1140124, -1140088, -1140080, -1140077, -1140076, -1140075, -1140074, -1140073,
        -1140072, -1140071, -1140070, -1140069, -1140068, -1140066, -1140065, -1140055, -1140054, -1140053, -1140052,
        -1140051, -1140050, -1140013, -1140012, -1140011, -1140010, -1140007, -1140006, -1140005, -1140004, -1114247,
        -1114129, -1114125, -1114114, -1019110, -1019107, -1019106, -1019105, -1019103, -1019102, -1019101, -1019100,
        -1017064, -1017063, -1017062, -1017061, -1017009, -1017007, -1013442, -1013427, -1010401, -1010400, -1007703,
        -1007702, -1005400, -1005198, -1005195, -1005122, -1002503, -1002502, -1001576, -1001384, -1001375,
    ];
}
