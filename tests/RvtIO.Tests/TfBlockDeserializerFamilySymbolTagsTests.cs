using System.IO.Compression;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// A FamilySymbol whose m_dummyHostInsertionFaceTags is not empty. The generic reading of that list took 24 bytes per entry,
/// the stored layout is 8 (two int32), so every later field of the symbol was shifted and the whole parameter walk failed:
/// Test_2022 lost the stored Assembly Code of a pocket-slider door type and of a sink type (the Uniformat reference
/// "C1020" lacked five of its sixteen objects, "D2010410" was missing altogether).
/// </summary>
public class TfBlockDeserializerFamilySymbolTagsTests
{
    // Test_2022 family symbol 505854 "2.027 x 0.945" of Pocket_Slider_Door_5851 (class 1829, 18966 bytes, gzip).
    private const string PocketSliderSymbol505854 =
        "H4sIAAAAAAAC/+1ceXxU1dm+dxJmQoIQ1oQdwr6FsCQhySR3QsKaQBY2EZUCQdYCVTYVFMTPSqutfv26uJYKVBr9kFb5AAlMRFpRUBDFskgNSEFBSrAasCD57jnzvHPfnJk7k7H2P+7vN2fyPOd93+"
            + "c9595z7rnL5MY2l3b8zcwXnaM1rZtLM7dacxsVJ8rhcYS7x4iyRwzhxx26+dcN07c2xAbj3CaM8G8Cv+Nw4K/SKF8p8K1m7Mm6VmdraX6izE+qM1qWDWTplKVLljGybCjLWFnGybKRLG+RZWNZNpFl"
            + "vCybyrKZLJvLsoUsW8qylSwTZKnm/rpkhHZ78YeudZCl1tGHOvlQZ2mvJ0mkd/F9CadE87PP2VqWbWTZVpbtZNlelh1kKQKsWv/+7MFPN6vsJfpHO+T1pRG/Qzs+P+2Zd68ay2X3HjQ+f+7eWX97qo"
            + "0HvHfVJ5eWHd4fv0ux99btXJP3fXt83x0853fE+ffsZK/1NzW+7s7k+0pn36Ir+sboQWpoi2Z/09FEGvy7joEezsARziDGziDSTeSfZn4uPBalTTS/z5vfD+P7TXyLdFdvqq6ltudtr5YZpmoD/P0h"
            + "kjihh1NzhPjE+XfLbbov5s3Pzc/Nz83Pzc/3/wm2oCG+h0ssNgZqyVqKWaabK4Hl5ifFxBnaYHPWbxVkZhd+d//JpbCTJ330yyUzdxmidtP2auU0VXXpyrVvH6iVtcsDfLc7N6+eV3WfrF1Wp1YwrR"
            + "w7bk/+9f6/75H1i4PUe74u+nZc7V5ZvzQg+m9qNq6cM9XhEbX3B9Q+8eX66z2N49L3voBarnN4h9qqQXsbDcg7UCl9lwTJ62r33WsXnnZJ5XttYx8t1bQV/lqxmhqlLdAWazO1u83vadp8c4+M0+aY"
            + "f88y/55pomJtoTZDm2f+vVjWzTdry6S9mvOoX1HOYhGaZ57FxX5OCbC7/mq1zcJCrBYW2eYu1gw/Cqg9i5WdiPzy9lC9Vr7dTlcusQNqXz0m1oQxMvJLNpGfkJHv8WclFqVubazZZwvMPsoJUFmzic"
            + "epnlDm+CzhpPfGtfffubruC6OuQrWh5kmjLGq1WXjaftCn2elzxr62H4zZVnnBCN40Kwg5R69Wctrg6Gm7shL1YsUe69Aj8OgsPRwReCRJjyjGnAnj0UV6REfg0VV6NIjAo5v0cEbg0V16uCLw6CE9"
            + "YiLoq57So7fONLTQHr2kRx+9/hq9pUffCDz6SI9+EXj0lR7JEXj0kx79I/BIlh4pEXj0lx4DIvBIkR4DI/AYID0GReAxUHoMjsBjkPRIjcBjsH+ci3GoR9nPJavo5CE20WHRDuvqlU5ED185lL24yS"
            + "lv3gdiotyiTm5eNXoHm3g0hVO8LltlPG+4+a5jmPyOlQ18qrB7o8rg8QIn305h8qN4wdsb2O7O+E6R8fx3A7T87r94u2zg50antOurOy45auTs9sUr6VtzpfueaI8/Tq/bf74n9aKb/JJs4qXI/A4Z"
            + "s1v1blp+6Zi3Ron3btaDdz2/7qyRuHnEqfu+OlNBuMv3G89Ly8J98vZAjCbvRck7SGmyTJflEFlmyDJTllmydMsyW5Y5/ntcfKkpbhyJTj8hu+hEdK4sh8oyT5b5shwmy+GyHCHLkbIcJcvRsiyQZa"
            + "Esx8hyrCyLZFksyxJZlspynCzHy3KCLCfKUuS11vykY1F0+Wht7Q/NuX4mw0dM/AuGHzE7Jk33Ydp6MyzGSVeNYVOlD6s/Y3ZMX4bPmrgfw/NMvWSG55t4CIsn8uvP6itNfBerF/n+D8MDzFuL2xku"
            + "N3EK8+9htmcAwz1NPJDhDSYexHCViQczXGLerkzl+ZvYAKaDspdmYdEfVQw3N+2dzD7RxCOBaYmSrFlY+OcwLPpjPsOi/c8xLNrvZVi0/xRwNPRduoWFfgGwvw81Cwv9XIaF/t0MC/31DAv9NxgW+m"
            + "MQvyHNUZqFRfzhDIv4ixkW8TcyLOL/i2HRnk4svmjPt6hvhPouuoVFfRHwLcgnVbOwyGckwyKfpQyLfF5kWOSjIV4T6HVnWOiVADeF3mjNwiL+/QyL+H9gWMTfA9wM/enQLSz0ejIs9D6FfXPUN9Qt"
            + "LOrHAbdEPi9pFhZ6uxgWemeBWyFeI93CIt6dqE9EeyagvjXi/69mYRH/c+A2iNdEt7CI9xjq26I/vgBuB/tmuoWF/SRgWgP8SbOw0PsBcEfk93OGRfxLwJ0Qv6VuYRH/S9QnoT5Rt7Con4H6Loj/Ks"
            + "NC/2vgbvBvq1tY+D+J+h7I5yOGhf8V4J7wb69bWPh/gvreqG+gW1jU/w31fVAfrVtY1Bejvh/yf4Bhkc9Y4GTUHwPuj/xW09iH/TjgVNivYVjUHwdOh/8E4AzYrwDOhv1JYIPGGy5taamoA9OtHQcw"
            + "PWCIAo4CjgaOBm4A3ADYCUzndBf+dgHHANPziYbIlbZYhkVOcQyXYR4iPBPzCGFxbmvM8CzMI4RniwdADM/BPEF4LuYBwvMw7rPZ+akFwzrGOeEyjGvCIr8Ehu/COCc8C+Oa8GyMY8JzMI4Jz8W4JT"
            + "wP7WsP/HSUVudhJR7Xac8ofEfwzyo8Huhpzyl8Z/AXY+vySeAvK3wX8N8ovFgd5uI8wfmJ4MXxy/mHwR9R+DfBP6LE/8bkhmLsibHqb69u8YmMPw37ZMU+Rrd4bl9p4hzdNxailJsLxFHbMhhH7ZrF"
            + "OGrTLxknxqfgdjCuHJwHutHKRTRxpJsNO6dyK4M4sstkHOU3m3GU368YR/m9zjjKLxe6LuW2AHGkOwp2McplN3Fk52Ec5beAcZTfbxlH+VUyjvJzQzdWuWFDHOlmMY505zCOdH/NONL9P8aR7nXG0T"
            + "HWmeVCx9dQcHHKjRjiKL9rjKN4ScyX4hWCa6TceiCO4g1lHLX3HsZRezcwjtq7jXHU3lpwt7D8uuoWR/llgWus3A4jjvJzM47ym8s4yu83jKP8tjCO8rvBOMqvG8uF8ssD10S5lUYc5TcWdvHK7Rfi"
            + "yG4E46gdSxhH7fg946gdOtOgnHswjnLuD9+mTPcVxlEfROkWR/F6MY7iZYJrxtqWrVkctWMe46gdTzGO2pGPeM1ZvJ2axVF+xbBrwR9JaBZHussYR7qbGEe6f2cctTeWaVB7Ddi1ZBo/ZBxpnGEcxY"
            + "vTLY7iZYBrxdrxtGZxlF8F46gPhsE3gfl+plkc6d7C7Ei3FFwi8y3XLI50dzOOdM8xjjQas3ikMQl2rVlfXWAc+cbrFke+Q8C1Yfk9o1kc5fc7xlF+w+Hblvme1yyOdJsyO9IdD64d8/2xZnG0f19m"
            + "HOXyAuMol8vg2jPdVrrFkW46uA5M91bN4qj/HmEc5fIs4yiXrYyjXP7BOMqlOdOlXEaA68hyuahZHPm2YHbkOxFcJ+Z7m2Zx1I5HGUft2Mw4asdoxOvM4j2vWRzZvcY4am814yjnBBaPcp4MuySW38"
            + "8YR/l9Ba4Li9datziKNwV2XVm8fzKOfNvoFke+02HXnfmuZRzlUsM4itdOtziKdxV2PZhdB93iyO6nsOvJNP7COOrn28H1Zvn9hHHkewe4PszuvxhHdvvA9WMab4FLZtxUcP1ZvCJwKYxbxDjSeIdx"
            + "FO9DWk8z7nFwA5nv24zzn6PADWK6qxhHvnngBjO7HzGO7A7QW51MYxq4NOb7BOPIdz/jyLcUXDrzfZBx5DsM3BBm9xDjyO49xpFGGbhM5vsu48iuBFwWs7uXcaQxHpyb2S1nHNn9N7gcxhWCM5jvSs"
            + "aRXQE4D7O7j3Fk9z7jqB1jwPFr0IWMI9/DjCPfQ+CGMm4vuDzGHQQ3nl23fgBuEuOOgLuVcX8GN4VxR8GVMe6v4GYy7mNwsxl3Atwcxq0DN59xf6TrV8bRfRlxTbfyt3H+++789aAC+aZmi1g73Mrh"
            + "w918r/R6/5L5r9hXpr3jXup79dc4jxdB9KPHfO/yFs9bsXffS2fc5V/2u966zbsVLYrmT59fcbyCHqBd3/nc2klvbnU7vy1a/nHyCxmE6QGg+k6x+i6x/kbXOy71O+bYpT7ja/545svXj6x22zxJXK"
            + "U+afTdYDvkbf9Q1etbfn+wQkOLPiOhR30tMgp9Lao4cHRLTfSXB9xqi8pHr+x858atFeWXL4x/v+ULOwjXTSR+B701rbwVbfif8dVtut1GAattnnXWQ8h3h635rv+4EO287yoU8KK58oK5bpdZ6MwD"
            + "HzdHLORrcqwnUqEHIYTA3mgIQdjQ19yzeeoXZw9WqG9aqYEGHzzyhz+eftFt96CbWqS+m+8fR/XNONw4KjffDziZF4ME4yvnrmj+yd5lSZ7DWf98r2JQA48+QW7NPJuv8YMh3v9I/cfb2rY5+tgNd7"
            + "iENihCc1ShUggpU4hf6KF6Cg3GvnkLc91Cda6bLhK5UuOdI2aGC1Xfea5zYApqhynIpU5BCT4ho9Qn9J2noAeUo66hetR98uyFhJ3fnPIfdY/kJz05smvkR12a0nUL1K4rzS041Oy1Gu9c0aJ5373r"
            + "dHRdW3RdjNp1DXxCRolP6Hvruhi167LWP/bkwOgqf9ftTb88ZkXzyLuu4MLbfc6/17JyrURfeTcuSZjw6NfnDN8V3GdefUrOnoTNGz83bshMLhnP461XVejaNzHrTr9y0W03oKebL47ED/qQhkel83"
            + "eiy94y/OPolDd2i+Plq8anFfydmnhP1prGP521/qS7vnPeU13O7cu4u3HlMrQoI7GRc80bTo+cB2rPefWjTdeax9lV4xhaBAdP/aYkC6ex962izZ9eiUXa2WjVX9wCr6nZIy/nYx2G4jM5pM9t0mek"
            + "4jMlpM/t0qdA8bkjpM+d0meM4jM1pM8PpE+R4jMtpM906VOi+MwI6VMmfcYpPjND+twlfSYoPrNC+syWPpMUnzkhfeZKn3TuY77dmYoLFA8W0OK4mAjPEwu2nbi4dH/AYmsr3j+mesIBykOCiGXjim"
            + "MWrngmwPhjm2APebo2W5u/x3jlrxeX7jz5kb1YRhAxNy5lZqKF42FsF2wnMNVvlzgxUCwziFgWrpFmoIXjFLHgwaqMF4p9LdxhJ5YVRExcuIqL+2loYSmM7YLtRsuo/jW7bnQHERNP/8QV+FS0sEQR"
            + "U4MR70OL3DZiq5zZQcTEpbt4DegOtLCojk9gMLuWBLQsJ4hYOg75KWhhcZggOOQNu6HhF/MoYmkQGwmRyaxlFDT4ejZwo3ryc+YGERsif0LhExnB9pldsLrsogrqVqon7BwaRCwDrzKVoIWlYYIpB4"
            + "pXracDyJkXREwc+gUQGcXGmV0wVYyGBtXT0HDmBxETg7oQIqPZDGIXTKs76I3tQeurDOewIGJiuhoDkQI2N4YOZtVvVVpO05lzeBAxMRGPhUghm/Xtgml1J+qAepqonSOCiOXgkJ+IFhYrh76dmN3Q"
            + "IHvnKEVsCMbZMBz2IzG46zOoVXF1nLlGK2LpaNlwdOUInESDjSs1c7tk/C0rVMQyMKjz0apRmCNDXYDaDQV1ULvGKmKZGNR5EBqN2T/URoPWTozqXcWKWBYG9VAIFeC8FmrbHaZlVO8qVcTcGNS5EC"
            + "rEGTv8VmXbMqp3jVfEsjGoPRAag7VIqG1nmJZRvWuiIpaDQW1AaCxWWaE2GrR2YlTvEr9Ro1+WdZD7Z4n5cu5C+Yu7e8xXt5LNcpH59wzzr2nyO1P+Tk/8Xm+xebu2n/n4Iln+pjJFs2Kt+7djOerc"
            + "lC2I+75y5P9sIBqv96Sic4ezRZ1mt+4KZ6AX4ap5/1Qxu+z0Bj8RV4f7+ceqYJmmYTIay85U6snCn2k4g4BMg882dX+EU99M07FEGV6PdU5YA3+mF1bKTI3QK6mAvvXyXx2pmQ7B+mZYiExt16KqQT"
            + "33vppZtXHq09j7V24YXxEq0wwsjvKDnBnUyTisgf4hnlocqGemz5dVftrE9XGF0qdB934mVlZ5Ifp0d7g+JQN/pvm48VXfPg32mzM10ywsy4aGXUJXGWEN9E14bhJZppUVasbBMnVjTZcbItOd4fqU"
            + "DPQNx+re5a9nn3rr06fZWBB6QmRKJ6CwBvph7P3IMq3f2M/BktUIkenWcH1KBv5MI5ulAvtU/PZb/OJe/Ceo/wc24VzJFkoAAA==";

    private static byte[] Block()
    {
        using var input = new MemoryStream(Convert.FromBase64String(PocketSliderSymbol505854));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void NonEmptyDummyHostInsertionFaceTags_DoNotFailTheParameterWalk()
    {
        byte[] block = Block();
        uint size = BitConverter.ToUInt32(block, 8);

        var np = TfBlockDeserializer.ExtractParams(block, 0, size, 1829);

        Assert.NotNull(np);
        Assert.Contains(np!.NamedParams, named => named.ParamId == -1002500 && named.Str == "C1020");
        Assert.Contains(np.Strings, entry => entry.Item1 == -1001405 && entry.Item2 == "51");
    }

    [Fact]
    public void TruncatedBlock_IsRejectedNotMisread()
    {
        byte[] block = Block();
        uint size = BitConverter.ToUInt32(block, 8);

        Assert.Null(TfBlockDeserializer.ExtractParams(block.AsSpan(0, block.Length - 40).ToArray(), 0, size, 1829));
    }
}
