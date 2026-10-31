using System.IO.Compression;
using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcExporterAnchoredSecondaryJambsTests
{
    private sealed class Fixture
    {
        public required Element Cutter;
        public required Element Host;
        public required Element Secondary;
        public required List<Element> Hosts;
        public IfcExporter.HostedOpeningGeometry Common;
        public bool Expand(out IfcExporter.HostedOpeningGeometry result) =>
            IfcExporter.TryExpandCommonOpeningFromAnchoredSecondaryJambs(Cutter,Host,Hosts,Common,out result);
    }

    [Fact]
    public void AnchoredJambs_RestoreOnlySourceDepthAndNormalPlacement()
    {
        var f=Create(); Assert.True(f.Expand(out var result));
        Assert.Equal(1200,result.DepthFt*304.8,6);
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
    public void AnchoredJambs_AcceptReversedCycles()
    {
        var f=Create();
        foreach(var face in f.Hosts.SelectMany(host=>host.Geometry!.Faces))
        {
            face.OuterVertices?.Reverse(); face.AlternativeOuterVertices?.Reverse();
        }
        Assert.True(f.Expand(out _));
    }

    [Fact]
    public void AnchoredJambs_RejectEveryIndividualWitnessCoordinateMutation()
    {
        int count=0;
        foreach(bool secondary in new[] {false,true})
        foreach(int index in secondary?new[] {12,13}:new[] {29})
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
        Assert.Equal(81,count);
    }

    [Fact]
    public void AnchoredJambs_AcceptOppositeCommonAxisAndPairOrder()
    {
        var f=Create();
        f.Common=f.Common with {AxisX=-f.Common.AxisX,AxisY=-f.Common.AxisY};
        var faces=f.Secondary.Geometry!.Faces;
        (faces[12],faces[13])=(faces[13],faces[12]);
        Assert.True(f.Expand(out var expanded));
        Assert.Equal(1200,expanded.DepthFt*304.8,6);
    }

    [Fact]
    public void AnchoredJambs_AcceptHostOrderAndEveryCycleStart()
    {
        for(int ordinary=0;ordinary<4;ordinary++)
        for(int alternative=0;alternative<5;alternative++)
        {
            var f=Create(); f.Hosts.Reverse();
            foreach(var (host,indices) in new[] {(f.Host,new[] {29}),(f.Secondary,new[] {12,13})})
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
    [InlineData("primary-missing-roster")]
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
    public void AnchoredJambs_RejectIncompleteOrAmbiguousAssociation(string mutation)
    {
        var f=Create();
        switch(mutation)
        {
            case "primary-empty-roster": f.Host.CuttingElementIds=[]; break;
            case "primary-missing-roster": f.Host.CuttingElementIds=null; break;
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
            case "secondary-ownership": f.Secondary.Geometry!.BrepSolids![0].FaceIndices.Remove(12); break;
            case "secondary-cap-missing": f.Secondary.Geometry!.Faces[13].AlternativeOuterVertices=null; break;
            case "secondary-source-order": f.Secondary.Geometry!.Faces[12].OuterVertices!.Reverse(1,2); break;
            case "secondary-alternative-order": f.Secondary.Geometry!.Faces[12].AlternativeOuterVertices!.Reverse(1,3); break;
            case "secondary-inner-ring": f.Secondary.Geometry!.Faces[12].InnerVertices=[f.Secondary.Geometry.Faces[12].OuterVertices!]; break;
            case "secondary-width": f.Secondary.Parameters.Single(p=>p.Id==-1001000).Value=1.0; break;
            case "duplicate-host": f.Hosts.Add(f.Secondary); break;
            case "missing-primary": f.Hosts.Remove(f.Host); break;
            case "missing-secondary": f.Hosts.Remove(f.Secondary); break;
            case "secondary-nonwall": f.Secondary.CategoryId=-2000032; break;
            case "competing-secondary":
                f.Host.References!.Add(99204);
                // Deliberately no geometry: association ambiguity must fail before geometry selection.
                f.Hosts.Add(new Element {Id=99204,CategoryId=-2000011,
                    CuttingElementIds=null,Location=f.Secondary.Location,References=[f.Host.Id,f.Cutter.Id]}); break;
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
        var oldCuts=f.Secondary.CuttingElementIds;f.Secondary.CuttingElementIds=[f.Cutter.Id];
        f.Cutter.SerializedHostId=f.Secondary.Id;
        f.Cutter.References!.Add(f.Secondary.Id);
        try { Assert.True(IfcExporter.TryExpandCommonOpeningFromPairedSourceJambs(f.Cutter,f.Secondary,f.Common,out _)); }
        finally { f.Cutter.SerializedHostId=old;f.Cutter.References.Remove(f.Secondary.Id);f.Secondary.CuttingElementIds=oldCuts; }
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(-0.05)]
    public void AnchoredJambs_RejectGapOrOverlapEvenWhenBothPairsAgree(double delta)
    {
        var f=Create();
        foreach(int index in new[] {12,13})
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
    public void AnchoredJambs_RejectDifferentIndependentlyValidCutIntervals(double offset,double growth)
    {
        var f=Create(); ChangeExpandedInterval(f,f.Secondary,[12,13],offset,growth);
        AssertSecondaryPairIsIndependentlyValid(f);
        Assert.False(f.Expand(out _));
    }

    [Theory]
    [InlineData(0.25,0.0)]
    [InlineData(0.0,0.1)]
    public void AnchoredJambs_UseAgreedAuthoredInterval(double offset,double growth)
    {
        var f=Create();
        ChangeExpandedInterval(f,f.Host,[29],offset,growth);
        ChangeExpandedInterval(f,f.Secondary,[12,13],offset,growth);
        Assert.True(f.Expand(out var result));
        Assert.Equal(1200+2*growth*304.8,result.DepthFt*304.8,6);
        Assert.Equal(f.Cutter.Transform!.Origin[0]-offset*f.Common.AxisY,result.CentreXFt,9);
        Assert.Equal(f.Cutter.Transform.Origin[1]+offset*f.Common.AxisX,result.CentreYFt,9);
    }

    private static void SetAlong(Fixture f,double[] p,double along)
    {
        var origin=f.Cutter.Transform!.Origin;
        double current=(p[0]-origin[0])*f.Common.AxisX+(p[1]-origin[1])*f.Common.AxisY;
        p[0]+=(along-current)*f.Common.AxisX;p[1]+=(along-current)*f.Common.AxisY;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnchoredJambs_AcceptExactPathCoverageInEitherDirection(bool reverse)
    {
        var f=Create(); var line=f.Secondary.Location!;
        SetAlong(f,line.StartPoint,f.Common.WidthFt/2);
        SetAlong(f,line.EndPoint,-f.Common.WidthFt/2);
        if(reverse) (line.StartPoint,line.EndPoint)=(line.EndPoint,line.StartPoint);
        Assert.True(f.Expand(out _));
    }

    [Theory]
    [InlineData("short-left")]
    [InlineData("short-right")]
    [InlineData("off-axis")]
    [InlineData("non-horizontal")]
    [InlineData("missing")]
    [InlineData("nan")]
    [InlineData("overflow")]
    public void AnchoredJambs_RejectUnvalidatedSecondaryPath(string mutation)
    {
        var f=Create(); var line=f.Secondary.Location!;
        switch(mutation)
        {
            case "short-left": SetAlong(f,line.EndPoint,-f.Common.WidthFt/2+0.0001); break;
            case "short-right": SetAlong(f,line.StartPoint,f.Common.WidthFt/2-0.0001); break;
            case "off-axis": MoveNormal(f,line.StartPoint,0.1); break;
            case "non-horizontal": line.EndPoint[2]+=0.1; break;
            case "missing": f.Secondary.Location=null; break;
            case "nan": line.StartPoint[0]=double.NaN; break;
            case "overflow": line.StartPoint[0]=double.MaxValue; break;
        }
        Assert.False(f.Expand(out _));
    }

    [Fact]
    public void AnchoredJambs_DoNotSelectReferencedSegmentOutsideExactOpeningSpan()
    {
        var f=Create(); var line=f.Secondary.Location!;
        var distant=new LocationCurve {CurveType="Line",StartPoint=line.StartPoint.ToArray(),EndPoint=line.EndPoint.ToArray()};
        SetAlong(f,distant.StartPoint,-2*f.Common.WidthFt);
        SetAlong(f,distant.EndPoint,-3*f.Common.WidthFt);
        f.Host.References!.Add(99204);
        f.Hosts.Add(new Element {Id=99204,CategoryId=-2000011,Location=distant,
            CuttingElementIds=null,References=[f.Host.Id,f.Cutter.Id]});
        Assert.True(f.Expand(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnchoredJambs_RejectCompetingPrimaryCapClaim(bool upper)
    {
        var f=Create(); var box=f.Common;
        double[] P(double x,double y)=>[box.CentreXFt+x*box.AxisX-y*box.AxisY,
            box.CentreYFt+x*box.AxisY+y*box.AxisX,box.BaseZFt+(upper?box.HeightFt:0)];
        List<double[]> ring=[P(-box.WidthFt/2,-box.DepthFt/2),P(box.WidthFt/2,-box.DepthFt/2),
            P(box.WidthFt/2,box.DepthFt/2),P(-box.WidthFt/2,box.DepthFt/2)];
        f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(f.Host.Geometry.Faces.Count);
        f.Host.Geometry.Faces.Add(new GeoFace {OuterVertices=ring,AlternativeOuterVertices=ring.Select(p=>p.ToArray()).ToList()});
        Assert.False(f.Expand(out _));
    }

    [Theory]
    [InlineData(0.37)]
    [InlineData(1.5707963267948966)]
    public void AnchoredJambs_AcceptRigidWorldTransforms(double angle)
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
        Assert.Equal(1200,expanded.DepthFt*304.8,6);
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
    public void AnchoredJambs_FailClosed(string mutation)
    {
        var f=Create();
        switch(mutation)
        {
            case "host": f.Cutter.SerializedHostId=99299; break;
            case "nested": f.Cutter.SuperInstanceId=99299; break;
            case "decoded-missing": f.Host.CuttingElementIds=null; break;
            case "decoded-duplicate": f.Host.CuttingElementIds!.Add(f.Cutter.Id); break;
            case "reference": f.Host.References!.Remove(f.Cutter.Id); break;
            case "reverse-reference": f.Cutter.References!.Remove(f.Host.Id); break;
            case "ownership": f.Host.Geometry!.BrepSolids![0].FaceIndices.Remove(29); break;
            case "ownership-duplicate": f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(29); break;
            case "lower-missing": f.Host.Geometry!.Faces[29].AlternativeOuterVertices=null; break;
            case "upper-missing": f.Host.Geometry!.Faces[29].AlternativeOuterVertices=null; break;
            case "alternative-order": f.Host.Geometry!.Faces[29].AlternativeOuterVertices!.Reverse(1,3); break;
            case "source-order": f.Host.Geometry!.Faces[29].OuterVertices!.Reverse(1,2); break;
            case "source-inner": f.Host.Geometry!.Faces[29].InnerVertices=[f.Host.Geometry.Faces[29].OuterVertices!]; break;
            case "duplicate-cap":
                f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(f.Host.Geometry.Faces.Count);
                f.Host.Geometry.Faces.Add(f.Host.Geometry.Faces[29]); break;
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
                foreach(var p in f.Host.Geometry!.Faces[29].OuterVertices!) p[2]=2e14;
                break;
            case "alternative-overflow": f.Host.Geometry!.Faces[29].AlternativeOuterVertices![0][0]=double.MaxValue; break;
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
        int Map(int id)=>id==2054545?99201:id==4510817?99202:id==2054856?99203:id;
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
        var raw=doc.RootElement.EnumerateArray().Single(e=>e.GetProperty("Id").GetInt32()==4510817).GetProperty("Common");
        double Value(string name)=>raw.GetProperty(name).GetDouble();
        return new Fixture {Host=host,Secondary=elements[99203],Hosts=[host,elements[99203]],Cutter=cutter,
            Common=new IfcExporter.HostedOpeningGeometry(Value("CentreXFt"),Value("CentreYFt"),
                Value("AxisX"),Value("AxisY"),Value("WidthFt"),Value("DepthFt"),Value("BaseZFt"),Value("HeightFt"),
                ExactDepth:true,IsExactAuthoredMultiHostEnvelope:true)};
    }
}
