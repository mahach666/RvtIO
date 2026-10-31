using System.IO.Compression;
using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// A room outline with a 0.6 mm gap between two consecutive edges of one loop and a 0.9 mm sliver edge next to it: the
/// three points form one vertex cluster even though the outer two are farther apart than the weld tolerance, and an edge
/// that collapses into one cluster is dropped. The loop-by-loop reading welds as the whole-shell reading does.
/// </summary>
public class GRepParserV2SpaceBoundaryBridgedGapTests
{
    // sample project A room 2772756 (class 2007, 17141 bytes, gzip): 26 Faces, 21 vertices on its largest floor loop.
    private const string Room2772756 =
        "H4sIAAAAAAAC/61cCXgVRba+lxu6AwkhCQkJYRFCIothEZKQQMLtkAXICoEAYSdsAcIa9mEJIpsYIUZUREF2BQcRlOcYIFeZ8QMBBUcfyqKoI+iM+uWxPHUG5N06+esmqe7qDt/38n39375UnfpPVV"
            + "edOudUX4JyOtsSlDy/7/vZbJfUIPc39vfA/ee12dtmd9873FeUd5NhByufadpFCzt7pXjiulTXpB5bMx8t76yFnRi758fQRO1+F/9nqs5rrl0XC6PDmiRoD1vfYbN5mAM2N2hEGri8bWHuf/NyXzFK"
            + "Q0KFUCX0JmxE2JjQh9CXsAmhH2FTQn/CAMJAwmaEQYTBhM0JQwhDCVsQhhG2JGxF2NqjCdOV6c5Ga5z7auO+rng9QtiWsB1hOGF7wgjCSMJHCTsQdiTsRNiZ8DHCKMIuhF0JuxF2J3ycsAdhT8Jowh"
            + "jCWMJehHGE8YS9CfsQJhAmEvYldBJqhEmE/QiTCVMIUwnTCPsTDiAcSJhOmEGYSZhFmE2YQziIcDBhLuEQwqGEeYTDCIcTjiDMJxxJOIpwNOEYwrGE4wjHE04gLCCcSDiJcDLhFMKphIWE0winE84g"
            + "LCKcSTiLcDbhHMK5hPMIiwnnEy4gXEi4iHAx4RLCpYR/IlxGaKv1t989nbye8NaWu+9v1CmxYU3c/d8PHqxwfzZuYBdkVhrLNKiWKSGZBoLMKlOeJ0jGIcisNpV5kmS8BJk1pjJrSaahILPOVGY9yS"
            + "iCzAZTmadIRhVkNprKPE0y3oJMqanMMyTTVpDZZCqzmWTaCTJlpjLPkky4IFNuKvMcybQXZLaYyjxPMhGCzAumMi+STKQgs9VU5iWSeVSQ2WYq8zLJdBBkXjGV2U4yHQWZHaYyr5JMJ0Fmp6nMLpLp"
            + "LMjsNpXZQzKPCTJ7TWX2kUyUILPfVOY1kskTZF43lTlAMt81qCtz0CPzhvv6ii8vmwLL82eSetpRV+qQIZMCpjdJplSQOWwq8xbJ+NfyKdwyNmZVmG/Adma2L7BdsxR19ry/8p87/t1OKzk7fNWM6x"
            + "9VXp8T18pvXRdNi+//cVnxT07/7G3np/Rsqyv3MAcYkDF3hC39MXAKNqKOrDH/tpc//yI1WKvKqi5/e8InSz6fFaTpyAINyJgHxGzGSPRwA+pIG/PPGLDyLxVOXn6ntGphxTUDsmYGZMzpYsZmGHq4"
            + "TiATG4u6efP27/fcPe5QfmZSjy8rb8jIggzImMVlVmooergGdWSNbe8W2+XNK4Ga7dHqcu3Wp1kF/tedOrJgAzI2Z9n2mYserua1JY3x77ycV9eRNTcgY/OV+Ws5IF0lkOkaq/53Jx8AKVlvAzLmyD"
            + "KDWgDS/XXXTuIXJ9/u9nOG3eXp2Qfju4f63nCW4FMs95D1MSBjvjOzxMyTYpZ/L1dZ0tgPuc3i/+araC6UX/eeFJ3/RjM9WYIBGXPXmQmfih7uRh1ZY67mKWnRNkWb0390bu/VPtpVGVmiARl7jp3g"
            + "6bMe7kQdWWONfnt+a8DPB5zXj6deK/jER9t86oslMz81IOtrQMZmKNs0stDDHdyCRCYOzPnUR5vf9FryrxdqGlt7YJ3y45Y9Tlfw1UvrfX21c+uifIJ+MSBzGpCxtcd2m0z08BU+jJLGKr0mL7/b/7"
            + "Dzwj8jJuwq9NVSWq9PU10GZJoBGbMqbJvKRw+3oY6ssaqDCyaUL61ytl3uG+7fJkC7E9Eg7sPXm+vJkgzImL1ksdMQ9HAr6sgaK25Y3bMXAwvHOKr8tZJN++59esmArJ8BGdsJWIiWgR6+gDqyxoZW"
            + "Tlhz8vkgrSTx7n/ur3hEi7q29fyUo+F6smQDMrbHsUiwB3pYxNeZpDHXd/EvnP3+ljPuyjfufwmXk6UYkLEwujtIWA/LUUfW2LJIZq5aaFEof7dT0u6Lhd56slQDMj/s0DPQwzLUkTb2ge/jyecOO1"
            + "/DTi4lSzMga4Idejp6uAl1pI39+bF/ffSeosncBg9ZpkAWgGfGJgXztLMxlEZWXHxmNskfL1cPGfQsDAaYhdjMbX+9rqxuC4Fh1mS7gqdnTe11ydjGyfIjLJ5l8T+LifkkevrW7nsdnWGezfKDhJ2D"
            + "Og5N1977n7LD3f2SXLycN87LVyeeiZ+XneRSAgUyH5CxMJvtCCz0ThfIuDdVHuv2Scqztewt6Xtzp+nJnkL5i7vaBHdblehSQgQyNu1ZzoctdpbfYJkO8WGUtI0YdG27t7Zs3JGbST5xGifhn2K5Zx"
            + "hbCGR+6NkcELJ0QqrQMz7Vv/yyKHZbkxytzaD7Qx581k/Xs8VDPguKaVhTroTZ9bOxNWYh86iZhVkqkCXYn6pI8gnS1vv8cTT/5QBNnH1iOZdTWglkzdAzlvJhwQZL5UwWyI7Byh9/K00LycrUuiS9"
            + "FF78YbKnZ98WfX1u4W+2E0dvLWiTcyBTC3/ijO1g+xSX0kkgCwEZywWxXZzleEYLZNwNaHHj7tITX6VrO07mjb9/Klk3jGs6jKx09c3UtqNciRLIWoCMpZnYLl4I0jpTH75I04nRYaVr07U5G/7q+/"
            + "hE/TMLhTK8XCkSyHxBNg8Tg+WYYkQyRC2LMAFkU/9ZYWkoCwWyUJCx4euDHvYWyLjrJmoukonDrLwtkIWBrBCWYyFIa5Nx8+SHYeQWQiQTh1n5RSBriUXNUnosQcpCV24/L2NddXz2Qcefx6a4lgiL"
            + "dvs7yr+L/pGtadsdl1wxKa6v7oyN+MvKWou6SiBrhXXGCJbAisxEZS4s9oB/FxvXxby3BbJGGMZFsImzYJBrN8rjMW771g//7y0LJifqhrEShvhplCv3DGxjS5ipgxjKXgLZZtg+PqwdMKwimWjOlD"
            + "8MzBUjY0ns2fCwJgpkA+FNFUz+Zk3pnky3hzx2a1GnGrJImKtud9ae+uZwppaEZ6gENqhLpoCMzcJo7Gt9RUOMeEzcr8SeibNVCRLImoBsAYZvDva1OmTwOUSrLpKJi17pIJAFg4z5JqMQXAwXyHgg"
            + "MS7mtN+R5Ext7tcp4WWna2zjZRjiGRFH9uf1z9SWf9y1Ivqce1E/IZB5wW9cjsnBpv4kkPWoduVckXuPLLm0OtTFe1CGCQO/0sX9Sl7O/Uql1ICsFXZoDWstFkID0geuXLmonauq4d9/nN87xHVZWH"
            + "ecrCOUWSzs5Mpmgcwbw8gcnSSst2hhGHkqQlxHMoeHO0RKuUDWGGSzYO3ng7TOBEFGR3RoZA4PX/TK8wJZIKKYrXAJZiO4MHNSeRQjM2O8XN1qMBuDENqyWZiGgNCMjEeeMjJerr4kkDVHTL0TRKMQ"
            + "6pqRtXq9einIyHi52tmhn40B8KhYfLYYpqtWfKab2vzPvuE59+oNcP2A2cr/vTvMlxLt0LvfCvJVf8IVYdEzbr42n2HHszU983xHuRrj0LtygYinmQsXj/s6wsL3+Q3NnxmPudVYh94QhyE5thBXTw"
            + "sy7ibIes7LlV4O/aJWkWOci6u9xTDyqEamTBLK1TjJMG6DVzwIGQMzMj5MVsOsxjv0boE3UrWLcIVbkHHzJSPj5WpvgSwI5moEFvVwTJLaD/x+xoE9t3sdciaX+/9XpXsL2TPuwsGPztZsMef7rJyy"
            + "/dUbCRWIBaIQCyh9HHrb2AgZ71m42ln0jDtAsp51QbmaIOlZHixIJu5r94w/g+nC5mjVc6WvAVkzJMniQZRvMfV5oky2zni5qknIXsFzG4Fkmdkw8saslFGTHPoAvjGORObjamtlrrC/Sc0VytV+Dr"
            + "3VbwlHZzSuNGFYfvvk1/nHNox35g8YuGqo2+GZJzg8mI3Hr570HrDaXX43z+vHSne5kiIhG4VQaSzua5PF3K3OCa8VIksrZZRUhz4Y9MEx1jxcj1gMI8+LyIaRl6tpDn1M3RyZ79Ho3TiL2dgY2W/Z"
            + "bOTZcXWAhGw3hnAsMuBmPeONWSmjDnTo3W9fpP0W4GpjZfURY8vIeLma7tBnC0JxNJKIEHeaxTDyGFs2jLxczXLoY+oWSPsV4oq06hlSFdKeoVzNduhj6tbwGWfiOsw93IrxF957K1NzIt76+uNuWW"
            + "VbatJJXRGP8Zi6REg3KYMEMn8EFkXw95fVyn7LNOeBgyyW5uXqYIfeI24JTzgeO/Vs4Rl0VKqt/hHkptohN8XLBx9sHn+1XbHT643Rrt/dkemF09+OtbkjUyXPoU+6hOA4qzcmyVSLnvFclmyC8HJ1"
            + "mIRsL7I8U3CsZZqQRmNWyqjDJbOxANO+L+7NesZnm6xnntk4QrLO9iNvNQGng2Y94+vIah2qIx36tG0TnFLMwfW41X6GgF66n6FcHe3QJ138cP5SjKu7FRlyxlIylKvjJeusKwL3STiPMSPj68hqHa"
            + "pFEgsyEQG8s1aGR8y6iYvXysIoKyR+4zDE0um4N5uN3C+UzUbuV6pPSixIBkKmybCTtYVlFkIkm4DcVjFyW8pGyWycjhxxL9yb9YzPNlnPPLPxpiRkykX+YyDuzch4SCRd1DxkCvEydnjGIA+SgHsz"
            + "Mu7QyMg8Dk8rL+N1NgND2A/3psOIdSQdRr7OIr2MPeKRmIkxuDclg8crJeMecYyXscMzHkOYintTQwyHRvrMuMMT52UcwA+GxU/BfR1NJXl+qwBfSfYyDnOHYiYm4d6sZzyMlSnjCXMzvPQZHn+cny"
            + "3DtQXC6sXI6Nlr9RmeXVsWHVNO33LKMkD8NFfJNCBrjQzqUlxc1Vf/VvDWzPyadBG3fRv5s0HyU0wneZKbgyVkS5CzysY9/Qm5qRLh5M9KGWWol3HuKgeTIxr3plMfuSnp1Oe5q7mSZ7YYe9ly3Nd6"
            + "kUGalbN6pgr/KQCR2bw9r0wug5OqIxDTfFYV7E9+XJYYW9lTyxy6e3ieLcnzA4q6ilYJ59NVlWJXjDRtiJxh+1ov13FF+Dse/M0+ywo6TflPQOq+eeDRrORhNFWg6WADE6J7Wc6qQj3HVDaWNWNtpK"
            + "mK8cwx0ZTvhpYVPJrautb9+Yy5pvUbU28kBHNNNPW8WmlVwX4rtv1jR/s+vKZ3N/617UdxdxLMNG2EBN9QE025o2ZZwR6MnyqJP0mSaTrv24hN/2p5+bg4f400bYy01jCzeYpN2rKC3Q+anu7Q2Kdb"
            + "k9T6jqmzPk/fB5mjkSaackfJsoK96099Ttr7P7ymi9On/vbDAu8TZpr6IjkzxkRT7otYVrC3XPFc0K93emoXHkpT/XcjTZsgCBxv9vThVltW8Gj6Tkpy7v0V9R/TPR0i5kWX3ThupqkfIsjpZprCc7"
            + "WsYK+4/fvcQeditWMPpWn9xrQpws8ZBi2J4adlBY+mIfiZYr1XVH7piOLcX0zHlDsZPUw0lb7aJ1aw8x9Ohj2UpvXbowJwvmumKT+/tazg0fTef9qFxFSlWGrqqs6sV16u/jTVNBAnZBkm85Tn4Swr"
            + "2CfNq7jXeUG8tmPYoFN5bZItNT2dUK3pxd7Vn2aWvxlOT4bU0pS/vSaenlhWsL8//Nmz+6fFa1Xf/WNUSIS1pocwpj/VY0yDcOCebzKm/ETLsoL98897No17M077fdc7y7/+sJ+lpjhXcZ6vh6bBOK"
            + "3PrKXpZWHI+IsrlhXsi6pGaxXvxWmvBbf7vvX79da0sj6aNsdpRVYtTfkJEleEn0ZYVrAXPlBfvng0Tgs7vGhNxon/X01DkO8dZ/L0eT7XsoK94sKBpzIPxGmnQs/cW/+utabn3pnik33si4T6WKlQ"
            + "5G+nmmjK87OWFezHoenwS5PeXTMiqd7+6fuHKr6pPHjT1D9tgUOWaWYeCtLWlhXs46as/+7oDzHasIfStH6WPwzvUhSYaOp5I9yqgkfTrIeKo6oq4w81jB6w9CfTMW2JbHC/Wm8l8vcz+YuF/IUmTw"
            + "NXKPyuTivz4Xiydfia2b49NUe+vWh0RReXudPMP9GiPbAe9HbeNKd68Jnzbs7yGK2+fo/ZOLTCOLDGFgvvpepeJbWqYC8KuXi8xZxUrXBZ2oRzaraLa7qvpX1fiNuCFNy+3D5y5w3dD87EOWakKc/j"
            + "xxlIiC+LW1aw/7FJWfz30njt6r4FvWLGprqEMS0RZrtUU/YfXfwfwEVCL/VCAAA=";

