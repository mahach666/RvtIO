using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class MemberTypeNameSourceTests
{
    private const int MullionCategoryId = -2000171;

    [Fact]
    public void PostProcess_AppliesMasterNameAfterNativeTypeAndCategoryPropagation()
    {
        var master = Master(200, "50x100 mm");
        var instance = Instance(300, master.Id, "Wrong instance name");
        instance.TypeId = 999;
        instance.CategoryId = null;
        instance.Category = null;
        instance.Parameters.Clear();
        var info = new RevitFileInfo();
        info.Elements.AddRange([master, instance]);
        var state = ValidatedState(instance.Id, master.Id, master.Name!);

        ElementBuilder.PostProcess(info, state);

        Assert.Equal(MullionCategoryId, instance.CategoryId);
        Assert.Equal(master.Id, instance.TypeId);
        Assert.Equal(master.Id, instance.FamilyTypeId);
        Assert.Equal("50x100 mm", instance.Name);
        Assert.Equal("50x100 mm", master.Name);
    }

    [Fact]
    public void MemberTypeValidation_DoesNotUseTransformSymbolId()
    {
        var master = Master(200, "50x150 mm");
        var instance = Instance(300, master.Id, "Wrong instance name");
        instance.Transform = new InstanceTransform { SymbolId = 999 };
        var elements = new List<Element> { master, instance };
        var state = ValidatedState(instance.Id, master.Id, master.Name!);

        Apply(elements, state);

        Assert.Equal("50x150 mm", instance.Name);
        Assert.Equal(999, instance.Transform.SymbolId);
    }

    [Fact]
    public void NamelessValidatedMember_ExportsBothReferencePathsAndCreatesQto()
    {
        var master = Master(200, "64 x 128 rectangular");
        var instance = Instance(300, master.Id, null, withGeometry: true);
        var elements = new List<Element> { master, instance };
        var state = ValidatedState(instance.Id, master.Id, master.Name!);
        Apply(elements, state);
        var info = new RevitFileInfo();
        info.Elements.AddRange(elements);
        string path = Path.Combine(Path.GetTempPath(),
            $"revitreader-member-name-{Guid.NewGuid():N}.ifc");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.Single(Regex.Matches(ifc, "=IFCMEMBER\\("));
            // Occurrence Reference is still emitted exactly once; the type additionally
            // has its independent inline IsExternal-only common set.
            Assert.Equal(2, Regex.Matches(ifc, "'Pset_MemberCommon'").Count);
            Assert.Single(Regex.Matches(ifc, "'Pset_QuantityTakeOff'"));
            Assert.Single(Regex.Matches(ifc,
                @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('64 x 128 rectangular'\)"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MemberTypeValidation_IsIdempotentWhenNameAlreadyMatches()
    {
        var master = Master(200, "150x150x100 mm");
        var instance = Instance(300, master.Id, master.Name);
        var elements = new List<Element> { master, instance };
        var state = ValidatedState(instance.Id, master.Id, master.Name!);

        Apply(elements, state);
        Apply(elements, state);

        Assert.Equal(master.Name, instance.Name);
    }

    [Theory]
    [InlineData("wrong-instance-category")]
    [InlineData("missing-native-row")]
    [InlineData("missing-native-field")]
    [InlineData("nonpositive-master")]
    [InlineData("self-master")]
    [InlineData("unresolved-master")]
    [InlineData("type-id-mismatch")]
    [InlineData("family-type-id-mismatch")]
    [InlineData("wrong-target-category")]
    [InlineData("missing-table-name")]
    [InlineData("blank-table-name")]
    [InlineData("missing-native-name")]
    [InlineData("conflicting-native-name")]
    [InlineData("conflicting-target-name")]
    public void MemberTypeValidation_FailsClosedWithoutEveryProvenanceLink(
        string brokenValidation)
    {
        var master = Master(200, "Canonical member type");
        var instance = Instance(300, master.Id, "Existing instance name");
        var elements = new List<Element> { master, instance };
        var state = ValidatedState(instance.Id, master.Id, master.Name!);

        switch (brokenValidation)
        {
            case "wrong-instance-category":
                instance.CategoryId = -2000170;
                break;
            case "missing-native-row":
                state.NativeParams.Clear();
                break;
            case "missing-native-field":
                state.NativeParams[(uint)instance.Id] = new();
                break;
            case "nonpositive-master":
                state.NativeParams[(uint)instance.Id] = MasterNativeParams(0);
                break;
            case "self-master":
                state.NativeParams[(uint)instance.Id] =
                    MasterNativeParams(instance.Id);
                break;
            case "unresolved-master":
                state.NativeParams[(uint)instance.Id] = MasterNativeParams(999);
                instance.TypeId = 999;
                SetFamilyType(instance, 999);
                break;
            case "type-id-mismatch":
                instance.TypeId = 999;
                break;
            case "family-type-id-mismatch":
                SetFamilyType(instance, 999);
                break;
            case "wrong-target-category":
                master.CategoryId = -2000170;
                break;
            case "missing-table-name":
                state.ElementNameTable.Clear();
                break;
            case "blank-table-name":
                state.ElementNameTable[(uint)master.Id] = "   ";
                break;
            case "missing-native-name":
                state.NativeElementNames.Clear();
                break;
            case "conflicting-native-name":
                state.NativeElementNames[(uint)master.Id] = "Other native name";
                break;
            case "conflicting-target-name":
                master.Name = "Other target name";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(brokenValidation));
        }

        Apply(elements, state);

        Assert.Equal("Existing instance name", instance.Name);
    }

    private static void Apply(IReadOnlyList<Element> elements, ParsingState state) =>
        ElementBuilder.ApplyValidatedMemberTypeNames(
            elements, elements.ToDictionary(element => element.Id),
            state.NativeParams, state.ElementNameTable,
            state.NativeElementNames);

    private static ParsingState ValidatedState(
        int instanceId, int masterId, string name)
    {
        var state = new ParsingState();
        state.NativeParams[(uint)instanceId] = MasterNativeParams(masterId);
        state.ElementNameTable[(uint)masterId] = name;
        state.NativeElementNames[(uint)masterId] = name;
        return state;
    }

    private static TfBlockDeserializer.NativeParams MasterNativeParams(int masterId)
    {
        var nativeParams = new TfBlockDeserializer.NativeParams();
        nativeParams.Fields["m_masterSymbolId"] = masterId;
        return nativeParams;
    }

    private static Element Master(int id, string name) => new()
    {
        Id = id,
        TypeId = -1,
        CategoryId = MullionCategoryId,
        Category = "OST_CurtainWallMullions",
        Name = name,
    };

    private static Element Instance(
        int id, int masterId, string? name, bool withGeometry = false)
    {
        var instance = new Element
        {
            Id = id,
            TypeId = masterId,
            IsDiscovered = true,
            CategoryId = MullionCategoryId,
            Category = "OST_CurtainWallMullions",
            Name = name,
            References = [3],
            Parameters =
            [
                new Parameter
                {
                    Id = -1002050,
                    Definition = new Definition
                    {
                        Id = -1002050,
                        Name = "ELEM_TYPE_PARAM",
                    },
                    StorageType = StorageType.ElementId,
                    Value = masterId,
                },
                new Parameter
                {
                    Id = -1012100,
                    StorageType = StorageType.ElementId,
                    Value = 3,
                },
            ],
        };
        if (withGeometry)
        {
            instance.BoundingBox = [-0.1, 0, 0, 0.1, 10, 0.2];
            instance.Geometry = RectangularPrism();
        }
        return instance;
    }

    private static void SetFamilyType(Element element, int typeId)
    {
        Parameter parameter = element.Parameters.Single(item =>
            item.Definition?.Name == "ELEM_TYPE_PARAM");
        parameter.Value = typeId;
    }

    private static GeometryData RectangularPrism()
    {
        double[][] points =
        [
            [-0.1, 0, 0], [-0.1, 0, 0.2], [0.1, 0, 0], [0.1, 0, 0.2],
            [-0.1, 10, 0], [-0.1, 10, 0.2], [0.1, 10, 0], [0.1, 10, 0.2],
        ];
        int[][] faces =
        [
            [0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1],
            [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3],
        ];
        return new GeometryData
        {
            BoundingBox = [-0.1, 0, 0, 0.1, 10, 0.2],
            Faces = faces.Select(face => new GeoFace
            {
                OuterVertices = face.Select(index => points[index]).ToList(),
            }).ToList(),
            BrepSolids =
            [
                new GeoBrepSolid
                {
                    IsClosed = true,
                    FaceIndices = [0, 1, 2, 3, 4, 5],
                },
            ],
        };
    }
}
