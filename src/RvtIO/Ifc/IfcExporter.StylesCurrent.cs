using RvtIO.Model;

namespace RvtIO.Ifc;

/// <summary>Presentation styles of the current profile (IFC2X3): the surface style of every Body item
/// and the curve style of the grid axes.</summary>
public partial class IfcExporter
{
    /// <summary>The material of the volume of a room whose boundary names no material of the file. The current profile styles the
    /// Body of a space with the surface style "Room interiors" (colour 217, 243, 253, 75 % transparent,
    /// specular exponent 64) and no IfcMaterial of that name exists in the file.
    /// shortcut: the values are fixed and applied to a room body without a decoded material,
    /// until the source of that material (the graphics style of the room volume) is decoded.</summary>
    internal const int RoomInteriorsMaterialId = int.MaxValue - 7;
    private const int RoomInteriorsColour = 0x00FDF3D9;   // 0x00BBGGRR
    private const int RoomInteriorsTransparencyPercent = 75;
    private const int RoomInteriorsShininess = 64;

    /// <summary>The material of an MEP space: the current profile styles every MEP space body with the surface style "MEP space interiors"
    /// (colour 217, 255, 217, 75 % transparent, specular exponent 64).</summary>
    internal const int MepSpaceInteriorsMaterialId = int.MaxValue - 8;
    private const int MepSpaceInteriorsColour = 0x00D9FFD9;   // 0x00BBGGRR

    /// <summary>The representation items that get a surface style; other Body items (an IfcBoundingBox, a curve set)
    /// are left unstyled. The surface and shell based models are the ones accepted for a surface body.</summary>
    private static readonly HashSet<string> StyledBodyItemTypes =
    [
        "IFCFACETEDBREP", "IFCEXTRUDEDAREASOLID", "IFCFACEBASEDSURFACEMODEL", "IFCSHELLBASEDSURFACEMODEL"
    ];

    /// <summary>The body items (faceted breps and extrusions) written from the faces of a Geometry node: item entity to its geometry and node.</summary>
    internal readonly Dictionary<int, (GeometryData Geometry, int Node)> _bodyItemOrigins = new();

    /// <summary>The boundary geometry each IfcSpace body was written from, by space entity.</summary>
    private readonly Dictionary<int, GeometryData> _spaceBodyGeometry = new();

    /// <summary>The material each Body item of a product gets, by product entity.</summary>
    internal sealed class BodyStyleSources
    {
        /// <summary>One material (element id, 0 for none) for every item: the first layer of a host object's type.</summary>
        public Dictionary<int, int> Uniform { get; } = new();

        /// <summary>One material per solid, in the order of the solids (the order of the items): the fallback for an item
        /// that is not traced to a Geometry node.</summary>
        public Dictionary<int, List<int>> Solids { get; } = new();

        /// <summary>What replaces the best material of a Geometry node: <c>Override</c> every one (the material of the
        /// system type of a pipe or duct), <c>Zero</c> the null one (the structural material of the element or its type).
        /// A product listed here has its items styled from the Geometry node they were written from.</summary>
        public Dictionary<int, (int? Override, int? Zero)> Rules { get; } = new();

        /// <summary>Products exported inside one element call (the panels and mullions of a curtain wall), one list per
        /// outer element in the order of the outer elements, each in export order. The export state (the last
        /// non-null best material) flows through a list: an item whose solid has no material takes the one of the
        /// product exported before it; the state is empty at the start of a list and of every other product.</summary>
        public List<IReadOnlyList<int>> Sequences { get; } = new();
    }

    /// <summary>What <see cref="ApplyCurrentBodyStyles"/> did: products with a styled item, styled items, new
    /// presentation style assignments, items left unstyled because their solids could not be told apart.</summary>
    internal readonly record struct BodyStyleCounts(int Products, int StyledItems, int Assignments, int Skipped);

    /// <summary>Gives a product the single material every item of another one takes (a host object's first layer): the
    /// slab that holds the body of a roof gets the one of the roof.</summary>
    internal static void CopyUniformStyleSource(BodyStyleSources sources, int fromProduct, int toProduct)
    {
        if (sources.Uniform.TryGetValue(fromProduct, out int material)) sources.Uniform[toProduct] = material;
    }