    private static byte[] Block()
    {
        using var input = new MemoryStream(Convert.FromBase64String(Room2772756));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void RoomWithABridgedGapInOneLoop_IsReadAsAClosedShellOf26Faces()
    {
        byte[] data = Block();
        var body = GRepParserV2.ParseExactPlanarSpaceBoundary(data, 0, (uint)(data.Length - 16), 2772756);

        Assert.NotNull(body);
        Assert.Equal(26, body!.Faces.Count);
        var solid = Assert.Single(body.Solids);
        Assert.True(solid.IsClosed);
        var geometry = new GeometryData
        {
            Faces = body.Faces.Select(face => new GeoFace { OuterVertices = face.Vertices, InnerVertices = face.InnerVertices }).ToList(),
            BrepSolids = [new GeoBrepSolid { FaceIndices = solid.FaceIndices, IsClosed = true }],
        };
        Assert.True(IfcExporter.TryOrientExactFacetedShell(geometry.Faces, solid.FaceIndices, out _, triangulateNonPlanarFaces: false));
    }

    [Fact]
    public void BlockOfAnotherOwner_StaysUnread()
    {
        byte[] data = Block();
        Assert.Null(GRepParserV2.ParseExactPlanarSpaceBoundary(data, 0, (uint)(data.Length - 16), 2772757));
    }
}
