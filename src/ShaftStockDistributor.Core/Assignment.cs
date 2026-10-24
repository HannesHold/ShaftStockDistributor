using System.Diagnostics.CodeAnalysis;

namespace ShaftStockDistributor.Core;

public sealed record ShaftAssignment(string ArticleId, bool Pinned);

/// <summary>Article assigned to each occupied shaft. Empty shafts are simply absent.</summary>
public sealed class Assignment
{
    private readonly Dictionary<ShaftLocation, ShaftAssignment> _shafts = [];

    public IReadOnlyDictionary<ShaftLocation, ShaftAssignment> Shafts => _shafts;

    public bool ExampleData { get; set; }

    public string? Notes { get; set; }

    public void Set(ShaftLocation location, string articleId, bool pinned) =>
        _shafts[location] = new ShaftAssignment(articleId, pinned);

    public bool Remove(ShaftLocation location) => _shafts.Remove(location);

    public bool TryGet(ShaftLocation location, [NotNullWhen(true)] out ShaftAssignment? assignment) =>
        _shafts.TryGetValue(location, out assignment);

    public bool Contains(ShaftLocation location) => _shafts.ContainsKey(location);

    public Assignment Clone()
    {
        var copy = new Assignment { ExampleData = ExampleData, Notes = Notes };
        foreach (var (location, assignment) in _shafts)
        {
            copy._shafts[location] = assignment;
        }

        return copy;
    }

    public int CountFor(string articleId)
    {
        var count = 0;
        foreach (var assignment in _shafts.Values)
        {
            if (assignment.ArticleId == articleId)
            {
                count++;
            }
        }

        return count;
    }
}
