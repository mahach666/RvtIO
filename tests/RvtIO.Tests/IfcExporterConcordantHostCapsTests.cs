using System.IO.Compression;
using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcExporterConcordantHostCapsTests
{
    private sealed class Fixture
    {
        public required Element Cutter;
        public required Element Host;
        public required Element Secondary;
        public required List<Element> Hosts;
        public IfcExporter.HostedOpeningGeometry Common;
        public bool Expand(out IfcExporter.HostedOpeningGeometry result) =>
            IfcExporter.TryExpandCommonOpeningFromConcordantHostCaps(Cutter,Host,Hosts,Common,out result);
    }

    [Fact]
    public void ConcordantCaps_RestoreOnlySourceDepthAndNormalPlacement()
    {
        var f=Create(); Assert.True(f.Expand(out var result));
        Assert.Equal(1155,result.DepthFt*304.8,6);
        Assert.Equal(f.Common.WidthFt,result.WidthFt);
        Assert.Equal(f.Common.HeightFt,result.HeightFt);
        Assert.Equal(f.Common.BaseZFt,result.BaseZFt);
        Assert.Equal(f.Common.AxisX,result.AxisX);
        Assert.Equal(f.Common.AxisY,result.AxisY);
        Assert.True(result.IsExactAuthoredMultiHostEnvelope);
        Assert.Equal(f.Cutter.Transform!.Origin[0],result.CentreXFt,9);
        Assert.Equal(f.Cutter.Transform.Origin[1],result.CentreYFt,9);
    }

    [Fact]
    public void ConcordantCaps_AcceptReversedCycles()
    {
        var f=Create();
        foreach(var face in f.Hosts.SelectMany(host=>host.Geometry!.Faces))
        {
            face.OuterVertices?.Reverse(); face.AlternativeOuterVertices?.Reverse();
        }
        Assert.True(f.Expand(out _));
    }

    [Fact]
    public void ConcordantCaps_RejectEveryIndividualWitnessCoordinateMutation()
    {
        int count=0;
        foreach(bool secondary in new[] {false,true})
        foreach(int index in secondary?new[] {23,26}:new[] {11,14})
        foreach(bool alternative in new[] {false,true})
        for(int point=0;point<(alternative?5:4);point++)
        for(int coordinate=0;coordinate<3;coordinate++)
        {
            var f=Create(); var face=(secondary?f.Secondary:f.Host).Geometry!.Faces[index];
            var ring=alternative?face.AlternativeOuterVertices!:face.OuterVertices!;
            ring[point][coordinate]+=0.1;
            Assert.False(f.Expand(out _),$"face {index}, alternative {alternative}, point {point}, coordinate {coordinate}");
            count++;
        }
        Assert.Equal(108,count);
    }

    [Fact]
    public void ConcordantCaps_AcceptOppositeCommonAxisAndCapOrder()
    {
        var f=Create();
        f.Common=f.Common with {AxisX=-f.Common.AxisX,AxisY=-f.Common.AxisY};
        var faces=f.Host.Geometry!.Faces;
        (faces[11],faces[14])=(faces[14],faces[11]);
        Assert.True(f.Expand(out var expanded));
        Assert.Equal(1155,expanded.DepthFt*304.8,6);
    }

    [Fact]
    public void ConcordantCaps_AcceptHostOrderAndEveryCycleStart()
    {
        for(int ordinary=0;ordinary<4;ordinary++)
        for(int alternative=0;alternative<5;alternative++)
        {
            var f=Create(); f.Hosts.Reverse();
            foreach(var (host,indices) in new[] {(f.Host,new[] {11,14}),(f.Secondary,new[] {23,26})})
            foreach(int index in indices)
            {
                var face=host.Geometry!.Faces[index];
                face.OuterVertices=face.OuterVertices!.Skip(ordinary).Concat(face.OuterVertices.Take(ordinary)).ToList();
                face.AlternativeOuterVertices=face.AlternativeOuterVertices!.Skip(alternative)
                    .Concat(face.AlternativeOuterVertices.Take(alternative)).ToList();
            }
            Assert.True(f.Expand(out _));
        }
    }

    [Theory]
    [InlineData("primary-empty-roster")]
    [InlineData("primary-present-roster")]
    [InlineData("secondary-empty-roster")]
    [InlineData("secondary-other-roster")]
    [InlineData("primary-to-secondary-missing")]
    [InlineData("primary-to-secondary-duplicate")]
    [InlineData("secondary-to-primary-missing")]
    [InlineData("secondary-to-primary-duplicate")]
    [InlineData("secondary-to-cutter-missing")]
    [InlineData("secondary-to-cutter-duplicate")]
    [InlineData("primary-to-cutter-duplicate")]
    [InlineData("cutter-to-primary-duplicate")]
    [InlineData("secondary-ownership")]
    [InlineData("secondary-cap-missing")]
    [InlineData("secondary-source-order")]
    [InlineData("secondary-alternative-order")]
    [InlineData("secondary-inner-ring")]
    [InlineData("secondary-width")]
    [InlineData("duplicate-host")]
    [InlineData("missing-primary")]
    [InlineData("missing-secondary")]
    [InlineData("secondary-nonwall")]
    [InlineData("competing-secondary")]
    public void ConcordantCaps_RejectIncompleteOrAmbiguousAssociation(string mutation)
    {
        var f=Create();
        switch(mutation)
        {
            case "primary-empty-roster": f.Host.CuttingElementIds=[]; break;
            case "primary-present-roster": f.Host.CuttingElementIds=[f.Cutter.Id]; break;
            case "secondary-empty-roster": f.Secondary.CuttingElementIds=[]; break;
            case "secondary-other-roster": f.Secondary.CuttingElementIds=[99299]; break;
            case "primary-to-secondary-missing": f.Host.References!.Remove(f.Secondary.Id); break;
            case "primary-to-secondary-duplicate": f.Host.References!.Add(f.Secondary.Id); break;
            case "secondary-to-primary-missing": f.Secondary.References!.Remove(f.Host.Id); break;
            case "secondary-to-primary-duplicate": f.Secondary.References!.Add(f.Host.Id); break;
            case "secondary-to-cutter-missing": f.Secondary.References!.Remove(f.Cutter.Id); break;
            case "secondary-to-cutter-duplicate": f.Secondary.References!.Add(f.Cutter.Id); break;
            case "primary-to-cutter-duplicate": f.Host.References!.Add(f.Cutter.Id); break;
            case "cutter-to-primary-duplicate": f.Cutter.References!.Add(f.Host.Id); break;
            case "secondary-ownership": f.Secondary.Geometry!.BrepSolids![0].FaceIndices.Remove(23); break;
            case "secondary-cap-missing": f.Secondary.Geometry!.Faces[26].AlternativeOuterVertices=null; break;
            case "secondary-source-order": f.Secondary.Geometry!.Faces[23].OuterVertices!.Reverse(1,2); break;
            case "secondary-alternative-order": f.Secondary.Geometry!.Faces[23].AlternativeOuterVertices!.Reverse(1,3); break;
            case "secondary-inner-ring": f.Secondary.Geometry!.Faces[23].InnerVertices=[f.Secondary.Geometry.Faces[23].OuterVertices!]; break;
            case "secondary-width": f.Secondary.Parameters.Single(p=>p.Id==-1001000).Value=1.0; break;
            case "duplicate-host": f.Hosts.Add(f.Secondary); break;
            case "missing-primary": f.Hosts.Remove(f.Host); break;
            case "missing-secondary": f.Hosts.Remove(f.Secondary); break;
            case "secondary-nonwall": f.Secondary.CategoryId=-2000032; break;
            case "competing-secondary":
                f.Host.References!.Add(99204);
                // Deliberately no geometry: association ambiguity must fail before geometry selection.
                f.Hosts.Add(new Element {Id=99204,CategoryId=-2000011,
                    CuttingElementIds=[f.Cutter.Id],References=[f.Host.Id,f.Cutter.Id]}); break;
        }
        Assert.False(f.Expand(out _));
    }

    private static long NormalKey(Fixture f,double[] p) => checked((long)Math.Round(
        (-(p[0]-f.Cutter.Transform!.Origin[0])*f.Common.AxisY
         +(p[1]-f.Cutter.Transform.Origin[1])*f.Common.AxisX)*30480,MidpointRounding.ToEven));

    private static void MoveNormal(Fixture f,double[] p,double delta)
    {
        p[0]-=delta*f.Common.AxisY;p[1]+=delta*f.Common.AxisX;
    }

    private static void ChangeExpandedInterval(Fixture f,Element host,int[] indices,double offset,double growth)
    {
        foreach(int index in indices)
        {
            var ring=host.Geometry!.Faces[index].AlternativeOuterVertices!;
            long low=ring.Min(p=>NormalKey(f,p)),high=ring.Max(p=>NormalKey(f,p));
            foreach(var p in ring)
            {
                long key=NormalKey(f,p);
                if(key==low) MoveNormal(f,p,offset-growth);
                else if(key==high) MoveNormal(f,p,offset+growth);
            }
        }
    }

    private static void AssertSecondaryPairIsIndependentlyValid(Fixture f)
    {
        int? old=f.Cutter.SerializedHostId;
        f.Cutter.SerializedHostId=f.Secondary.Id;
        f.Cutter.References!.Add(f.Secondary.Id);
        try { Assert.True(IfcExporter.TryExpandCommonOpeningFromPairedSourceCaps(f.Cutter,f.Secondary,f.Common,out _)); }
        finally { f.Cutter.SerializedHostId=old;f.Cutter.References.Remove(f.Secondary.Id); }
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(-0.05)]
    public void ConcordantCaps_RejectGapOrOverlapEvenWhenBothPairsAgree(double delta)
    {
        var f=Create();
        foreach(int index in new[] {23,26})
        {
            var face=f.Secondary.Geometry!.Faces[index];
            var ordinaryKeys=face.OuterVertices!.Select(p=>NormalKey(f,p)).ToHashSet();
            foreach(var p in face.AlternativeOuterVertices!)
                if(ordinaryKeys.Contains(NormalKey(f,p))) MoveNormal(f,p,delta);
            foreach(var p in face.OuterVertices) MoveNormal(f,p,delta);
        }
        MoveNormal(f,f.Secondary.Location!.StartPoint,delta);
        MoveNormal(f,f.Secondary.Location.EndPoint,delta);
        AssertSecondaryPairIsIndependentlyValid(f);
        Assert.False(f.Expand(out _));
    }

    [Theory]
    [InlineData(0.25,0.0)]
    [InlineData(0.0,0.1)]
    public void ConcordantCaps_RejectDifferentIndependentlyValidCutIntervals(double offset,double growth)
    {
        var f=Create(); ChangeExpandedInterval(f,f.Secondary,[23,26],offset,growth);
        AssertSecondaryPairIsIndependentlyValid(f);
        Assert.False(f.Expand(out _));
    }

    [Theory]
    [InlineData(0.25,0.0)]
    [InlineData(0.0,0.1)]
    public void ConcordantCaps_UseAgreedAuthoredInterval(double offset,double growth)
    {
        var f=Create();
        ChangeExpandedInterval(f,f.Host,[11,14],offset,growth);
        ChangeExpandedInterval(f,f.Secondary,[23,26],offset,growth);
        Assert.True(f.Expand(out var result));
        Assert.Equal(1155+2*growth*304.8,result.DepthFt*304.8,6);
        Assert.Equal(f.Cutter.Transform!.Origin[0]-offset*f.Common.AxisY,result.CentreXFt,9);
        Assert.Equal(f.Cutter.Transform.Origin[1]+offset*f.Common.AxisX,result.CentreYFt,9);
    }

    [Theory]
    [InlineData(0.37)]
    [InlineData(1.5707963267948966)]
    public void ConcordantCaps_AcceptRigidWorldTransforms(double angle)
    {
        var f=Create(); double c=Math.Cos(angle),s=Math.Sin(angle);
        void Rotate(double[] p)
        {
            double x=p[0],y=p[1];p[0]=x*c-y*s;p[1]=x*s+y*c;
        }
        void Move(double[] p) {Rotate(p);p[0]+=32;p[1]-=17;p[2]+=4;}
        var points=f.Hosts.SelectMany(host=>host.Geometry!.Faces).SelectMany(face=>face.OuterVertices!
            .Concat(face.AlternativeOuterVertices??[]).Concat((face.InnerVertices??[]).SelectMany(r=>r)));
        foreach(var p in points) Move(p);
        foreach(var host in f.Hosts) { Move(host.Location!.StartPoint);Move(host.Location.EndPoint); }
        Move(f.Cutter.Transform!.Origin);
        Rotate(f.Cutter.Transform.BasisX);Rotate(f.Cutter.Transform.BasisY);Rotate(f.Cutter.Transform.BasisZ);
        var old=f.Common;
        f.Common=old with
        {
            CentreXFt=c*old.CentreXFt-s*old.CentreYFt+32,CentreYFt=s*old.CentreXFt+c*old.CentreYFt-17,
            AxisX=c*old.AxisX-s*old.AxisY,AxisY=s*old.AxisX+c*old.AxisY,BaseZFt=old.BaseZFt+4,
        };
        Assert.True(f.Expand(out var expanded));
        Assert.Equal(1155,expanded.DepthFt*304.8,6);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("nested")]
    [InlineData("decoded-missing")]
    [InlineData("decoded-duplicate")]
    [InlineData("reference")]
    [InlineData("reverse-reference")]
    [InlineData("ownership")]
    [InlineData("ownership-duplicate")]
    [InlineData("lower-missing")]
    [InlineData("upper-missing")]
    [InlineData("alternative-order")]
    [InlineData("source-order")]
    [InlineData("source-inner")]
    [InlineData("duplicate-cap")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("elevation")]
    [InlineData("along-placement")]
    [InlineData("non-unit-axis")]
    [InlineData("basis")]
    [InlineData("loop")]
    [InlineData("inexact")]
    [InlineData("not-common")]
    [InlineData("already-deeper")]
    [InlineData("nan")]
    [InlineData("wall-width")]
    [InlineData("wall-width-overflow")]
    [InlineData("relative-origin-overflow")]
    [InlineData("relative-cap-overflow")]
    [InlineData("alternative-overflow")]
    public void ConcordantCaps_FailClosed(string mutation)
    {
        var f=Create();
        switch(mutation)
        {
            case "host": f.Cutter.SerializedHostId=99299; break;
            case "nested": f.Cutter.SuperInstanceId=99299; break;
            case "decoded-missing": f.Secondary.CuttingElementIds=null; break;
            case "decoded-duplicate": f.Secondary.CuttingElementIds!.Add(f.Cutter.Id); break;
            case "reference": f.Host.References!.Remove(f.Cutter.Id); break;
            case "reverse-reference": f.Cutter.References!.Remove(f.Host.Id); break;
            case "ownership": f.Host.Geometry!.BrepSolids![0].FaceIndices.Remove(11); break;
            case "ownership-duplicate": f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(11); break;
            case "lower-missing": f.Host.Geometry!.Faces[14].AlternativeOuterVertices=null; break;
            case "upper-missing": f.Host.Geometry!.Faces[11].AlternativeOuterVertices=null; break;
            case "alternative-order": f.Host.Geometry!.Faces[11].AlternativeOuterVertices!.Reverse(1,3); break;
            case "source-order": f.Host.Geometry!.Faces[11].OuterVertices!.Reverse(1,2); break;
            case "source-inner": f.Host.Geometry!.Faces[11].InnerVertices=[f.Host.Geometry.Faces[11].OuterVertices!]; break;
            case "duplicate-cap":
                f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(f.Host.Geometry.Faces.Count);
                f.Host.Geometry.Faces.Add(f.Host.Geometry.Faces[11]); break;
            case "width": f.Common=f.Common with {WidthFt=f.Common.WidthFt+0.1}; break;
            case "height": f.Common=f.Common with {HeightFt=f.Common.HeightFt+0.1}; break;
            case "elevation": f.Common=f.Common with {BaseZFt=f.Common.BaseZFt+0.1}; break;
            case "along-placement": f.Common=f.Common with {CentreXFt=f.Common.CentreXFt+f.Common.AxisX*0.1,CentreYFt=f.Common.CentreYFt+f.Common.AxisY*0.1}; break;
            case "non-unit-axis": f.Common=f.Common with {AxisX=f.Common.AxisX*2,AxisY=f.Common.AxisY*2}; break;
            case "basis": f.Cutter.Transform!.BasisZ[2]=2; break;
            case "loop": f.Common=f.Common with {IsFamilyCutLoop=true}; break;
            case "inexact": f.Common=f.Common with {ExactDepth=false}; break;
            case "not-common": f.Common=f.Common with {IsExactAuthoredMultiHostEnvelope=false}; break;
            case "already-deeper": f.Common=f.Common with {DepthFt=10}; break;
            case "nan": f.Common=f.Common with {DepthFt=double.NaN}; break;
            case "wall-width": f.Host.Parameters.Single(p=>p.Id==-1001000).Value=1.0; break;
            case "wall-width-overflow": f.Host.Parameters.Single(p=>p.Id==-1001000).Value=double.MaxValue; break;
            case "relative-origin-overflow":
                f.Cutter.Transform!.Origin[2]=-2e14;
                f.Common=f.Common with {BaseZFt=2e14}; break;
            case "relative-cap-overflow":
                f.Cutter.Transform!.Origin[2]=-2e14;
                f.Common=f.Common with {BaseZFt=-2e14};
                foreach(var p in f.Host.Geometry!.Faces[11].OuterVertices!) p[2]=2e14;
                break;
            case "alternative-overflow": f.Host.Geometry!.Faces[11].AlternativeOuterVertices![0][0]=double.MaxValue; break;
        }
        Assert.False(f.Expand(out _));
    }

    private static Fixture Create()
    {
        const string captured="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";
        using var stream=new GZipStream(new MemoryStream(Convert.FromBase64String(captured)),CompressionMode.Decompress);
        using var doc=JsonDocument.Parse(stream);
        static double[] Point(JsonElement p)=>p.EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        static List<double[]> Ring(JsonElement p)=>p.EnumerateArray().Select(Point).ToList();
        int Map(int id)=>id==2054823?99201:id==2054824?99202:id==2054545?99203:id;
        var elements=new Dictionary<int,Element>();
        foreach(var row in doc.RootElement.EnumerateArray())
        {
            var e=new Element
            {
                Id=Map(row.GetProperty("Id").GetInt32()),CategoryId=row.GetProperty("CategoryId").GetInt32(),
                Location=JsonSerializer.Deserialize<LocationCurve>(row.GetProperty("Location")),
                Transform=JsonSerializer.Deserialize<InstanceTransform>(row.GetProperty("Transform")),
                BoundingBox=Point(row.GetProperty("BoundingBox")),
                References=row.GetProperty("References").EnumerateArray().Select(v=>Map(v.GetInt32())).ToList(),
                CuttingElementIds=row.GetProperty("CuttingElementIds").ValueKind==JsonValueKind.Array
                    ?row.GetProperty("CuttingElementIds").EnumerateArray().Select(v=>Map(v.GetInt32())).ToList():null,
                SerializedHostId=row.GetProperty("SerializedHostId").ValueKind==JsonValueKind.Number
                    ?Map(row.GetProperty("SerializedHostId").GetInt32()):null,
                Parameters=(row.GetProperty("Parameters").ValueKind==JsonValueKind.Array
                    ?row.GetProperty("Parameters").EnumerateArray().ToArray():[row.GetProperty("Parameters")]).Select(p=>new Parameter
                {Id=p.GetProperty("Id").GetInt32(),StorageType=StorageType.Double,Value=p.GetProperty("Value").GetDouble()}).ToList(),
            };
            var g=row.GetProperty("Geometry");
            if(g.ValueKind==JsonValueKind.Object)
            {
                e.Geometry=new GeometryData {BoundingBox=Point(g.GetProperty("BoundingBox")),
                    BrepSolids=JsonSerializer.Deserialize<List<GeoBrepSolid>>(g.GetProperty("Solids"))};
                foreach(var face in g.GetProperty("Faces").EnumerateArray()) e.Geometry.Faces.Add(new GeoFace
                {
                    OuterVertices=Ring(face.GetProperty("Outer")),
                    InnerVertices=face.GetProperty("Inner").ValueKind==JsonValueKind.Array?face.GetProperty("Inner").EnumerateArray().Select(Ring).ToList():null,
                    AlternativeOuterVertices=face.GetProperty("Alternative").ValueKind==JsonValueKind.Array?Ring(face.GetProperty("Alternative")):null,
                });
            }
            elements[e.Id]=e;
        }
        var host=elements[99201];var cutter=elements[99202];
        var cap=host.Geometry!.Faces[11].OuterVertices!;
        double dx=host.Location!.EndPoint[0]-host.Location.StartPoint[0],dy=host.Location.EndPoint[1]-host.Location.StartPoint[1];
        double length=Math.Sqrt(dx*dx+dy*dy);dx/=length;dy/=length;
        var along=cap.Select(p=>p[0]*dx+p[1]*dy).ToArray();
        var normal=cap.Select(p=>-p[0]*dy+p[1]*dx).ToArray();
        double x=(along.Min()+along.Max())/2,y=(normal.Min()+normal.Max())/2;
        double bottom=host.Geometry.Faces[14].OuterVertices![0][2],top=cap[0][2];
        return new Fixture {Host=host,Secondary=elements[99203],Hosts=[host,elements[99203]],Cutter=cutter,Common=new IfcExporter.HostedOpeningGeometry(
            x*dx-y*dy,x*dy+y*dx,dx,dy,along.Max()-along.Min(),normal.Max()-normal.Min(),bottom,top-bottom,
            ExactDepth:true,IsExactAuthoredMultiHostEnvelope:true)};
    }
}
