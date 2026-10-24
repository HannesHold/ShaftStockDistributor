using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class OperationViewModel : ObservableObject
{
    public OperationViewModel(IMessenger messenger)
    {
        messenger.Register<SimulationUpdatedMessage>(this, (_, message) => Apply(message.Snapshot));
    }

    public ObservableCollection<JobVisual> Jobs { get; } = new();

    public ObservableCollection<string> Events { get; } = new();

    public ObservableCollection<ArticleFlowVisual> Flows { get; } = new();

    [ObservableProperty] private string _shuttle1 = UiText.Shuttle1;
    [ObservableProperty] private string _shuttle2 = UiText.Shuttle2;
    [ObservableProperty] private string _lockText = "";
    [ObservableProperty] private bool _sharedLocked;
    [ObservableProperty] private string _modeHint = UiText.ModeConfirmBody;

    private void Apply(WarehouseSnapshot snapshot)
    {
        Jobs.Clear();
        foreach (var job in snapshot.Jobs)
        {
            Jobs.Add(job);
        }

        Events.Clear();
        foreach (var line in snapshot.Events)
        {
            Events.Add(line);
        }

        Flows.Clear();
        foreach (var flow in snapshot.ArticleFlows)
        {
            Flows.Add(flow);
        }

        var first = snapshot.Shuttles.FirstOrDefault(shuttle => shuttle.Id == Core.ShuttleId.Shuttle1);
        var second = snapshot.Shuttles.FirstOrDefault(shuttle => shuttle.Id == Core.ShuttleId.Shuttle2);
        Shuttle1 = first is null ? UiText.Shuttle1 : $"{UiText.Shuttle1}: {first.Phase} · L{first.Level}";
        Shuttle2 = second is null ? UiText.Shuttle2 : $"{UiText.Shuttle2}: {second.Phase} · L{second.Level}";
        SharedLocked = snapshot.SharedZoneLocked || snapshot.AnyShuttleBlocked;
        LockText = snapshot.AnyShuttleBlocked ? UiText.Waiting : snapshot.SharedZoneLocked ? UiText.SharedLock : "";
    }
}
