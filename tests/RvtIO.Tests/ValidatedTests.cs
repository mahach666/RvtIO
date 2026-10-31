using RvtIO;
using RvtIO.BuiltIns;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// Tests of the binary parser against known values of the test model.
/// </summary>
public class ValidatedTests : IClassFixture<TestFileFixture>
{
    private readonly RvtDocument _doc;

    public ValidatedTests(TestFileFixture fixture)
    {
        _doc = fixture.Document;
    }


    [SkippableFact]
    public void Document_HasElements()
    {
        Assert.True(_doc.ElementCount > 50000, $"Expected >50000 elements, got {_doc.ElementCount}");
    }

    [SkippableFact]
    public void Document_HasMultipleCategories()
    {
        var summary = _doc.GetCategorySummary();
        Assert.True(summary.Count > 15, $"Expected >15 categories, got {summary.Count}");
    }


    [SkippableTheory]
    [InlineData(263584)]
    [InlineData(263597)]
    [InlineData(263610)]
    [InlineData(263623)]
    [InlineData(263733)]
    [InlineData(263865)]
    public void Wall_Exists_And_HasCategory(int wallId)
    {
        var elem = _doc.GetElement(wallId);
        Assert.NotNull(elem);
        Assert.Equal("OST_Walls", elem!.Category);
        Assert.Equal(-2000011, elem.CategoryId);
    }

    [SkippableTheory]
    [InlineData(263584)]
    [InlineData(263597)]
    [InlineData(263610)]
    public void Wall_HasBoundingBox(int wallId)
    {
        var elem = _doc.GetElement(wallId)!;
        Assert.NotNull(elem.BoundingBox);
        Assert.Equal(6, elem.BoundingBox!.Length);
        // minZ should be 0 for ground-level walls. Wall 263623 is covered by
        // ApiTests.Wall263623_StoredBoundingBox_ReachesTheSlantedFaces and its known-gap test.
        Assert.Equal(0.0, elem.BoundingBox[2], 2);
    }

    [SkippableFact]
    public void Wall263584_BBox_MatchesReference()
    {
        // Root box m_bBox: min=(-36.087, 8.183, 0.0) max=(34.123, 32.031, 13.123); not an unnamed-double scan of the record.
        var bb = _doc.GetElement(263584)!.BoundingBox!;
        Assert.Equal(-36.087, bb[0], 2);
        Assert.Equal(8.183, bb[1], 2);
        Assert.Equal(0.0, bb[2], 2);
        Assert.Equal(34.123, bb[3], 2);
        Assert.Equal(32.031, bb[4], 2);
        Assert.Equal(13.123, bb[5], 2);
    }

    [SkippableFact]
    public void Wall263597_BBox_MatchesReference()
    {
        // Root box m_bBox: min=(-36.087, -33.797, 0.0) max=(-35.103, 31.353, 13.123).
        // The tight box ends at 30.996 in Y: the wall is trimmed at its join with wall 263584.
        var elem = _doc.GetElement(263597)!;
        var bb = elem.BoundingBox!;
        Assert.Equal(-36.087, bb[0], 2);
        Assert.Equal(-33.797, bb[1], 2);
        Assert.Equal(0.0, bb[2], 2);
        Assert.Equal(-35.103, bb[3], 2);
        Assert.Equal(30.996, bb[4], 2);
        Assert.Equal(13.123, bb[5], 2);
        Assert.Equal(31.353, elem.Geometry!.RootBoundingBox![4], 3);
    }

    [SkippableTheory]
    [InlineData(263584)]
    [InlineData(263597)]
    [InlineData(263610)]
    [InlineData(263623)]
    public void Wall_AssocLevelId_Is355(int wallId)
    {
        var elem = _doc.GetElement(wallId)!;
        var levelParam = elem.LookupParameter("LEVEL_PARAM")
            ?? elem.LookupParameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
        // The level reference should point to 355 or be findable in References
        Assert.True(
            elem.References?.Contains(355) == true ||
            (levelParam != null && levelParam.AsElementId() == 355),
            $"Wall {wallId} should reference Level 355");
    }

