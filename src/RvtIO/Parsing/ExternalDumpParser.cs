using System.Text.RegularExpressions;
using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static class ExternalDumpParser
{
    /// <summary>Default shape of the prefix that class names in the dump carry in front of the class name proper:
    /// four letters (capital, lowercase, capital, lowercase) followed by a capital letter. A dump with another prefix gives its own pattern.</summary>
    private const string DefaultClassPrefixPattern = @"\b[A-Z][a-z][A-Z][a-z](?=[A-Z])";

    private const string ClassMark = "@";

    /// <summary>Parse a text dump of the model for CompoundStructure layers, element names, and episode data.
    /// Every line is normalised to <see cref="ClassMark"/> + the class name before it is matched, so that only prefixed names match and
    /// the rules below name the class alone. <paramref name="classPrefixPattern"/> is a regular expression for the prefix of the class names
    /// (null: <see cref="DefaultClassPrefixPattern"/>).</summary>
    public static void Parse(string dumpPath, ParsingState state, TextWriter? diag = null, string? classPrefixPattern = null)
    {
        var classPrefix = new Regex(classPrefixPattern ?? DefaultClassPrefixPattern, RegexOptions.CultureInvariant);
        if (!File.Exists(dumpPath))
        {
            diag?.WriteLine($"The external dump not found at {dumpPath}, skipping");
            return;
        }

        var wallTypeWidths = state.WallTypeWidths;
        var elemBipDoubles = state.ElemBipDoubles;

        // Map the class names to BIP IDs for thickness/width
        var classNameToBip = new Dictionary<string, int>
        {
            [ClassMark + "BasicWallType"] = -1001000,
            [ClassMark + "FloorAttributes"] = -1001902,
            [ClassMark + "CompoundCeilingType"] = -1002206,
            [ClassMark + "RoofAttributes"] = -1001601,
            [ClassMark + "BuildingPadType"] = -1001902,
            [ClassMark + "RoofSoffitType"] = -1001601,
        };

        int dumpWallAdded = 0, dumpFloorAdded = 0, dumpCeilingAdded = 0, dumpRoofAdded = 0;
        int namesExtracted = 0, episodesExtracted = 0, paramCaptionsExtracted = 0;
        bool inParamElemBody = false;
        uint paramElemHandle = 0;

        // Episode table: maps episode index -> GUID for UniqueId construction
        var episodeGuids = new List<string>(); // index = episode number
        bool inEpisodeList = false;
        int nextEpisodeIdx = -1;

        uint currentHandle = 0;
        int currentBipId = 0;
        bool inTargetBody = false;
        bool inLayers = false;
        var layerWidths = new List<double>();

        // State for name/episode extraction - tracks any Body section
        uint bodyHandle = 0;
        bool inAnyBody = false;
        bool foundElemRec = false;
        bool foundSymbolInfo = false;
        bool nameExtractedForCurrent = false;

        void FlushCompoundStructure()
        {
            if (!inTargetBody || currentHandle == 0 || layerWidths.Count == 0) return;
            double totalW = layerWidths.Sum();
            if (totalW < 0.001) return;
            if (currentBipId == -1001000) { wallTypeWidths.TryAdd(currentHandle, totalW); dumpWallAdded++; }
            else if (currentBipId == -1001902) dumpFloorAdded++;
            else if (currentBipId == -1002206) dumpCeilingAdded++;
            else if (currentBipId == -1001601) dumpRoofAdded++;
            if (!elemBipDoubles.TryGetValue(currentHandle, out var bd))
                elemBipDoubles[currentHandle] = bd = new Dictionary<int, double>();
            bd.TryAdd(currentBipId, totalW);
        }

        foreach (var rawLine in File.ReadLines(dumpPath))
        {
            string line = classPrefix.Replace(rawLine, ClassMark);
            if (line.StartsWith("Part: Body; Handle:"))
            {
                FlushCompoundStructure();

                var parts = line.Split(';');
                if (parts.Length >= 2)
                {
                    var handleStr = parts[1].Trim().Split(':').LastOrDefault()?.Trim();
                    if (uint.TryParse(handleStr, out uint h))
                    {
                        bodyHandle = h;
                        inAnyBody = true;
                        foundElemRec = false;
                        foundSymbolInfo = false;
                        nameExtractedForCurrent = false;

                        inParamElemBody = line.Contains(ClassMark + "ParamElem");
                        if (inParamElemBody) paramElemHandle = h;

                        string? matchedClass = null;
                        foreach (var cn in classNameToBip.Keys)
                            if (line.Contains(cn)) { matchedClass = cn; break; }

                        if (matchedClass != null)
                        {
                            currentHandle = h;
                            currentBipId = classNameToBip[matchedClass];
                            inTargetBody = true;
                        }
                        else
                        {
                            inTargetBody = false;
                        }
                    }
                }
                inLayers = false;
                layerWidths.Clear();
                continue;
            }

            if (line.StartsWith("Part:") && !line.StartsWith("Part: Body;"))
            {
                FlushCompoundStructure();
                inTargetBody = false;
                inAnyBody = false;
                inLayers = false;
                layerWidths.Clear();
                continue;
            }

            if (line.Contains("VariantArray EpisodeList"))
            {
                inEpisodeList = true;
                continue;
            }
            if (inEpisodeList)
            {
                if (line.Contains(ClassMark + "Episode item["))
                {
                    // Extract index from "item[N]"
                    int bracket = line.IndexOf('[');
                    int closeBracket = line.IndexOf(']', bracket + 1);
                    if (bracket >= 0 && closeBracket > bracket
                        && int.TryParse(line.AsSpan(bracket + 1, closeBracket - bracket - 1), out int idx))
                        nextEpisodeIdx = idx;
                    continue;
                }
                if (nextEpisodeIdx >= 0 && line.Contains("GUID GUID"))
                {
                    var guid = ExtractDottedValue(line);
                    if (!string.IsNullOrWhiteSpace(guid))
                    {
                        while (episodeGuids.Count <= nextEpisodeIdx)
                            episodeGuids.Add("");
                        episodeGuids[nextEpisodeIdx] = guid.ToLowerInvariant();
                    }
                    nextEpisodeIdx = -1;
                    continue;
                }
                // End of episode list when we hit a non-episode section
                if (!line.Contains("char Strength") && !line.Contains(ClassMark + "Episode")
                    && !line.Contains("GUID GUID") && line.TrimStart().Length > 0
                    && !line.Contains("VariantArray"))
                {
                    inEpisodeList = false;
                }
            }

            if (!inAnyBody) continue;

            if (inParamElemBody && line.Contains("String Caption"))
            {
                var caption = ExtractDottedValue(line);
                if (!string.IsNullOrWhiteSpace(caption) && caption != "\"\"")
                {
                    // Strip the ellipsis or & prefix
                    if (caption.StartsWith("\u2026") || caption.StartsWith("&"))
                        caption = caption[1..];
                    if (caption.Length >= 2 && !state.ForgeParamDefs.ContainsKey(paramElemHandle))
                    {
                        state.ForgeParamDefs[paramElemHandle] = ("", caption);
                        paramCaptionsExtracted++;
                    }
                    else if (state.ForgeParamDefs.TryGetValue(paramElemHandle, out var ex) && ex.DisplayName == null)
                    {
                        state.ForgeParamDefs[paramElemHandle] = (ex.ForgeId, caption);
                        paramCaptionsExtracted++;
                    }
                }
                inParamElemBody = false; // only first Caption per body
            }

            // Track ElemRec section (contains History with CreationDate for UniqueId)
            if (line.Contains(ClassMark + "ElemRec ElemRec"))
            {
                foundElemRec = true;
                continue;
            }

            // Extract CreationDate from ElemRec.History for episode-based UniqueId
            if (foundElemRec && line.Contains("int CreationDate"))
            {
                var val = ExtractDottedValue(line);
                if (int.TryParse(val, out int episodeNum) && episodeNum >= 0)
                {
                    state.ElemEpisodeNumbers.TryAdd(bodyHandle, episodeNum);
                    episodesExtracted++;
                }
                continue;
            }

            // Track SymbolInfo section
            if (line.Contains(ClassMark + "SymbolInfo SymbolInfo"))
            {
                foundSymbolInfo = true;
                continue;
            }

            // Extract String Name from SymbolInfo (wall types, family symbols, etc.)
            if (foundSymbolInfo && !nameExtractedForCurrent && line.Contains("String Name"))
            {
                var name = ExtractDottedValue(line);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    state.MeshElementNames.TryAdd(bodyHandle, name);
                    namesExtracted++;
                    nameExtractedForCurrent = true;
                }
                foundSymbolInfo = false;
                continue;
            }

            // Extract String Text for levels/datums (different from SymbolInfo.Name)
            if (foundElemRec && !nameExtractedForCurrent && line.Contains("String Text") && !line.Contains("VariantArray"))
            {
                var text = ExtractDottedValue(line);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    state.MeshElementNames.TryAdd(bodyHandle, text);
                    namesExtracted++;
                    nameExtractedForCurrent = true;
                }
                continue;
            }

            // Stop tracking ElemRec/SymbolInfo when we hit a new major section
            if (foundElemRec && (line.Contains("Handle RenderStyleId") || line.Contains("Handle PreviewElemId")))
            {
                foundElemRec = false;
                foundSymbolInfo = false;
            }

            if (!inTargetBody) continue;
            if (line.Contains("VariantArray Layers"))
            {
                inLayers = true;
                continue;
            }
            if (inLayers && line.Contains("double LayerWidth"))
            {
                var val = line.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries).Last().Trim();
                if (double.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, out double lw))
                    layerWidths.Add(lw);
                continue;
            }
            if (inLayers && !line.Contains(ClassMark + "CompoundStructureLayer") && !line.Contains("double LayerWidth")
                && !line.Contains("int Layer") && !line.Contains("Handle MaterialId") && !line.Contains("Handle ProfileId")
                && !line.Contains("bool LayerCapFlag") && !line.Contains("int EmbeddingType"))
            {
                inLayers = false;
            }
        }

        FlushCompoundStructure();

        if (episodeGuids.Count > 0)
        {
            state.EpisodeGuids = episodeGuids;
        }
        else if (episodesExtracted > 0)
        {
            // The dump gave element episode indices but no EpisodeList of its own. They index a different
            // table than ParsingState.EpisodeGuids (filled from the file's own History stream), so the table is
            // cleared and BuildUniqueId falls back to the document GUID.
            state.EpisodeGuids = new List<string>();
            diag?.WriteLine($"The external dump: {episodesExtracted} episode indices found without an EpisodeList of their own - clearing EpisodeGuids to avoid mixing indexings");
        }

        diag?.WriteLine($"The external dump: walls={dumpWallAdded}, floors={dumpFloorAdded}, ceilings={dumpCeilingAdded}, roofs={dumpRoofAdded}");
        diag?.WriteLine($"The external dump: {namesExtracted} element names, {episodesExtracted} episodes, {paramCaptionsExtracted} param captions");
        diag?.WriteLine($"Total wallTypeWidths: {wallTypeWidths.Count}");
    }

    /// <summary>
    /// Extract a value from a dump line like "    String Name . . . . . . Value"
    /// The value follows the last sequence of dots.
    /// </summary>
    private static string ExtractDottedValue(string line)
    {
        int lastDotSpace = -1;
        for (int i = line.Length - 2; i >= 0; i--)
        {
            if (line[i] == '.' && line[i + 1] == ' ')
            {
                lastDotSpace = i + 2;
                break;
            }
        }
        return lastDotSpace >= 0 ? line[lastDotSpace..].Trim() : "";
    }
}
