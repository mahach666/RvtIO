using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static class GRepParser
{
    public static GRepData? Parse(byte[] d, int start, int end)
    {
        int len = end - start;
        if (len < 16) return null;

        // GInfoBlob header: [Tag:4][CtrlCmd:4][GStyleElemId:4][Flags:4] = 16 bytes
        int tag = BitConverter.ToInt32(d, start);
        int gStyleElemId = BitConverter.ToInt32(d, start + 8);
        uint flags = BitConverter.ToUInt32(d, start + 12);

        var result = new GRepData { GStyleElemId = gStyleElemId, Flags = flags };

        for (int i = start; i + 2 + 48 <= end; i++)
        {
            if (d[i] == 0x2C && d[i + 1] == 0x08)
            {
                // 6 doubles after marker = bbox [minX, minY, minZ, maxX, maxY, maxZ]
                int dblOff = i + 2;
                if (dblOff + 48 > end) break;
                var bbox = new double[6];
                for (int k = 0; k < 6; k++)
                    bbox[k] = BitConverter.ToDouble(d, dblOff + k * 8);

                bool valid = true;
                for (int k = 0; k < 6; k++) if (!double.IsFinite(bbox[k])) { valid = false; break; }
                if (valid && bbox[0] <= bbox[3] && bbox[1] <= bbox[4] && bbox[2] <= bbox[5])
                {
                    result.BoundingBox ??= bbox;
                    break;
                }
            }
        }

        // Fallback: after header, skip node table entries ([type:2][idx:4] per GGroup node,
        // terminated by non-GGroup) then look for 6 consecutive doubles forming a valid bbox
        if (result.BoundingBox == null && start + 24 + 48 <= end)
        {
            int nodeOff = start + 16;
            if (nodeOff + 8 <= end)
            {
                int count1 = BitConverter.ToInt32(d, nodeOff);
                nodeOff += 8;
                while (nodeOff + 6 <= end && count1 > 0)
                {
                    ushort nodeType = BitConverter.ToUInt16(d, nodeOff);
                    if (nodeType == 0x07DF) { nodeOff += 6; count1--; }
                    else break;
                }
                if (nodeOff + 48 <= end)
                {
                    var bbox = new double[6];
                    for (int k = 0; k < 6; k++)
                        bbox[k] = BitConverter.ToDouble(d, nodeOff + k * 8);
                    bool valid = true;
                    for (int k = 0; k < 6; k++) if (!double.IsFinite(bbox[k])) { valid = false; break; }
                    if (valid && bbox[0] <= bbox[3] && bbox[1] <= bbox[4] && bbox[2] <= bbox[5]
                        && (bbox[3] - bbox[0] + bbox[4] - bbox[1] + bbox[5] - bbox[2]) > 1e-10)
                        result.BoundingBox = bbox;
                }
            }
        }

        // Scan for face planes: [envelope:32][orientByte:1][origin:24][xvec:24][yvec:24] = 105
        // bytes The envelope is 4 doubles (minU, minV, maxU, maxV) immediately before
        // orientByte+plane.
        for (int i = start + 16; i + 105 <= end; i++)
        {
            byte orientByte = d[i + 32];
            if (orientByte > 1) continue;

            double minU = BitConverter.ToDouble(d, i);
            double minV = BitConverter.ToDouble(d, i + 8);
            double maxU = BitConverter.ToDouble(d, i + 16);
            double maxV = BitConverter.ToDouble(d, i + 24);
            if (!double.IsFinite(minU) || !double.IsFinite(minV) || !double.IsFinite(maxU) || !double.IsFinite(maxV))
                continue;
            if (minU > maxU || minV > maxV) continue;
            // Envelope must have non-zero size (except for degenerate faces)
            double envArea = (maxU - minU) * (maxV - minV);
            if (envArea < 1e-12) continue;

            int planeOff = i + 33;
            if (planeOff + 72 > end) break;
            var vals = new double[9];
            bool allFinite = true;
            for (int k = 0; k < 9; k++)
            {
                vals[k] = BitConverter.ToDouble(d, planeOff + k * 8);
                if (!double.IsFinite(vals[k])) { allFinite = false; break; }
            }
            if (!allFinite) continue;

            // xvec = vals[3..5], yvec = vals[6..8] - check unit-length (tolerance 0.01)
            double xLen = Math.Sqrt(vals[3] * vals[3] + vals[4] * vals[4] + vals[5] * vals[5]);
            double yLen = Math.Sqrt(vals[6] * vals[6] + vals[7] * vals[7] + vals[8] * vals[8]);
            if (Math.Abs(xLen - 1.0) > 0.01 || Math.Abs(yLen - 1.0) > 0.01) continue;

            double dot = vals[3] * vals[6] + vals[4] * vals[7] + vals[5] * vals[8];
            if (Math.Abs(dot) > 0.01) continue;

            var face = new GRepFace
            {
                Envelope = new[] { minU, minV, maxU, maxV },
                Orient = orientByte == 1,
                Origin = new[] { vals[0], vals[1], vals[2] },
                XVector = new[] { vals[3], vals[4], vals[5] },
                YVector = new[] { vals[6], vals[7], vals[8] },
            };
            result.Faces.Add(face);

            i = planeOff + 72 - 1;
        }

        // Scan for inner loop envelopes by finding EdgeLoop GInfoBlob markers.
        if (result.Faces.Count > 0)
        {
            for (int i = start; i + 16 <= end; i++)
            {
                // Check for EdgeLoop GInfoBlob pattern:
                // [Tag=-1:4][Cmd=0:4][StyleId=-1:4][Flags=0x00080004:4] = 16 bytes
                if (BitConverter.ToInt32(d, i) != -1) continue;
                if (BitConverter.ToInt32(d, i + 4) != 0) continue;
                if (BitConverter.ToInt32(d, i + 8) != -1) continue;
                if (BitConverter.ToUInt32(d, i + 12) != 0x00080004) continue;
                // Found EdgeLoop header at offset i. Scan forward for envelope. After header:
                // [NextLoop:4(null)][Face:4][Next:4][Prev:4] = 16 bytes, then envelope at +32.
                // Allow wider scan for non-null NextLoop chains.
                for (int j = i + 16; j + 33 <= end && j < i + 16 + 120; j++)
                {
                    double eMinU = BitConverter.ToDouble(d, j);
                    double eMinV = BitConverter.ToDouble(d, j + 8);
                    double eMaxU = BitConverter.ToDouble(d, j + 16);
                    double eMaxV = BitConverter.ToDouble(d, j + 24);
                    byte openByte = d[j + 32];

                    if (openByte > 1) continue;
                    if (!double.IsFinite(eMinU) || !double.IsFinite(eMinV) ||
                        !double.IsFinite(eMaxU) || !double.IsFinite(eMaxV)) continue;
                    if (eMinU >= eMaxU || eMinV >= eMaxV) continue;
                    double innerArea = (eMaxU - eMinU) * (eMaxV - eMinV);
                    if (innerArea < 1e-6) continue;

                    bool isFaceEnvelope = false;
                    foreach (var f in result.Faces)
                    {
                        if (f.Envelope.Length == 4 &&
                            Math.Abs(f.Envelope[0] - eMinU) < 1e-9 && Math.Abs(f.Envelope[1] - eMinV) < 1e-9 &&
                            Math.Abs(f.Envelope[2] - eMaxU) < 1e-9 && Math.Abs(f.Envelope[3] - eMaxV) < 1e-9)
                        { isFaceEnvelope = true; break; }
                    }
                    if (isFaceEnvelope) { break; }

                    GRepFace? bestFace = null;
                    double bestArea = double.MaxValue;
                    foreach (var f in result.Faces)
                    {
                        if (f.Envelope.Length != 4) continue;
                        double fMinU = f.Envelope[0], fMinV = f.Envelope[1];
                        double fMaxU = f.Envelope[2], fMaxV = f.Envelope[3];
                        if (eMinU >= fMinU - 0.01 && eMinV >= fMinV - 0.01 &&
                            eMaxU <= fMaxU + 0.01 && eMaxV <= fMaxV + 0.01)
                        {
                            double fArea = (fMaxU - fMinU) * (fMaxV - fMinV);
                            if (innerArea < fArea * 0.95 && fArea < bestArea)
                            {
                                bestFace = f;
                                bestArea = fArea;
                            }
                        }
                    }

                    if (bestFace != null)
                    {
                        bool dup = bestFace.InnerLoops.Any(il =>
                            Math.Abs(il[0] - eMinU) < 1e-9 && Math.Abs(il[1] - eMinV) < 1e-9 &&
                            Math.Abs(il[2] - eMaxU) < 1e-9 && Math.Abs(il[3] - eMaxV) < 1e-9);
                        if (!dup)
                            bestFace.InnerLoops.Add(new[] { eMinU, eMinV, eMaxU, eMaxV });
                    }
                    break;
                }
            }
        }

        // TopographySurface GReps do not store a BRep face/edge graph. They store the source
        // contour polylines as arrays of XYZ points under GInfo blobs with flags
        // 0x00088004/0x0008C004.
        if (result.Faces.Count == 0)
            RecoverContourSurface(d, start, end, result);

        // Scan for world coordinate system: [origin:3][xdir:3][ydir:3] = 9 doubles = 72 bytes.
        // Detected when xdir is unit-length and non-axis-aligned (both X and Y > 0.05), and ydir =
        // (0,0,1) (vertical wall).
        for (int i = start; i + 72 <= end; i += 2) // 2-byte alignment (Revit data isn't always 4-byte aligned)
        {
            double ox = BitConverter.ToDouble(d, i);
            double oy = BitConverter.ToDouble(d, i + 8);
            double oz = BitConverter.ToDouble(d, i + 16);
            double xx = BitConverter.ToDouble(d, i + 24);
            double xy = BitConverter.ToDouble(d, i + 32);
            double xz = BitConverter.ToDouble(d, i + 40);
            double yx = BitConverter.ToDouble(d, i + 48);
            double yy = BitConverter.ToDouble(d, i + 56);
            double yz = BitConverter.ToDouble(d, i + 64);

            if (!double.IsFinite(ox) || !double.IsFinite(oy) || !double.IsFinite(oz) ||
                !double.IsFinite(xx) || !double.IsFinite(xy) || !double.IsFinite(xz) ||
                !double.IsFinite(yx) || !double.IsFinite(yy) || !double.IsFinite(yz))
                continue;

            // ydir must be (0, 0, 1) - vertical wall
            if (Math.Abs(yx) > 0.001 || Math.Abs(yy) > 0.001 || Math.Abs(yz - 1.0) > 0.001)
                continue;

            // xdir must be unit-length and horizontal (z ~ 0)
            double xLen = xx * xx + xy * xy + xz * xz;
            if (Math.Abs(xLen - 1.0) > 0.01 || Math.Abs(xz) > 0.01)
                continue;

            // xdir must be non-axis-aligned: both components must be significant. Since
            // xx^2+xy^2~1, if one is near 0 the other must be near +/-1 (axis-aligned).
            if (Math.Abs(xx) < 0.02 || Math.Abs(xy) < 0.02)
                continue;

            // Origin must have reasonable values (< 10000 feet)
            if (Math.Abs(ox) > 10000 || Math.Abs(oy) > 10000 || Math.Abs(oz) > 10000)
                continue;

            // Store as 3x4 transform: [xdir, origin_x, perpdir, origin_y, 0,0,1, origin_z] perpdir
            // = cross(ydir=(0,0,1), xdir) = (-xy, xx, 0)
            result.Transform = [xx, xy, xz, ox, -xy, xx, 0, oy, 0, 0, 1, oz];
            break;
        }

        return result;
    }

    /// <summary>
    /// Recover a triangulated terrain from the constant-elevation contour point arrays used by
    /// TopographySurface. The signature is deliberately strict so this fallback cannot turn an
    /// arbitrary empty GRep into a terrain mesh.
    /// </summary>
    private static void RecoverContourSurface(byte[] d, int start, int end, GRepData result)
    {
        var points = new List<double[]>();
        for (int i = start; i + 8 <= end; i++)
        {
            uint flags = BitConverter.ToUInt32(d, i);
            if (flags is not (0x00088004 or 0x0008C004)) continue;

            uint count = BitConverter.ToUInt32(d, i + 4);
            if (count < 3 || count > 4096 || i + 8L + count * 24L > end) continue;

            var line = new List<double[]>((int)count);
            bool valid = true;
            for (int p = 0; p < count; p++)
            {
                int off = i + 8 + p * 24;
                double x = BitConverter.ToDouble(d, off);
                double y = BitConverter.ToDouble(d, off + 8);
                double z = BitConverter.ToDouble(d, off + 16);
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)
                    || Math.Abs(x) > 100_000 || Math.Abs(y) > 100_000 || Math.Abs(z) > 100_000)
                { valid = false; break; }
                line.Add([x, y, z]);
            }
            if (!valid) continue;

            // Every real topographic source array is a contour: all its points have one elevation.
            // This rejects node tables and coordinate-system arrays which can happen to be preceded
            // by the same GInfo flags.
            double z0 = line[0][2];
            if (line.Any(p => Math.Abs(p[2] - z0) > 1e-5)) continue;
            if (line.Max(p => p[0]) - line.Min(p => p[0]) < 1e-5
                && line.Max(p => p[1]) - line.Min(p => p[1]) < 1e-5) continue;

            points.AddRange(line);
            i += 7 + (int)count * 24;
        }

        if (points.Count < 40) return;
        if (points.Select(p => Math.Round(p[2], 5)).Distinct().Count() < 3) return;

        // Terrain is a height field. Collapse repeated XY contour junctions, preferring the highest
        // point when corrupt/redundant records disagree at the same plan point.
        var unique = new Dictionary<(long X, long Y), double[]>();
        foreach (var p in points)
        {
            var key = ((long)Math.Round(p[0] * 1e6), (long)Math.Round(p[1] * 1e6));
            if (!unique.TryGetValue(key, out var old) || p[2] > old[2]) unique[key] = p;
        }
        points = unique.Values.ToList();
        if (points.Count < 30) return;

        // The contour records omit the extrema between contour intervals, while the GRep bbox
        // carries the exact surface elevation range.
        if (result.BoundingBox is { Length: >= 6 } bb)
        {
            double srcMin = points.Min(p => p[2]), srcMax = points.Max(p => p[2]);
            if (srcMax - srcMin > 1e-6 && bb[5] - bb[2] > 1e-6)
                foreach (var p in points)
                    p[2] = bb[2] + (p[2] - srcMin) / (srcMax - srcMin) * (bb[5] - bb[2]);
        }

        double minX = points.Min(p => p[0]), maxX = points.Max(p => p[0]);
        double minY = points.Min(p => p[1]), maxY = points.Max(p => p[1]);
        if (maxX - minX < 1 || maxY - minY < 1) return;

        foreach (var (a, b, c) in Delaunay(points))
        {
            var pa = points[a]; var pb = points[b]; var pc = points[c];
            double ux = pb[0] - pa[0], uy = pb[1] - pa[1], uz = pb[2] - pa[2];
            double vx = pc[0] - pa[0], vy = pc[1] - pa[1], vz = pc[2] - pa[2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-10) continue;
            if (nz < 0) { (pb, pc) = (pc, pb); nx = -nx; ny = -ny; nz = -nz; }
            result.Faces.Add(new GRepFace
            {
                Origin = pa,
                XVector = [nx / nl, ny / nl, nz / nl],
                Vertices = [pa, pb, pc],
            });
        }
        if (result.Faces.Count >= 3) result.IsOpenSurface = true;
    }

    /// <summary>Bowyer-Watson Delaunay triangulation in the XY plane.</summary>
    private static List<(int A, int B, int C)> Delaunay(List<double[]> source)
    {
        int n = source.Count;
        var p = source.Select(v => (X: v[0], Y: v[1])).ToList();
        double minX = p.Min(v => v.X), maxX = p.Max(v => v.X);
        double minY = p.Min(v => v.Y), maxY = p.Max(v => v.Y);
        double span = Math.Max(maxX - minX, maxY - minY);
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        p.Add((cx - 20 * span, cy - span));
        p.Add((cx, cy + 20 * span));
        p.Add((cx + 20 * span, cy - span));

        var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };

        static bool InCircle((double X, double Y) q, (double X, double Y) a,
            (double X, double Y) b, (double X, double Y) c)
        {
            double ax = a.X - q.X, ay = a.Y - q.Y;
            double bx = b.X - q.X, by = b.Y - q.Y;
            double cx = c.X - q.X, cy = c.Y - q.Y;
            double det = (ax * ax + ay * ay) * (bx * cy - by * cx)
                       - (bx * bx + by * by) * (ax * cy - ay * cx)
                       + (cx * cx + cy * cy) * (ax * by - ay * bx);
            double orient = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return orient > 0 ? det > 1e-9 : det < -1e-9;
        }

        for (int pi = 0; pi < n; pi++)
        {
            var bad = triangles.Where(t => InCircle(p[pi], p[t.A], p[t.B], p[t.C])).ToList();
            var edges = new Dictionary<(int A, int B), int>();
            foreach (var t in bad)
                foreach (var edge in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var key = edge.Item1 < edge.Item2 ? edge : (edge.Item2, edge.Item1);
                    edges.TryGetValue(key, out int count); edges[key] = count + 1;
                }
            foreach (var t in bad) triangles.Remove(t);
            foreach (var edge in edges.Where(e => e.Value == 1).Select(e => e.Key))
                triangles.Add((edge.A, edge.B, pi));
        }

        return triangles.Where(t => t.A < n && t.B < n && t.C < n).ToList();
    }

    /// <summary>Scan GRep block for node type markers (diagnostic).</summary>
    public static Dictionary<ushort, int> ScanMarkers(byte[] d, int start, int end)
    {
        var counts = new Dictionary<ushort, int>();
        for (int i = start; i + 2 <= end; i += 2)
        {
            ushort m = BitConverter.ToUInt16(d, i);
            if (m is 0x082C or 0x06C8 or 0x07B6 or 0x07DF or 0x07D7 or 0x07D9 or 0x07CC or 0x07E0
                or 0x0826 or 0x0827 or 0x082A or 0x0830 or 0x07DA)
            {
                counts.TryGetValue(m, out int c);
                counts[m] = c + 1;
            }
        }
        return counts;
    }
}
