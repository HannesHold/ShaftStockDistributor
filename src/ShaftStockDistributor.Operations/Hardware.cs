using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Operations;

public enum HardwareStage
{
    ArrivedAtShaft = 0,
    TransferFinished = 1,
    Failed = 2
}

public enum PullKind
{
    Discharge = 0,
    Reposition = 1
}

/// <summary>One pull (or a reposition back to home) sent to the plant.</summary>
public sealed class PullJobCommand
{
    public required string JobId { get; init; }

    public required PullKind Kind { get; init; }

    public required ShuttleId Shuttle { get; init; }

    public ShaftAddress Shaft { get; init; }

    public required double TravelSeconds { get; init; }

    public required double TransferSeconds { get; init; }

    public required double FromX { get; init; }

    public required double FromY { get; init; }

    public required double ToX { get; init; }

    public required double ToY { get; init; }

    public required int ToLevel { get; init; }
}

public sealed class ShaftObservation : EventArgs
{
    public required ShaftAddress Address { get; init; }

    public required int Count { get; init; }
}

public sealed class ShuttleObservation : EventArgs
{
    public required ShuttleId Shuttle { get; init; }

    public required double XMeters { get; init; }

    public required double YMeters { get; init; }

    public required int Level { get; init; }

    public required string Phase { get; init; }
}

public sealed class JobResultEvent : EventArgs
{
    public required string JobId { get; init; }

    public required HardwareStage Stage { get; init; }

    public string? FailureReason { get; init; }
}

/// <summary>
/// Boundary to the PLC. The simulation implements it. A real plant adapter is not written yet:
/// the telegram protocol is unknown (OPC UA, S7, and TCP are all still open).
/// </summary>
public interface IWarehouseHardware
{
    event EventHandler<ShaftObservation>? ShaftCountChanged;

    event EventHandler<ShuttleObservation>? ShuttleStatusChanged;

    event EventHandler<JobResultEvent>? JobCompleted;

    void SendPullJob(PullJobCommand command);
}

/// <summary>Placeholder for a future PLC driver. Every call fails with an English explanation.</summary>
public sealed class PlcHardwareStub : IWarehouseHardware
{
#pragma warning disable CS0067
    public event EventHandler<ShaftObservation>? ShaftCountChanged;

    public event EventHandler<ShuttleObservation>? ShuttleStatusChanged;

    public event EventHandler<JobResultEvent>? JobCompleted;
#pragma warning restore CS0067

    public void SendPullJob(PullJobCommand command) =>
        throw new NotSupportedException(
            "No PLC adapter is connected. The plant protocol is still unknown. A future adapter would speak OPC UA, S7, or TCP telegrams and implement IWarehouseHardware.");
}

/// <summary>
/// Virtual plant. Travel and transfer finish later, on the scheduler, so the controller and a live adapter share one decision path.
/// Completing a job inside <see cref="SendPullJob"/> is not allowed; the scheduler must call back afterwards.
/// </summary>
public sealed class SimulatedHardwareAdapter : IWarehouseHardware
{
    private readonly Func<double> _now;
    private readonly Action<double, Action> _schedule;

    public SimulatedHardwareAdapter(Func<double> now, Action<double, Action> schedule)
    {
        _now = now;
        _schedule = schedule;
    }

    public int PullJobsSent { get; private set; }

    public event EventHandler<ShaftObservation>? ShaftCountChanged;

    public event EventHandler<ShuttleObservation>? ShuttleStatusChanged;

    public event EventHandler<JobResultEvent>? JobCompleted;

    public void SendPullJob(PullJobCommand command)
    {
        PullJobsSent++;
        double start = _now();
        _schedule(start + Math.Max(0, command.TravelSeconds), () =>
        {
            ShuttleStatusChanged?.Invoke(this, new ShuttleObservation
            {
                Shuttle = command.Shuttle,
                XMeters = command.ToX,
                YMeters = command.ToY,
                Level = command.ToLevel,
                Phase = command.Kind == PullKind.Reposition ? "Idle" : "Transferring"
            });
            JobCompleted?.Invoke(this, new JobResultEvent
            {
                JobId = command.JobId,
                Stage = HardwareStage.ArrivedAtShaft
            });
        });

        if (command.Kind == PullKind.Discharge)
        {
            _schedule(start + Math.Max(0, command.TravelSeconds) + Math.Max(0, command.TransferSeconds), () =>
            {
                JobCompleted?.Invoke(this, new JobResultEvent
                {
                    JobId = command.JobId,
                    Stage = HardwareStage.TransferFinished
                });
            });
        }
    }

    public void PublishShaftCount(ShaftAddress address, int count) =>
        ShaftCountChanged?.Invoke(this, new ShaftObservation { Address = address, Count = count });
}
