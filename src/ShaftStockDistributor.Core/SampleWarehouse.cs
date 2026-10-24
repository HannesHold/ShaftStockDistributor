namespace ShaftStockDistributor.Core;

/// <summary>
/// Illustrative returnable-crate mix for a sorting warehouse. Every name, inflow, pallet size,
/// and shaft capacity is an assumption for the prototype, not a measurement.
/// </summary>
public static class SampleWarehouse
{
    public const string ExampleNote =
        "EXAMPLE DATA. Crate types, inflows (crates/hour), pallet quantities, offsets, shaft capacities, "
        + "and shuttle kinematics are illustrative assumptions for this prototype. They are not measurements "
        + "from a warehouse. Indexes in these files are one-based (segment 1 is the leftmost rack).";

    public static LayoutDocument Layout()
    {
        return new LayoutDocument
        {
            ExampleData = true,
            Notes = ExampleNote + " Default geometry: 12 segments x 5 levels x 9 shafts. "
                + "Shuttle 1 home is the left end at level 1; shuttle 2 home is the right end at level 1.",
            SegmentCount = 12,
            LevelsPerSegment = 5,
            ShaftsPerLevel = 9,
            DefaultShaftCapacity = 48,
            ShaftPitchMeters = 0.45,
            LevelHeightMeters = 0.50,
            SafetyDistanceSegments = 1,
            Kinematics = new KinematicsDocument(),
            Shuttle1Home = new ShuttleHomeDocument { Segment = 1, Level = 1, Shaft = 1 },
            Shuttle2Home = new ShuttleHomeDocument { Segment = 12, Level = 1, Shaft = 9 }
        };
    }

    public static ArticlesDocument Articles()
    {
        return new ArticlesDocument
        {
            ExampleData = true,
            Notes = ExampleNote + " ABC shares apply only to articles that omit cratesPerHour. "
                + "Every sample article sets an explicit rate so the scenario is stable.",
            AbcDefaults = new AbcDefaultsDocument
            {
                TotalCratesPerHour = 1500,
                ShareA = 0.70,
                ShareB = 0.20,
                ShareC = 0.10
            },
            DefaultCratesPerPallet = 40,
            DefaultOffset = 4,
            Articles =
            [
                Article("BEER-20-05", "Example: Euro beer crate 20 x 0.5 L", AbcClass.A, 360, 40, 4, 48),
                Article("BEER-24-033", "Example: beer crate 24 x 0.33 L", AbcClass.A, 280, 44, 4, 52),
                Article("BEER-11-05", "Example: NRW beer crate 11 x 0.5 L", AbcClass.A, 200, 60, 6, 72),
                Article("BEER-20-LONG", "Example: long-neck beer crate 20 x 0.5 L", AbcClass.A, 150, 40, 4, 48),
                Article("WATER-12-075", "Example: mineral water crate 12 x 0.75 L", AbcClass.B, 70, 32, 4, 40),
                Article("WATER-12-100", "Example: mineral water crate 12 x 1.0 L", AbcClass.B, 55, 24, 3, 32),
                Article("SODA-12-100", "Example: soft drink crate 12 x 1.0 L", AbcClass.B, 40, 24, 3, 32),
                Article("JUICE-06-100", "Example: juice crate 6 x 1.0 L", AbcClass.B, 30, 40, 4, 48),
                Article("EXPORT-24-033", "Example: export stubby crate 24 x 0.33 L", AbcClass.B, 25, 44, 4, 52),
                Article("WINE-12-075", "Example: wine crate 12 x 0.75 L", AbcClass.C, 12, 36, 4, 44),
                Article("KEG-PARTY", "Example: party-keg crate", AbcClass.C, 8, 16, 2, 22),
                Article("SPIRITS-06", "Example: spirits crate, 6 bottles", AbcClass.C, 6, 48, 4, 56)
            ]
        };
    }

    public static ParametersDocument Parameters()
    {
        return new ParametersDocument
        {
            ExampleData = true,
            Notes = ExampleNote + " expectedPullWaitSeconds is a planning input for shaft sizing. "
                + "Annealing weights are dimensionless relative weights on pull-seconds, imbalance, and shared-zone traffic.",
            ExpectedPullWaitSeconds = 300,
            Weights = new CostWeightsDocument(),
            Annealing = new AnnealingDocument()
        };
    }

    public static Layout CreateLayout() => WarehouseMapper.ToLayout(Layout());

    public static ArticleSet CreateArticles() => WarehouseMapper.ToArticles(Articles());

    public static PlanningParameters CreateParameters() => WarehouseMapper.ToParameters(Parameters());

    private static ArticleDocument Article(
        string id,
        string name,
        AbcClass abcClass,
        double cratesPerHour,
        int cratesPerPallet,
        int offset,
        int shaftCapacity) => new()
    {
        Id = id,
        Name = name,
        AbcClass = abcClass,
        CratesPerHour = cratesPerHour,
        CratesPerPallet = cratesPerPallet,
        Offset = offset,
        ShaftCapacity = shaftCapacity
    };
}
