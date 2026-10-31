using RvtIO.Model;
using RvtIO.Parsing;

namespace RvtIO.RootProbe;

/// <summary>Read-only diagnostic projections of materialized parameters; they are not serialized values.</summary>
internal static class ParameterInputProbe
{
    internal static object Materialized(Parameter parameter) => new
    {
        parameter.Id, parameter.Value, parameter.Definition, parameter.StorageType,
        parameter.IsApproximate, parameter.Guid,
        parameter.SpecType, parameter.UnitSymbol, parameter.ConversionFactor,
    };

    internal static object OwnRoot(ushort classId, TfBlockDeserializer.NativeParams? root) => new
    {
        classId,
        fields = root?.Fields,
        elementIds = root?.ElementIds.Select(item => new[] { item.ParamId, item.Value }),
        ints = root?.Ints.Select(item => new[] { item.ParamId, item.Value }),
        strings = root?.Strings.Select(item => new object[] { item.ParamId, item.Value }),
        doubles = root?.Doubles.Select(item => new object[] { item.ParamId, item.Value }),
        rootParameterStoresComplete = root?.RootParameterStoresComplete,
    };
}