    [SkippableTheory]
    [InlineData(263584)]
    [InlineData(263597)]
    [InlineData(263610)]
    [InlineData(263623)]
    public void Wall_HasTypeReference_1757(int wallId)
    {
        var elem = _doc.GetElement(wallId)!;
        // After synthesis, FamilyTypeId should be 1757
        Assert.Equal(1757, elem.FamilyTypeId);
    }

    [SkippableFact]
    public void Wall_TypeElement1757_Resolves()
    {
        var wall = _doc.GetElement(263584)!;
        var type = _doc.GetTypeElement(wall);
        Assert.NotNull(type);
        Assert.Equal(1757, type!.Id);
        Assert.Equal("OST_Walls", type.Category);
    }

    [SkippableFact]
    public void Wall263584_HasGeometry()
    {
        var elem = _doc.GetElement(263584)!;
        Assert.NotNull(elem.Geometry);
        Assert.True(elem.Geometry!.Faces.Count > 0,
            $"Expected faces, got {elem.Geometry.Faces.Count}");
    }

    [SkippableTheory]
    [InlineData(263584)]
    [InlineData(263597)]
    public void Wall_HasWallParams(int wallId)
    {
        var elem = _doc.GetElement(wallId)!;

        var keyRef = elem.LookupParameter("WALL_KEY_REF_PARAM");
        Assert.NotNull(keyRef);
        Assert.Equal(2, keyRef!.AsInteger());

        var structural = elem.LookupParameter("WALL_STRUCTURAL_SIGNIFICANT");
        Assert.NotNull(structural);
        Assert.Equal(1, structural!.AsInteger());

        var usage = elem.LookupParameter("WALL_STRUCTURAL_USAGE_PARAM");
        Assert.NotNull(usage);
        Assert.Equal(1, usage!.AsInteger());

        var cross = elem.LookupParameter("WALL_CROSS_SECTION");
        Assert.NotNull(cross);
        Assert.Equal(1, cross!.AsInteger());
    }

    [SkippableFact]
    public void Wall263584_Height_MatchesReference()
    {
        var elem = _doc.GetElement(263584)!;
        var heightParam = elem.LookupParameter("WALL_HEIGHT_PARAM");
        Assert.NotNull(heightParam);
        Assert.Equal(13.123, heightParam!.AsDouble(), 2);
    }

    [SkippableTheory]
    [InlineData(263584)]
    [InlineData(263597)]
    public void Wall_Width_IsExpected(int wallId)
    {
        // Wall type 1757 has a width of 300 mm = 0.984 ft
        var elem = _doc.GetElement(wallId)!;
        var widthParam = elem.LookupParameter("WALL_ATTR_WIDTH_PARAM");
        Assert.NotNull(widthParam);
        Assert.Equal(0.984, widthParam!.AsDouble(), 2);
    }

    // Wall HOST_AREA/VOLUME/PERIMETER_COMPUTED are not emitted: stored GRep faces are pre-wall-join and lack
    // opening cuts, so face-derived wall quantities would be wrong. Face-derived quantities are limited to slab-like categories.
    [SkippableFact]
    public void Wall_ComputedQuantities_NotEmitted()
    {
        var elem = _doc.GetElement(263584)!;
        Assert.Null(elem.LookupParameter("HOST_AREA_COMPUTED"));
        Assert.Null(elem.LookupParameter("HOST_PERIMETER_COMPUTED"));
        Assert.Null(elem.LookupParameter("HOST_VOLUME_COMPUTED"));
    }


    [SkippableFact]
    public void WallType1757_IsType()
    {
        var elem = _doc.GetElement(1757)!;
        Assert.Equal("OST_Walls", elem.Category);
        Assert.True(_doc.TypeElementIds.Contains(1757),
            "Element 1757 should be in TypeElementIds set");
    }

    [SkippableFact]
    public void WallType1757_HasWidth()
    {
        var elem = _doc.GetElement(1757)!;
        var width = elem.LookupParameter("WALL_ATTR_WIDTH_PARAM");
        Assert.NotNull(width);
        Assert.Equal(0.984, width!.AsDouble(), 2);
    }

