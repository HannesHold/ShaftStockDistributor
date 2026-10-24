namespace ShaftStockDistributor.Core;

/// <summary>How often an article arrives. A arrives often, C rarely.</summary>
public enum AbcClass
{
    A = 0,
    B = 1,
    C = 2
}

/// <summary>Which crate dimension points along the shaft (front to back).</summary>
public enum FlowAxis
{
    Length = 0,
    Width = 1,
    Height = 2
}

/// <summary>Who may pull shafts in a segment.</summary>
public enum ZoneKind
{
    Shuttle1Only = 0,
    Shared = 1,
    Shuttle2Only = 2
}

/// <summary>
/// Manual keeps the stored assignment. Automatic may move an article onto an empty shaft at runtime.
/// </summary>
public enum AssignmentMode
{
    Manual = 0,
    Automatic = 1
}

public enum ShuttleId
{
    Shuttle1 = 1,
    Shuttle2 = 2
}

public enum ValidationSeverity
{
    Warning = 0,
    Error = 1
}
