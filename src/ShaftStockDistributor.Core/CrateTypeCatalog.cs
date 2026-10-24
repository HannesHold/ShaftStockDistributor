namespace ShaftStockDistributor.Core;

/// <summary>A returnable crate the article generator can assign. Dimensions are millimeters.</summary>
public sealed class CrateType
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public CrateDimensions Dimensions { get; set; } = new();

    public int CratesPerPallet { get; set; } = 40;

    public int Offset { get; set; } = 4;
}

/// <summary>Built-in crate sizes used when a project has not configured its own.</summary>
public static class CrateTypeCatalog
{
    public static List<CrateType> CreateStandard() =>
    [
        Create("HALF-LITER", "Half-liter bottle crate", 400, 300, 280, 40, 4),
        Create("THIRD-LITER", "Third-liter bottle crate", 370, 280, 240, 48, 6),
        Create("MINERAL-WATER", "Mineral-water crate", 400, 300, 300, 36, 4),
        Create("JUICE", "Juice crate", 400, 300, 320, 32, 4),
        Create("SOFT-DRINK", "Soft-drink crate", 300, 200, 250, 40, 5),
        Create("WINE", "Wine-bottle crate", 430, 320, 350, 24, 3),
        Create("SPECIALTY", "Specialty crate", 450, 350, 380, 20, 2)
    ];

    private static CrateType Create(
        string id,
        string name,
        double length,
        double width,
        double height,
        int cratesPerPallet,
        int offset) => new()
    {
        Id = id,
        Name = name,
        Dimensions = new CrateDimensions
        {
            LengthMillimeters = length,
            WidthMillimeters = width,
            HeightMillimeters = height,
            FlowAxis = FlowAxis.Length
        },
        CratesPerPallet = cratesPerPallet,
        Offset = offset
    };
}
