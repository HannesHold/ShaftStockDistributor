using ShaftStockDistributor.Core;
using ShaftStockDistributor.Operations;

namespace ShaftStockDistributor.Tests;

public class InterlockTests
{
    [Fact]
    public void Overlapping_ranges_are_rejected_and_safe_ranges_are_kept()
    {
        var interlock = new ZoneInterlock(1.5);
        interlock.Hold(ShuttleId.Shuttle1, Interval.Point(0));
        Assert.True(interlock.Conflicts(ShuttleId.Shuttle2, Interval.Span(0, 4)));
        Assert.False(interlock.Conflicts(ShuttleId.Shuttle2, Interval.Span(2, 6)));
        interlock.Hold(ShuttleId.Shuttle2, Interval.Point(6));
        Assert.True(interlock.ReservationsAreSafe());
    }
}

public class AutomaticAssignmentTests
{
    [Fact]
    public void Non_empty_and_pinned_shafts_are_never_reassigned()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        var filled = shafts[0];
        filled.Count = 4;
        string? suggestion = AutomaticAssignmentPolicy.Suggest(
            filled, shafts, articles, new OperationSettings(), AssignmentMode.Automatic, now: 100,
            _ => 100, reassignmentsInLastHour: 0);
        Assert.Null(suggestion);
        Assert.Equal("SLOW", filled.ArticleId);

        var pinned = shafts[1];
        pinned.Count = 0;
        pinned.Pinned = true;
        suggestion = AutomaticAssignmentPolicy.Suggest(
            pinned, shafts, articles, new OperationSettings(), AssignmentMode.Automatic, now: 100,
            id => id == "FAST" ? 500 : 1, reassignmentsInLastHour: 0);
        Assert.Null(suggestion);
    }

    [Fact]
    public void Empty_unpinned_shaft_can_move_when_another_article_needs_it()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        var empty = shafts[1];
        string? suggestion = AutomaticAssignmentPolicy.Suggest(
            empty, shafts, articles, new OperationSettings { Hysteresis = 0.25 }, AssignmentMode.Automatic, now: 100,
            id => id == "FAST" ? 800 : 1, reassignmentsInLastHour: 0);
        Assert.Equal("FAST", suggestion);
    }

    [Fact]
    public void Manual_mode_does_not_reassign()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        string? suggestion = AutomaticAssignmentPolicy.Suggest(
            shafts[1], shafts, articles, new OperationSettings(), AssignmentMode.Manual, now: 100,
            id => 800, reassignmentsInLastHour: 0);
        Assert.Null(suggestion);
    }
}

public class ControllerTests
{
    [Fact]
    public void Controller_refuses_to_reassign_a_shaft_that_still_holds_crates()
    {
        var project = TestWarehouses.TwoShaftWarehouse();
        double now = 0;
        var hardware = new SimulatedHardwareAdapter(() => now, (_, _) => { });
        var controller = new OperationsController(project, hardware, () => now, mode: AssignmentMode.Automatic);
        var shaft = controller.Shafts[0];
        shaft.Count = 6;
        Assert.False(controller.TryReassignShaft(shaft.Address, 30));
        Assert.Equal(shaft.ArticleId, controller.Shafts[0].ArticleId);
    }

    [Fact]
    public void Failed_hardware_job_does_not_invent_a_pull()
    {
        var project = TestWarehouses.TwoShaftWarehouse();
        double now = 0;
        var hardware = new ScriptedHardware(() => now);
        var controller = new OperationsController(project, hardware, () => now);
        controller.OnShaftCountReported(controller.Shafts[0].Address, 40, 5);
        now = 5;
        Assert.NotEmpty(hardware.Jobs);
        hardware.Raise(new JobResultEvent
        {
            JobId = hardware.Jobs[0].JobId,
            Stage = HardwareStage.Failed,
            FailureReason = "Sensor fault."
        });
        Assert.Equal(0, controller.MeasuredPallets);
        Assert.Contains(controller.Events, line => line.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ScriptedHardware : IWarehouseHardware
{
    private readonly Func<double> _now;

    public ScriptedHardware(Func<double> now) => _now = now;

    public List<PullJobCommand> Jobs { get; } = new();

    public event EventHandler<ShaftObservation>? ShaftCountChanged;

    public event EventHandler<ShuttleObservation>? ShuttleStatusChanged;

    public event EventHandler<JobResultEvent>? JobCompleted;

    public void SendPullJob(PullJobCommand command) => Jobs.Add(command);

    public void Raise(JobResultEvent result) => JobCompleted?.Invoke(this, result);

    public void Touch() 
    {
        _ = _now();
        ShaftCountChanged?.Invoke(this, new ShaftObservation { Address = new ShaftAddress(1, 1, 1), Count = 0 });
        ShuttleStatusChanged?.Invoke(this, new ShuttleObservation
        {
            Shuttle = ShuttleId.Shuttle1,
            XMeters = 0,
            YMeters = 0,
            Level = 1,
            Phase = "Idle"
        });
    }
}