    /// <summary>The curve style of one grid axis: IfcColourRgb, IfcCurveStyle (no font, no width), the presentation
    /// style assignment and the IfcStyledItem of the IfcGeometricCurveSet. The colour comes from the grid type
    /// (GRID_END_SEGMENT_COLOR, black when unreadable); the type colour is not decoded, so the axes are black.</summary>
    internal static void AddGridCurveStyle(StepWriter w, int curveSetId)
    {
        int colourId = w.Add("IFCCOLOURRGB", $"$,{D(0)},{D(0)},{D(0)}");
        int curveStyleId = w.Add("IFCCURVESTYLE", $"'',$,$,{R(colourId)}");
        int assignmentId = w.Add("IFCPRESENTATIONSTYLEASSIGNMENT", StepWriter.List(R(curveStyleId)));
        w.Add("IFCSTYLEDITEM", $"{R(curveSetId)},{StepWriter.List(R(assignmentId))},$");
    }

    /// <summary>Gives the Body items of the products an IfcStyledItem with the surface style of their material.
    /// An item written from the faces of a Geometry node takes the best material of that node
    /// (<paramref name="nodeMaterialsOf"/> for the geometry it was written from, adjusted by the product's rule);
    /// otherwise it takes its material from the solids of the product when their count agrees with the items, the
    /// single material when every solid has the same one, and stays unstyled when it cannot be told. A host object
    /// styles all its items with one material. Items of a type's representation map are styled once, by the first
    /// product that uses the map (the type is created with its first instance); an opening element is a void and
    /// has no style. <paramref name="surfaceStyleOf"/> returns the IfcSurfaceStyle of a material, 0 when it has none.
    /// IfcPresentationStyleAssignment is cached: a lookup removes
    /// the entry, so the first item of a material creates the assignment, the second reuses it and the third creates
    /// a new one.</summary>
    internal static BodyStyleCounts ApplyCurrentBodyStyles(StepWriter w, BodyStyleSources sources,
        IReadOnlyDictionary<int, (GeometryData Geometry, int Node)> itemOrigins,
        Func<GeometryData, IReadOnlyDictionary<int, int>> nodeMaterialsOf, Func<int, int> surfaceStyleOf,
        IDictionary<string, int>? counters = null, Action<int, string, string>? trace = null)
    {
        var productEntities = sources.Uniform.Keys.Concat(sources.Solids.Keys).Concat(sources.Rules.Keys).ToHashSet();
        var indexById = new Dictionary<int, int>();
        var itemTypeById = new Dictionary<int, string>();
        var productIndexes = new List<int>();
        for (int i = 0; i < w.EntityCount; i++)
        {
            var (id, type, _) = w.EntityAt(i);
            if (type is "IFCPRODUCTDEFINITIONSHAPE" or "IFCSHAPEREPRESENTATION" or "IFCREPRESENTATIONMAP" or "IFCMAPPEDITEM")
                indexById[id] = i;
            else if (StyledBodyItemTypes.Contains(type))
                itemTypeById[id] = type;
            else if (productEntities.Contains(id) && type != "IFCOPENINGELEMENT")
                productIndexes.Add(i);
        }

        string[]? Arguments(int id) => indexById.TryGetValue(id, out int index)
            ? SplitStepArguments(w.EntityAt(index).Args) : null;
        static List<int> References(string list)
        {
            var result = new List<int>();
            foreach (string token in SplitStepArguments(list.Trim('(', ')')))
                if (TryEntityId(token.Trim(), out int id)) result.Add(id);
            return result;
        }

        // The styleable items of the Body representations of a product, each with the representation map it comes
        // from (0 for an item of the product itself): a mapped item is replaced by the items of its map.
        List<(int Item, int Map)> BodyItems(string[] product)
        {
            var items = new List<(int, int)>();
            if (product.Length < 7 || !TryEntityId(product[6].Trim(), out int definitionId)
                || Arguments(definitionId) is not { Length: 3 } definition)
                return items;
            foreach (int representationId in References(definition[2]))
            {
                if (Arguments(representationId) is not { Length: 4 } representation || representation[1] != "'Body'")
                    continue;
                foreach (int itemId in References(representation[3]))
                {
                    if (itemTypeById.ContainsKey(itemId)) items.Add((itemId, 0));
                    else if (Arguments(itemId) is { Length: 2 } mapped && TryEntityId(mapped[0].Trim(), out int mapId)
                        && Arguments(mapId) is { Length: 2 } map && TryEntityId(map[1].Trim(), out int mappedRepresentationId)
                        && Arguments(mappedRepresentationId) is { Length: 4 } mappedRepresentation)
                        items.AddRange(References(mappedRepresentation[3]).Where(itemTypeById.ContainsKey)
                            .Select(item => (item, mapId)));
                }
            }
            return items;
        }

        var assignmentByMaterial = new Dictionary<int, int>();
        var styledMaps = new HashSet<int>();
        var styledItems = new HashSet<int>();
        int styledProducts = 0, styledItemCount = 0, assignments = 0, skipped = 0;

        // One product: the items of the Body item list that no earlier product styled, their materials, their styles.
        // 'state' is the export state of the element call the product belongs to (0: empty); a product whose maps were
        // all styled before runs no body export and leaves it alone.
        void Process(int productIndex, ref int state)
        {
            var (productId, productType, productArgs) = w.EntityAt(productIndex);
            var items = BodyItems(SplitStepArguments(productArgs)).Where(item => !styledMaps.Contains(item.Map)).ToList();
            if (items.Count == 0) return;

            // The material of every item (0: none), -1 while unknown, and how it was found.
            var materials = Enumerable.Repeat(-1, items.Count).ToList();
            var how = new string[items.Count];
            // A host object without a material in its first layer is exported like any other body: each item takes the
            // best material of its node.
            if (sources.Uniform.TryGetValue(productId, out int uniform) && uniform > 0)
            {
                materials = Enumerable.Repeat(uniform, items.Count).ToList();
                Array.Fill(how, "uniform");
            }
            else
            {
                sources.Rules.TryGetValue(productId, out var rule);
                for (int i = 0; i < items.Count; i++)
                    if (itemOrigins.TryGetValue(items[i].Item, out var origin)
                        && nodeMaterialsOf(origin.Geometry).TryGetValue(origin.Node, out int nodeMaterial))
                    {
                        materials[i] = rule.Override ?? (nodeMaterial == 0 ? rule.Zero ?? 0 : nodeMaterial);
                        how[i] = "node";
                    }
                if (materials.Contains(-1) && sources.Solids.TryGetValue(productId, out var solids) && solids.Count > 0)
                {
                    bool byIndex = solids.Count == items.Count;
                    if (byIndex || solids.Distinct().Count() == 1)
                        for (int i = 0; i < items.Count; i++)
                            if (materials[i] == -1)
                            {
                                materials[i] = byIndex ? solids[i] : solids[0];
                                how[i] = "solids";
                            }
                }
            }

            // Export state: the last non-null best material of the element. Extrusions are styled first, solid by solid, each taking the
            // state as it is then (one still finding it empty takes the state at the end of that stage). The other solids follow: all their
            // best materials are settled first, so an item with a null material takes the LAST material of any of them, whatever its place.
            // The state carries over between the products of one element call (a curtain wall); items come in depth-first order of the nodes.
            var ownMaterials = materials.ToArray();
            int incoming = state;
            bool IsExtrusion(int index) => itemTypeById.TryGetValue(items[index].Item, out var kind) && kind == "IFCEXTRUDEDAREASOLID";
            int running = incoming;
            var stateAtExtrusion = new int[items.Count];
            for (int i = 0; i < items.Count; i++)
                if (IsExtrusion(i))
                {
                    if (materials[i] > 0) running = materials[i];
                    stateAtExtrusion[i] = running;
                }
            int afterExtrusions = running;
            for (int i = 0; i < items.Count; i++)
                if (!IsExtrusion(i) && materials[i] > 0) running = materials[i];
            int afterAll = running;
            for (int i = 0; i < items.Count; i++)
                if (materials[i] == 0)
                {
                    int fill = IsExtrusion(i) ? (stateAtExtrusion[i] > 0 ? stateAtExtrusion[i] : afterExtrusions) : afterAll;
                    if (fill <= 0) continue;
                    materials[i] = fill;
                    how[i] = fill == incoming && !ownMaterials.Any(material => material > 0) ? "sequence" : "state";
                }
            if (afterAll > 0) state = afterAll;

            void Count(string what, int by = 1)
            {
                if (counters == null) return;
                string key = productType + ":" + what;
                counters[key] = counters.TryGetValue(key, out int known) ? known + by : by;
            }
            Count("products");
            Count("items", items.Count);
            if (!sources.Uniform.ContainsKey(productId) && !sources.Rules.ContainsKey(productId) && !sources.Solids.ContainsKey(productId))
                Count("noSource");

            if (trace != null && materials.Any(material => material <= 0))
                trace(productId, productType, $"items={items.Count} how=[{string.Join(",", how.Select(h => h ?? "-"))}] own=[{string.Join(",", ownMaterials)}] materials=[{string.Join(",", materials)}]"
                    + $" rule={(sources.Rules.TryGetValue(productId, out var traced) ? $"({traced.Override},{traced.Zero})" : "none")}"
                    + $" solids=[{(sources.Solids.TryGetValue(productId, out var tracedSolids) ? string.Join(",", tracedSolids) : "none")}]"
                    + $" origins={items.Count(item => itemOrigins.ContainsKey(item.Item))}");

            bool any = false;
            for (int i = 0; i < items.Count; i++)
            {
                int materialId = materials[i];
                if (materialId < 0)
                {
                    skipped++;
                    Count("unresolved");
                }
                else Count(how[i]);
                if (materialId == 0) Count("nullMaterial");
                if (materialId <= 0 || !styledItems.Add(items[i].Item)) continue;
                int surfaceStyleId = surfaceStyleOf(materialId);
                if (surfaceStyleId == 0)
                {
                    Count("noSurfaceStyle");
                    continue;
                }
                if (!assignmentByMaterial.Remove(materialId, out int assignmentId))
                {
                    assignmentId = w.Add("IFCPRESENTATIONSTYLEASSIGNMENT", StepWriter.List(R(surfaceStyleId)));
                    assignmentByMaterial[materialId] = assignmentId;
                    assignments++;
                }
                w.Add("IFCSTYLEDITEM", $"{R(items[i].Item)},{StepWriter.List(R(assignmentId))},$");
                styledItemCount++;
                Count("styled");
                any = true;
            }
            if (any) styledProducts++;
            // A map is the type of its first user; a user that could not
            // tell the material of an item of the map leaves the map to the next one.
            styledMaps.UnionWith(items.Select((item, index) => (item.Map, index)).Where(pair => pair.Map != 0)
                .GroupBy(pair => pair.Map).Where(group => group.All(pair => materials[pair.index] >= 0)).Select(group => group.Key));
        }

        // The sub-elements of a curtain wall are exported inside the call of the wall, in its own order; every other
        // product is an element call of its own, in the order of the file.
        var productIndexById = productIndexes.ToDictionary(index => w.EntityAt(index).Id);
        var processed = new HashSet<int>();
        foreach (var sequence in sources.Sequences)
        {
            int state = 0;
            foreach (int sequenceProductId in sequence)
                if (productIndexById.TryGetValue(sequenceProductId, out int sequenceIndex) && processed.Add(sequenceIndex))
                    Process(sequenceIndex, ref state);
        }
        foreach (int productIndex in productIndexes)
            if (processed.Add(productIndex))
            {
                int state = 0;
                Process(productIndex, ref state);
            }
        return new BodyStyleCounts(styledProducts, styledItemCount, assignments, skipped);
    }
}
