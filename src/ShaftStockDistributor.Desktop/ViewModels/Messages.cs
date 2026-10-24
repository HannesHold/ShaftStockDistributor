using ShaftStockDistributor.Core;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.ViewModels;

public sealed class ProjectChangedMessage
{
    public required DistributorProject Project { get; init; }
}

public sealed class SimulationUpdatedMessage
{
    public required WarehouseSnapshot Snapshot { get; init; }
}

public sealed class ArticleChoice
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public override string ToString() => Name;
}
