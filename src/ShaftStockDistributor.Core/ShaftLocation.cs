namespace ShaftStockDistributor.Core;

/// <summary>
/// Zero-based shaft address. JSON and the UI use one-based indexes; convert at the file boundary.
/// </summary>
public readonly record struct ShaftLocation(int Segment, int Level, int Shaft) : IComparable<ShaftLocation>
{
    public int CompareTo(ShaftLocation other)
    {
        var segment = Segment.CompareTo(other.Segment);
        if (segment != 0)
        {
            return segment;
        }

        var level = Level.CompareTo(other.Level);
        return level != 0 ? level : Shaft.CompareTo(other.Shaft);
    }

    /// <summary>One-based label, for example S01-L1-H3.</summary>
    public override string ToString() => $"S{Segment + 1:D2}-L{Level + 1}-H{Shaft + 1}";

    public string Key => $"{Segment}:{Level}:{Shaft}";

    public static ShaftLocation FromOneBased(int segment, int level, int shaft) =>
        new(segment - 1, level - 1, shaft - 1);
}
