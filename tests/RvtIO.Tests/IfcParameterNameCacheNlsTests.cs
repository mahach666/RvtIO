using System.ComponentModel;
using System.Runtime.InteropServices;
using RvtIO.Ifc;
using Xunit;

namespace RvtIO.Tests;

// Tests use Windows NLS explicitly; production cache has no OS/vendor dependency
// and requires its source-compatible comparison backend from the caller.
public class IfcParameterNameCacheNlsTests
{
    private sealed class WindowsNlsComparer : IComparer<string>
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern int CompareStringEx(string locale, uint flags,
            string left, int leftLength, string right, int rightLength,
            IntPtr version, IntPtr reserved, IntPtr parameter);

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            // Framework CompareInfo.GetNativeCompareFlags(IgnoreCase), not
            // LINGUISTIC_IGNORECASE or SORT_STRINGSORT. Explicit UTF16 lengths
            // preserve embedded NUL input (there is no StopOnNull option).
            int result = CompareStringEx("", 0x08000001, left, left.Length, right, right.Length,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            return result - 2;
        }
    }

    [WindowsTheory]
    [InlineData("FINISH", "finish", 0)]
    [InlineData("Total Thickness", "TotalThickness", 0)]
    [InlineData("ТОЛЩИНА", "толщина", 0)]
    [InlineData("\u00e9", "e\u0301", 0)]
    [InlineData("\u00df", "ss", 0)]
    [InlineData("a\u00adb", "ab", 0)]
    [InlineData("a\0b", "ab", 0)]
    [InlineData("Total_Thickness", "TotalThickness", -1)]
    [InlineData("Total\tThickness", "TotalThickness", -1)]
    [InlineData("I", "i", 0)]
    [InlineData("\u0130", "i", 1)]
    [InlineData("\u0131", "i", 1)]
    public void WindowsComparisonAndCacheAgreeWithActualFramework174Vectors(string left, string right, int expected)
    {
        var comparer = new WindowsNlsComparer();
        Assert.Equal(expected, comparer.Compare(left.Replace(" ", ""), right.Replace(" ", "")));
        var first = new IfcParameterNameCache.ParameterFact(-10,
            new(left, 0, true, true), true);
        var last = new IfcParameterNameCache.ParameterFact(10,
            new(right, 0, true, true), true);
        var cache = IfcParameterNameCache.Create([last, first], comparer, true, out _)!;
        Assert.Same(expected == 0 ? first : last, cache.Find(right));
    }

    [WindowsFact]
    public void UnicodeEquivalencePreservesFirstHasValueWithTheExplicitSourceBackend()
    {
        var comparer = new WindowsNlsComparer();
        var noValue = new IfcParameterNameCache.ParameterFact(-20, new("\u00df", 0, true, true), false);
        var firstValue = new IfcParameterNameCache.ParameterFact(-10, new("ss", 0, true, true), true);
        var later = new IfcParameterNameCache.ParameterFact(10, new("SS", 0, true, true), true);
        var cache = IfcParameterNameCache.Create([later, noValue, firstValue], comparer, true, out _)!;
        Assert.Same(firstValue, cache.Find("\u00df"));
        Assert.Same(firstValue, cache.Find("ss"));
    }

    [WindowsTheory]
    [InlineData(null, null, 0)]
    [InlineData(null, "", -1)]
    [InlineData("", null, 1)]
    [InlineData("", "", 0)]
    [InlineData("", "a", -1)]
    [InlineData("a", "", 1)]
    public void ComparisonHandlesNullAndEmptyInputs(string? left, string? right, int expected)
        => Assert.Equal(expected, new WindowsNlsComparer().Compare(left, right));
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Original Framework comparison backend test requires Windows NLS";
    }
}
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Original Framework comparison backend test requires Windows NLS";
    }
}
