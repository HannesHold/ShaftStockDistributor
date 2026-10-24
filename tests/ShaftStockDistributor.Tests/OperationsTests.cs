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

    [Fact]
    public void Four_shuttles_cannot_pass_or_close_inside_the_safety_distance()
    {
        var interlock = new ZoneInterlock(1.5);
        interlock.Hold(new ShuttleId(1), Interval.Point(0));
        interlock.Hold(new ShuttleId(2), Interval.Point(5));
        interlock.Hold(new ShuttleId(3), Interval.Point(10));
        interlock.Hold(new ShuttleId(4), Interval.Point(15));
        Assert.True(interlock.ReservationsAreSafe());
        Assert.True(interlock.Conflicts(new ShuttleId(3), Interval.Span(10, 4)));
        Assert.True(interlock.Conflicts(new ShuttleId(2), Interval.Span(5, 9.2)));
        Assert.False(interlock.Conflicts(new ShuttleId(2), Interval.Span(5, 8)));
        Assert.Throws<InvalidOperationException>(() => interlock.Hold(new ShuttleId(4), Interval.Point(1)));
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
        string? suggestion = Suggest(filled, shafts, articles, _ => Sample(800), now: 1000);
        Assert.Null(suggestion);
        Assert.Equal("SLOW", filled.ArticleId);

        var pinned = shafts[1];
        pinned.Count = 0;
        pinned.Pinned = true;
        suggestion = Suggest(pinned, shafts, articles, id => Sample(id == "FAST" ? 800 : 1), now: 1000);
        Assert.Null(suggestion);
    }

    [Fact]
    public void Surplus_empty_shaft_moves_only_when_the_donor_is_below_plan_and_the_target_is_short()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        var empty = shafts[1];
        string? suggestion = Suggest(
            empty,
            shafts,
            articles,
            id => Sample(id == "FAST" ? 800 : 1),
            now: 1000,
            planned: new Dictionary<string, int>(StringComparer.Ordinal) { ["SLOW"] = 2, ["FAST"] = 0 });
        Assert.Equal("FAST", suggestion);
    }

    [Fact]
    public void On_plan_shaft_is_kept_even_when_another_article_is_busier()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        string? suggestion = Suggest(
            shafts[1],
            shafts,
            articles,
            id => Sample(id == "FAST" ? 800 : 10),
            now: 1000,
            planned: new Dictionary<string, int>(StringComparer.Ordinal) { ["SLOW"] = 2, ["FAST"] = 0 });
        Assert.Null(suggestion);
        Assert.Equal("SLOW", shafts[1].ArticleId);
    }

    [Fact]
    public void Packed_siblings_keep_a_just_emptied_shaft()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        shafts[0].Count = 28;
        string? suggestion = Suggest(
            shafts[1],
            shafts,
            articles,
            id => Sample(id == "FAST" ? 800 : 1),
            now: 1000);
        Assert.Null(suggestion);
    }

    [Fact]
    public void Reassignment_waits_for_the_observation_window()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        var settings = new OperationSettings { ObservationWindowSeconds = 600, Hysteresis = 0.25 };
        string? suggestion = AutomaticAssignmentPolicy.Suggest(
            shafts[1],
            shafts,
            articles,
            settings,
            AssignmentMode.Automatic,
            now: 100,
            id => Sample(id == "FAST" ? 800 : 1),
            reassignmentsInLastHour: 0,
            Planned(),
            Recommend);
        Assert.Null(suggestion);
    }

    [Fact]
    public void Manual_mode_does_not_reassign()
    {
        var (shafts, articles) = TestWarehouses.PolicyFixture();
        string? suggestion = AutomaticAssignmentPolicy.Suggest(
            shafts[1],
            shafts,
            articles,
            new OperationSettings { ObservationWindowSeconds = 0 },
            AssignmentMode.Manual,
            now: 100,
            id => Sample(800),
            reassignmentsInLastHour: 0,
            Planned(),
            Recommend);
        Assert.Null(suggestion);
    }

    private static string? Suggest(
        ShaftRuntime shaft,
        List<ShaftRuntime> shafts,
        Dictionary<string, ArticleRuntime> articles,
        Func<string, FlowSample> observed,
        double now,
        Dictionary<string, int>? planned = null) =>
        AutomaticAssignmentPolicy.Suggest(
            shaft,
            shafts,
            articles,
            new OperationSettings { ObservationWindowSeconds = 0, Hysteresis = 0.25 },
            AssignmentMode.Automatic,
            now,
            observed,
            reassignmentsInLastHour: 0,
            planned ?? Planned(),
            Recommend);

    private static Dictionary<string, int> Planned() =>
        new(StringComparer.Ordinal) { ["SLOW"] = 2, ["FAST"] = 0 };

    private static FlowSample Sample(double rate)
    {
        const double elapsed = 3600;
        int samples = (int)Math.Round(Math.Max(0, rate) * elapsed / 3600.0);
        return new FlowSample(rate, samples, elapsed);
    }

    private static int Recommend(Article article, double rate) =>
        ShaftCountCalculator.Recommend(rate, article.CratesPerPallet, article.Offset, 50, 90);
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
