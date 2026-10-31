using System.IO.Compression;
using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcExporterPairedOpeningCapsTests
{
    private sealed class Fixture
    {
        public required Element Cutter;
        public required Element Host;
        public IfcExporter.HostedOpeningGeometry Common;
        public bool Expand(out IfcExporter.HostedOpeningGeometry result) =>
            IfcExporter.TryExpandCommonOpeningFromPairedSourceCaps(Cutter,Host,Common,out result);
    }

    [Fact]
    public void PairedCaps_RestoreOnlySourceDepthAndNormalPlacement()
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
    public void PairedCaps_AcceptReversedCycles()
    {
        var f=Create();
        foreach(var face in f.Host.Geometry!.Faces)
        {
            face.OuterVertices?.Reverse(); face.AlternativeOuterVertices?.Reverse();
        }
        Assert.True(f.Expand(out _));
    }

    [Fact]
    public void PairedCaps_RejectEveryIndividualWitnessCoordinateMutation()
    {
        int count=0;
        foreach(int index in new[] {19,22})
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
    public void PairedCaps_AcceptOppositeCommonAxisAndCapOrder()
    {
        var f=Create();
        f.Common=f.Common with {AxisX=-f.Common.AxisX,AxisY=-f.Common.AxisY};
        var faces=f.Host.Geometry!.Faces;
        (faces[19],faces[22])=(faces[22],faces[19]);
        Assert.True(f.Expand(out var expanded));
        Assert.Equal(1200,expanded.DepthFt*304.8,6);
    }

    [Theory]
    [InlineData(0.37)]
    [InlineData(1.5707963267948966)]
    public void PairedCaps_AcceptRigidWorldTransforms(double angle)
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
    public void PairedCaps_FailClosed(string mutation)
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
            case "ownership": f.Host.Geometry!.BrepSolids![0].FaceIndices.Remove(19); break;
            case "ownership-duplicate": f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(19); break;
            case "lower-missing": f.Host.Geometry!.Faces[22].AlternativeOuterVertices=null; break;
            case "upper-missing": f.Host.Geometry!.Faces[19].AlternativeOuterVertices=null; break;
            case "alternative-order": f.Host.Geometry!.Faces[19].AlternativeOuterVertices!.Reverse(1,3); break;
            case "source-order": f.Host.Geometry!.Faces[19].OuterVertices!.Reverse(1,2); break;
            case "source-inner": f.Host.Geometry!.Faces[19].InnerVertices=[f.Host.Geometry.Faces[19].OuterVertices!]; break;
            case "duplicate-cap":
                f.Host.Geometry!.BrepSolids![0].FaceIndices.Add(f.Host.Geometry.Faces.Count);
                f.Host.Geometry.Faces.Add(f.Host.Geometry.Faces[19]); break;
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
                foreach(var p in f.Host.Geometry!.Faces[19].OuterVertices!) p[2]=2e14;
                break;
            case "alternative-overflow": f.Host.Geometry!.Faces[19].AlternativeOuterVertices![0][0]=double.MaxValue; break;
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
        int Map(int id)=>id==2054545?99201:id==2054795?99202:id;
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
        var cap=host.Geometry!.Faces[19].OuterVertices!;
        double dx=host.Location!.EndPoint[0]-host.Location.StartPoint[0],dy=host.Location.EndPoint[1]-host.Location.StartPoint[1];
        double length=Math.Sqrt(dx*dx+dy*dy);dx/=length;dy/=length;
        var along=cap.Select(p=>p[0]*dx+p[1]*dy).ToArray();
        var normal=cap.Select(p=>-p[0]*dy+p[1]*dx).ToArray();
        double x=(along.Min()+along.Max())/2,y=(normal.Min()+normal.Max())/2;
        double bottom=host.Geometry.Faces[22].OuterVertices![0][2],top=cap[0][2];
        return new Fixture {Host=host,Cutter=cutter,Common=new IfcExporter.HostedOpeningGeometry(
            x*dx-y*dy,x*dy+y*dx,dx,dy,along.Max()-along.Min(),normal.Max()-normal.Min(),bottom,top-bottom,
            ExactDepth:true,IsExactAuthoredMultiHostEnvelope:true)};
    }
}