    [SkippableFact]
    public void WallTypes_Exist()
    {
        var wallTypes = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsElementType()
            .GetElementCount();
        Assert.True(wallTypes >= 5, $"Expected >=5 wall types, got {wallTypes}");
    }


    [SkippableFact]
    public void FamilyInstance289128_Category()
    {
        var elem = _doc.GetElement(289128)!;
        Assert.Equal("OST_GenericModel", elem.Category);
        Assert.Equal(-2000151, elem.CategoryId);
    }

    [SkippableFact]
    public void FamilyInstance289128_BBox_MatchesReference()
    {
        // Root box m_bBox: min=(-0.036, -34.453, 0.0) max=(2.654, -32.118, 12.385) is the root box m_bBox;
        // Element.BoundingBox is the tight box of the solid, centred on the LocationPoint Y (-33.304).
        var elem = _doc.GetElement(289128)!;
        var root = elem.Geometry!.RootBoundingBox!;
        Assert.Equal(-0.036, root[0], 2);
        Assert.Equal(-34.453, root[1], 2);
        Assert.Equal(0.0, root[2], 1);
        Assert.Equal(2.654, root[3], 2);
        Assert.Equal(-32.118, root[4], 2);
        Assert.Equal(12.385, root[5], 1);
        var bb = elem.BoundingBox!;
        Assert.Equal(0.620, bb[0], 2);
        Assert.Equal(-34.453, bb[1], 2);
        Assert.Equal(1.998, bb[3], 2);
        Assert.Equal(-32.156, bb[4], 2);
        Assert.Equal(11.729, bb[5], 2);
    }

    [SkippableFact]
    public void FamilyInstance289128_MasterSymbolId()
    {
        var elem = _doc.GetElement(289128)!;
        Assert.Equal(289087, elem.FamilyTypeId);
    }

    [SkippableFact]
    public void FamilyInstance289128_ReferencesHostWall()
    {
        var elem = _doc.GetElement(289128)!;
        Assert.NotNull(elem.References);
        Assert.Contains(263623, elem.References!);
    }

    [SkippableFact]
    public void FamilyInstance289128_HasSharedParams()
    {
        var elem = _doc.GetElement(289128)!;
        Assert.True(elem.Parameters.Count >= 30,
            $"Expected >=30 params, got {elem.Parameters.Count}");
    }

    [SkippableFact]
    public void FamilyInstance289128_SharedParam_Elevation()
    {
        // This is "Задание - Абсолютная отметка" (GUID: 44347462-a195-461d-8d35-982424b35a87)
        var elem = _doc.GetElement(289128)!;
        var p = elem.Parameters.FirstOrDefault(p =>
            p.Guid == Guid.Parse("44347462-a195-461d-8d35-982424b35a87"));
        Assert.NotNull(p);
        Assert.Equal(10.351, p!.AsDouble(), 2);
    }

    [SkippableFact]
    public void FamilyInstance289128_SharedParam_Depth()
    {
        // GUID: 35b5d8c8-5d47-45c2-907e-3e8918806050
        var elem = _doc.GetElement(289128)!;
        var p = elem.Parameters.FirstOrDefault(p =>
            p.Guid == Guid.Parse("35b5d8c8-5d47-45c2-907e-3e8918806050"));
        Assert.NotNull(p);
        Assert.Equal(0.335, p!.AsDouble(), 2);
    }

    [SkippableFact]
    public void FamilyInstance289128_ELEM_TYPE_PARAM()
    {
        var elem = _doc.GetElement(289128)!;
        var p = elem.LookupParameter("ELEM_TYPE_PARAM");
        Assert.NotNull(p);
        Assert.Equal(StorageType.ElementId, p!.StorageType);
        Assert.Equal(289087, p.AsElementId());
    }

    [SkippableFact]
    public void FamilyInstance289128_ELEM_FAMILY_AND_TYPE_PARAM()
    {
        var elem = _doc.GetElement(289128)!;
        var p = elem.LookupParameter("ELEM_FAMILY_AND_TYPE_PARAM");
        Assert.NotNull(p);
        Assert.Equal(289087, p!.AsElementId());
    }


    [SkippableFact]
    public void Level355_Exists()
    {
        var elem = _doc.GetElement(355);
        Assert.NotNull(elem);
        Assert.Equal("OST_Levels", elem!.Category);
    }

