namespace ShaftStockDistributor.Core;

/// <summary>One-based address of a shaft inside the warehouse.</summary>
public readonly record struct ShaftAddress(int Segment, int Level, int Shaft) : IComparable<ShaftAddress>
{
    public override string ToString() => $"S{Segment}/L{Level}/H{Shaft}";

    public int CompareTo(ShaftAddress other)
    {
        int segment = Segment.CompareTo(other.Segment);
        if (segment != 0)
        {
            return segment;
        }

        int level = Level.CompareTo(other.Level);
        return level != 0 ? level : Shaft.CompareTo(other.Shaft);
    }
}
