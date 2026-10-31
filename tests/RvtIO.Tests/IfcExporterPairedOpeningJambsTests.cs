using System.IO.Compression;
using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcExporterPairedOpeningJambsTests
{
    private sealed class Fixture
    {
        public required Element Cutter;
        public required Element Host;
        public IfcExporter.HostedOpeningGeometry Common;
        public bool Expand(out IfcExporter.HostedOpeningGeometry result) =>
            IfcExporter.TryExpandCommonOpeningFromPairedSourceJambs(Cutter,Host,Common,out result);
    }

    [Fact]
    public void PairedJambs_RestoreOnlySourceDepthAndNormalPlacement()
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
    public void PairedJambs_AcceptReversedCycles()
    {
        var f=Create();
        foreach(var face in f.Host.Geometry!.Faces)
        {
            face.OuterVertices?.Reverse(); face.AlternativeOuterVertices?.Reverse();
        }
        Assert.True(f.Expand(out _));
    }

    [Fact]
    public void PairedJambs_RejectEveryIndividualWitnessCoordinateMutation()
    {
        int count=0;
        foreach(int index in new[] {17,18})
        foreach(bool alternative in new[] {false,true})
        for(int point=0;point<(alternative?5:4);point++)
        for(int coordinate=0;coordinate<3;coordinate++)
        {
            var f=Create(); var face=f.Host.Geometry!.Faces[index];
            var ring=alternative?face.AlternativeOuterVertices!:face.OuterVertices!;
            ring[point][coordinate]+=0.1;
            Assert.False(f.Expand(out _),$"face {index}, alternative {alternative}, point {point}, coordinate {coordinate}");
            count++;
        }
        Assert.Equal(54,count);
    }

    [Fact]
    public void PairedJambs_AcceptOppositeCommonAxisAndJambOrder()
    {
        var f=Create();
        f.Common=f.Common with {AxisX=-f.Common.AxisX,AxisY=-f.Common.AxisY};
        var faces=f.Host.Geometry!.Faces;
        (faces[17],faces[18])=(faces[18],faces[17]);
        Assert.True(f.Expand(out var expanded));
        Assert.Equal(1200,expanded.DepthFt*304.8,6);
    }

    [Fact]
    public void PairedJambs_AcceptEveryCycleStart()
    {
        for(int ordinary=0;ordinary<4;ordinary++)
        for(int alternative=0;alternative<5;alternative++)
        {
            var f=Create();
            foreach(int index in new[] {17,18})
            {
                var face=f.Host.Geometry!.Faces[index];
                face.OuterVertices=face.OuterVertices!.Skip(ordinary).Concat(face.OuterVertices.Take(ordinary)).ToList();
                face.AlternativeOuterVertices=face.AlternativeOuterVertices!.Skip(alternative)
                    .Concat(face.AlternativeOuterVertices.Take(alternative)).ToList();
            }
            Assert.True(f.Expand(out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairedJambs_RejectCompetingCapClaim(bool upper)
    {
        var f=Create(); var box=f.Common;
        double z=box.BaseZFt+(upper?box.HeightFt:0);
        double[] P(double x,double y)=>[box.CentreXFt+x*box.AxisX-y*box.AxisY,
            box.CentreYFt+x*box.AxisY+y*box.AxisX,z];
        List<double[]> ring=[P(-box.WidthFt/2,-box.DepthFt/2),P(box.WidthFt/2,-box.DepthFt/2),
            P(box.WidthFt/2,box.DepthFt/2),P(-box.WidthFt/2,box.DepthFt/2)];
        f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(f.Host.Geometry.Faces.Count);
        f.Host.Geometry.Faces.Add(new GeoFace {OuterVertices=ring,
            AlternativeOuterVertices=ring.Select(p=>p.ToArray()).ToList()});
        Assert.False(f.Expand(out _));
        Assert.False(IfcExporter.TryExpandCommonOpeningFromPairedSourceCaps(f.Cutter,f.Host,f.Common,out _));
    }

    [Theory]
    [InlineData(0.25,0.0)]
    [InlineData(0.0,0.5)]
    public void PairedJambs_UseAuthoredNormalOffsetAndDepth(double offset,double growth)
    {
        var f=Create();
        foreach(int index in new[] {17,18}) ChangeOuterNormal(f,index,offset,growth);
        Assert.True(f.Expand(out var result));
        Assert.Equal(1200+2*growth*304.8,result.DepthFt*304.8,6);
        Assert.Equal(f.Cutter.Transform!.Origin[0]-offset*f.Common.AxisY,result.CentreXFt,9);
        Assert.Equal(f.Cutter.Transform.Origin[1]+offset*f.Common.AxisX,result.CentreYFt,9);
    }

    [Fact]
    public void PairedJambs_RejectIndividuallyValidButDifferentNormalIntervals()
    {
        var f=Create(); ChangeOuterNormal(f,17,0,0.5);
        Assert.False(f.Expand(out _));
    }

    private static void ChangeOuterNormal(Fixture f,int index,double offset,double growth)
    {
        var origin=f.Cutter.Transform!.Origin;
        foreach(var p in f.Host.Geometry!.Faces[index].AlternativeOuterVertices!)
        {
            double normal=-(p[0]-origin[0])*f.Common.AxisY+(p[1]-origin[1])*f.Common.AxisX;
            if(Math.Abs(normal)<=f.Common.DepthFt) continue; // Keep the original inserted corner.
            double delta=offset+Math.Sign(normal)*growth;
            p[0]-=delta*f.Common.AxisY;p[1]+=delta*f.Common.AxisX;
        }
    }

    [Theory]
    [InlineData(0.37)]
    [InlineData(1.5707963267948966)]
    public void PairedJambs_AcceptRigidWorldTransforms(double angle)
    {
        var f=Create(); double c=Math.Cos(angle),s=Math.Sin(angle);
        void Rotate(double[] p)
        {
            double x=p[0],y=p[1];p[0]=x*c-y*s;p[1]=x*s+y*c;
        }
        void Move(double[] p) {Rotate(p);p[0]+=32;p[1]-=17;p[2]+=4;}
        var points=f.Host.Geometry!.Faces.SelectMany(face=>face.OuterVertices!
            .Concat(face.AlternativeOuterVertices??[]).Concat((face.InnerVertices??[]).SelectMany(r=>r)));
        foreach(var p in points) Move(p);
        Move(f.Host.Location!.StartPoint);Move(f.Host.Location.EndPoint);
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
    public void PairedJambs_FailClosed(string mutation)
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
            case "ownership": f.Host.Geometry!.BrepSolids![0].FaceIndices.Remove(17); break;
            case "ownership-duplicate": f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(17); break;
            case "lower-missing": f.Host.Geometry!.Faces[18].AlternativeOuterVertices=null; break;
            case "upper-missing": f.Host.Geometry!.Faces[17].AlternativeOuterVertices=null; break;
            case "alternative-order": f.Host.Geometry!.Faces[17].AlternativeOuterVertices!.Reverse(1,3); break;
            case "source-order": f.Host.Geometry!.Faces[17].OuterVertices!.Reverse(1,2); break;
            case "source-inner": f.Host.Geometry!.Faces[17].InnerVertices=[f.Host.Geometry.Faces[17].OuterVertices!]; break;
            case "duplicate-cap":
                f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(f.Host.Geometry.Faces.Count);
                f.Host.Geometry.Faces.Add(f.Host.Geometry.Faces[17]); break;
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
                foreach(var p in f.Host.Geometry!.Faces[17].OuterVertices!) p[2]=2e14;
                break;
            case "alternative-overflow": f.Host.Geometry!.Faces[17].AlternativeOuterVertices![0][0]=double.MaxValue; break;
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
        int Map(int id)=>id==2721799?99201:id==4511297?99202:id;
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
        var cap=host.Geometry!.Faces[17].OuterVertices!.Concat(host.Geometry.Faces[18].OuterVertices!).ToArray();
        double dx=host.Location!.EndPoint[0]-host.Location.StartPoint[0],dy=host.Location.EndPoint[1]-host.Location.StartPoint[1];
        double length=Math.Sqrt(dx*dx+dy*dy);dx/=length;dy/=length;
        var along=cap.Select(p=>p[0]*dx+p[1]*dy).ToArray();
        var normal=cap.Select(p=>-p[0]*dy+p[1]*dx).ToArray();
        double x=(along.Min()+along.Max())/2,y=(normal.Min()+normal.Max())/2;
        double bottom=cap.Min(p=>p[2]),top=cap.Max(p=>p[2]);
        return new Fixture {Host=host,Cutter=cutter,Common=new IfcExporter.HostedOpeningGeometry(
            x*dx-y*dy,x*dy+y*dx,dx,dy,along.Max()-along.Min(),normal.Max()-normal.Min(),bottom,top-bottom,
            ExactDepth:true,IsExactAuthoredMultiHostEnvelope:true)};
    }
}