    [SkippableFact]
    public void Level2607_Exists()
    {
        var elem = _doc.GetElement(2607);
        Assert.NotNull(elem);
        Assert.Equal("OST_Levels", elem!.Category);
    }

    [SkippableFact]
    public void Levels_CountReasonable()
    {
        var levels = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Levels)
            .GetElementCount();
        Assert.True(levels >= 10, $"Expected >=10 levels, got {levels}");
    }


    [SkippableFact]
    public void Collector_WallInstances()
    {
        var count = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .GetElementCount();
        Assert.True(count >= 6, $"Expected >=6 wall instances, got {count}");
    }

    [SkippableFact]
    public void Collector_GenericModelInstances()
    {
        var count = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType()
            .GetElementCount();
        Assert.True(count > 0, $"Expected >0 GenericModel instances, got {count}");
    }

    [SkippableFact]
    public void Collector_WhereHasFaces()
    {
        var count = _doc.Collector()
            .WhereHasFaces()
            .GetElementCount();
        Assert.True(count > 100, $"Expected >100 elements with faces, got {count}");
    }

    [SkippableFact]
    public void Collector_WhereHasParameters()
    {
        var count = _doc.Collector()
            .WhereHasParameters()
            .GetElementCount();
        Assert.True(count > 40000, $"Expected >40000 elements with params, got {count}");
    }

    [SkippableFact]
    public void Collector_OfCategory_String()
    {
        var count = _doc.Collector()
            .OfCategory("OST_Walls")
            .GetElementCount();
        Assert.True(count > 0);
    }

    [SkippableFact]
    public void Collector_OfCategory_Int()
    {
        var count = _doc.Collector()
            .OfCategory(-2000011) // OST_Walls
            .GetElementCount();
        Assert.True(count > 0);
    }

    [SkippableFact]
    public void Collector_Excluding()
    {
        var allWalls = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .GetElementCount();
        var excludedWalls = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .Excluding(new[] { 263584, 263597 })
            .GetElementCount();
        Assert.Equal(allWalls - 2, excludedWalls);
    }

    [SkippableFact]
    public void Collector_WhereHasParameter()
    {
        var count = _doc.Collector()
            .WhereHasParameter("WALL_KEY_REF_PARAM")
            .GetElementCount();
        Assert.True(count >= 6, $"Expected >=6 elements with WALL_KEY_REF_PARAM, got {count}");
    }

    [SkippableFact]
    public void Collector_WhereHasParameter_Bip()
    {
        var count = _doc.Collector()
            .WhereHasParameter(BuiltInParameter.WALL_KEY_REF_PARAM)
            .GetElementCount();
        Assert.True(count >= 6);
    }

    [SkippableFact]
    public void Collector_Chained_WallInstances_WithGeometry()
    {
        var elems = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .WhereHasGeometry()
            .ToElements();
        Assert.True(elems.Count > 0);
        Assert.All(elems, e =>
        {
            Assert.Equal("OST_Walls", e.Category);
            Assert.NotNull(e.Geometry);
        });
    }

    [SkippableFact]
    public void Collector_ToElementIds()
    {
        var ids = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElementIds();
        Assert.True(ids.Count > 0);
        Assert.Contains(263584, ids);
    }

    [SkippableFact]
    public void Collector_FirstElement()
    {
        var elem = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .FirstElement();
        Assert.NotNull(elem);
        Assert.Equal("OST_Walls", elem!.Category);
    }


    [SkippableTheory]
    [InlineData(263584, -2000011)]  // Wall: OST_Walls
    [InlineData(289128, -2000151)] // GenericModel: OST_GenericModel
    public void SynthesizedParam_ELEM_CATEGORY_PARAM(int elemId, int expectedCatId)
    {
        var elem = _doc.GetElement(elemId)!;
        var p = elem.LookupParameter("ELEM_CATEGORY_PARAM");
        Assert.NotNull(p);
        Assert.Equal(expectedCatId, p!.AsInteger());
    }

    [SkippableFact]
    public void SynthesizedParam_SYMBOL_ID_PARAM()
    {
        // For FamilyInstance 289128, SYMBOL_ID_PARAM should be filled = 289087
        var elem = _doc.GetElement(289128)!;
        var p = elem.LookupParameter("SYMBOL_ID_PARAM");
        Assert.NotNull(p);
        Assert.Equal(289087, p!.AsElementId());
    }

    [SkippableFact]
    public void SynthesizedParam_ELEM_FAMILY_PARAM()
    {
        var elem = _doc.GetElement(289128)!;
        var p = elem.LookupParameter("ELEM_FAMILY_PARAM");
        Assert.NotNull(p);
        Assert.Equal(289087, p!.AsElementId());
    }


    [SkippableFact]
    public void Parameter_AsDouble_ReturnsCorrectValue()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem.LookupParameter("WALL_ATTR_WIDTH_PARAM");
        Assert.NotNull(p);
        Assert.Equal(StorageType.Double, p!.StorageType);
        double val = p.AsDouble();
        Assert.True(val > 0.9 && val < 1.0, $"Width should be ~0.984 ft, got {val}");
    }

    [SkippableFact]
    public void Parameter_AsInteger_ReturnsCorrectValue()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem.LookupParameter("WALL_KEY_REF_PARAM");
        Assert.NotNull(p);
        Assert.Equal(StorageType.Integer, p!.StorageType);
        Assert.Equal(2, p.AsInteger());
    }

    [SkippableFact]
    public void Parameter_AsElementId_ReturnsCorrectValue()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem.LookupParameter("LEVEL_PARAM");
        Assert.NotNull(p);
        Assert.Equal(StorageType.ElementId, p!.StorageType);
        Assert.Equal(355, p.AsElementId());
    }

    [SkippableFact]
    public void Parameter_AsValueString_FormatsLength()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem.LookupParameter("WALL_ATTR_WIDTH_PARAM");
        Assert.NotNull(p);
        var vs = p!.AsValueString();
        Assert.NotNull(vs);
        Assert.Contains("mm", vs!);
        // 0.984 ft * 304.8 = ~300 mm
        Assert.Contains("300", vs);
    }

    [SkippableFact]
    public void Parameter_HasValue_True()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem.LookupParameter("WALL_KEY_REF_PARAM");
        Assert.NotNull(p);
        Assert.True(p!.HasValue);
    }

    [SkippableFact]
    public void Parameter_LookupByBipEnum()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem.LookupParameter(BuiltInParameter.WALL_KEY_REF_PARAM);
        Assert.NotNull(p);
        Assert.Equal(2, p!.AsInteger());
    }

    [SkippableFact]
    public void Parameter_Indexer_ByName()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem["WALL_KEY_REF_PARAM"];
        Assert.NotNull(p);
        Assert.Equal(2, p!.AsInteger());
    }

    [SkippableFact]
    public void Parameter_Indexer_ByBip()
    {
        var elem = _doc.GetElement(263584)!;
        var p = elem[BuiltInParameter.WALL_KEY_REF_PARAM];
        Assert.NotNull(p);
        Assert.Equal(2, p!.AsInteger());
    }

    [SkippableFact]
    public void Parameter_GetParameters_Works()
    {
        var elem = _doc.GetElement(263584)!;
        var results = elem.GetParameters("WALL_KEY_REF_PARAM");
        Assert.Single(results);
        Assert.Equal(2, results[0].AsInteger());
    }


    [SkippableFact]
    public void Element_References_ContainsExpected()
    {
        var elem = _doc.GetElement(263584)!;
        Assert.NotNull(elem.References);
        Assert.Contains(355, elem.References!);   // Level 1
        Assert.Contains(1757, elem.References!);   // Wall type
    }

    [SkippableFact]
    public void Element_UniqueId_Format()
    {
        // UniqueId format: {GUID}-{elemId:08x}
        var elem = _doc.GetElement(263584)!;
        Assert.NotNull(elem.UniqueId);
        // 263584 = 0x000405a0
        Assert.EndsWith("-000405a0", elem.UniqueId!);
    }

    [SkippableFact]
    public void GetTypeElement_Resolves()
    {
        var wall = _doc.GetElement(263584)!;
        var type = _doc.GetTypeElement(wall);
        Assert.NotNull(type);
        Assert.Equal(1757, type!.Id);

        var fi = _doc.GetElement(289128)!;
        var fiType = _doc.GetTypeElement(fi);
        Assert.NotNull(fiType);
        Assert.Equal(289087, fiType!.Id);
    }

    [SkippableFact]
    public void GetElement_ByUniqueId()
    {
        var elem = _doc.GetElement(263584)!;
        Assert.NotNull(elem.UniqueId);
        var found = _doc.GetElement(elem.UniqueId!);
        Assert.NotNull(found);
        Assert.Equal(263584, found!.Id);
    }


    [SkippableFact]
    public void TypeElementIds_ContainsKnownTypes()
    {
        // 1757 is a wall type (BasicWallType)
        Assert.Contains(1757, _doc.TypeElementIds);
        // 289087 is a FamilySymbol
        Assert.Contains(289087, _doc.TypeElementIds);
    }

    [SkippableFact]
    public void TypeElementIds_DoesNotContainInstances()
    {
        // 263584 is a wall instance
        Assert.DoesNotContain(263584, _doc.TypeElementIds);
        // 289128 is a FamilyInstance
        Assert.DoesNotContain(289128, _doc.TypeElementIds);
    }

    [SkippableFact]
    public void WhereElementIsNotElementType_ExcludesTypes()
    {
        var instances = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElementIds();
        Assert.DoesNotContain(1757, instances);
        Assert.Contains(263584, instances);
    }

    [SkippableFact]
    public void WhereElementIsElementType_IncludesTypes()
    {
        var types = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsElementType()
            .ToElementIds();
        Assert.Contains(1757, types);
        Assert.DoesNotContain(263584, types);
    }


    [SkippableFact]
    public void Phase64113_Exists()
    {
        var elem = _doc.GetElement(64113);
        Assert.NotNull(elem);
    }

    [SkippableFact]
    public void Wall_ReferencesPhase()
    {
        var wall = _doc.GetElement(263584)!;
        var phaseParam = wall.LookupParameter("PHASE_CREATED");
        Assert.NotNull(phaseParam);
        Assert.Equal(64113, phaseParam!.AsElementId());
    }


    [SkippableFact]
    public void Metadata_RevitVersion()
    {
        Assert.Equal("2022", _doc.Metadata.RevitVersion);
    }

    [SkippableFact]
    public void Metadata_HasBuild()
    {
        Assert.NotNull(_doc.Metadata.Build);
        Assert.Contains("20210224", _doc.Metadata.Build!);
    }

    [SkippableFact]
    public void ProjectInfo_HasName()
    {
        Assert.NotNull(_doc.ProjectInformation);
        Assert.False(string.IsNullOrEmpty(_doc.ProjectInformation.Name));
    }


    [SkippableFact]
    public void GetElement_NonExistent_ReturnsNull()
    {
        Assert.Null(_doc.GetElement(999999999));
    }

    [SkippableFact]
    public void GetElement_ByUniqueId_NonExistent_ReturnsNull()
    {
        Assert.Null(_doc.GetElement("00000000-0000-0000-0000-000000000000-ffffffff"));
    }

    [SkippableFact]
    public void GetTypeElement_ForType_ReturnsNull()
    {
        // Type elements don't have type references of their own
        var type = _doc.GetElement(1757)!;
        var typeOfType = _doc.GetTypeElement(type);
        Assert.Null(typeOfType);
    }

    [SkippableFact]
    public void Collector_EmptyResult()
    {
        var count = _doc.Collector()
            .OfCategory(999999)
            .GetElementCount();
        Assert.Equal(0, count);
    }

    [SkippableFact]
    public void Collector_ForEach_Works()
    {
        int count = 0;
        foreach (var e in _doc.Collector().OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType())
        {
            count++;
            Assert.Equal("OST_Walls", e.Category);
        }
        Assert.True(count > 0);
    }

    [SkippableFact]
    public void Collector_Linq_Works()
    {
        var maxId = _doc.Collector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .Max(e => e.Id);
        Assert.True(maxId > 0);
    }
}
