using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Abstractions;
using ShaftStockDistributor.Optimizer;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IProjectStore _store;
    private readonly IFileDialogs _dialogs;
    private readonly LiveDemoViewModel _live;
    private readonly ConfigurationLock _lock;

    public ShellViewModel(
        LiveDemoViewModel live,
        LayoutEditorViewModel layout,
        ArticlesViewModel articles,
        OptimizerViewModel optimizer,
        StudyViewModel study,
        OperationViewModel operation,
        IMessenger messenger,
        IProjectStore store,
        IFileDialogs dialogs,
        ConfigurationLock configurationLock)
    {
        Live = live;
        Layout = layout;
        Articles = articles;
        Optimizer = optimizer;
        Study = study;
        Operation = operation;
        _live = live;
        _lock = configurationLock;
        _messenger = messenger;
        _store = store;
        _dialogs = dialogs;
        CurrentPage = live;
        messenger.Register<ProjectChangedMessage>(this, (_, message) => Note(message.Project));
        _live.PropertyChanged += OnLivePropertyChanged;
        _lock.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsConfigurationLocked));
        Adopt(PrepareExample(store));
    }

    public bool IsConfigurationLocked => _lock.IsConfigurationLocked;

    public LiveDemoViewModel Live { get; }

    public LayoutEditorViewModel Layout { get; }

    public ArticlesViewModel Articles { get; }

    public OptimizerViewModel Optimizer { get; }

    public StudyViewModel Study { get; }

    public OperationViewModel Operation { get; }

    public ObservableCollection<string> Issues { get; } = new();

    [ObservableProperty] private ObservableObject _currentPage;
    [ObservableProperty] private DistributorProject _project = new();
    [ObservableProperty] private string _page = "Live";
    [ObservableProperty] private string _status = UiText.StatusReady;
    [ObservableProperty] private bool _isExample;
    [ObservableProperty] private string _modeText = UiText.ModeAutomatic;
    [ObservableProperty] private bool _modeConfirmationVisible;
    [ObservableProperty] private bool _loadExampleConfirmationVisible;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private string _issueSummary = UiText.NoIssues;

    private string _cleanSnapshot = "";

    public bool IsLiveSelected => Page == "Live";
    public bool IsLayoutSelected => Page == "Layout";
    public bool IsArticlesSelected => Page == "Articles";
    public bool IsOptimizerSelected => Page == "Optimizer";
    public bool IsStudySelected => Page == "Study";
    public bool IsOperationSelected => Page == "Operation";

    public static DistributorProject PrepareExample(IProjectStore store)
    {
        var project = store.CreateExample();
        ProjectGrid.ReplaceAssignments(project, AssignmentBuilder.Constructive(project));
        return project;
    }

    [RelayCommand]
    private void ShowPage(string page)
    {
        Page = page;
        CurrentPage = page switch
        {
            "Layout" => Layout,
            "Articles" => Articles,
            "Optimizer" => Optimizer,
            "Study" => Study,
            "Operation" => Operation,
            _ => Live
        };
        OnPropertyChanged(nameof(IsLiveSelected));
        OnPropertyChanged(nameof(IsLayoutSelected));
        OnPropertyChanged(nameof(IsArticlesSelected));
        OnPropertyChanged(nameof(IsOptimizerSelected));
        OnPropertyChanged(nameof(IsStudySelected));
        OnPropertyChanged(nameof(IsOperationSelected));
    }

    [RelayCommand(CanExecute = nameof(CanStartDemo))]
    private async Task StartDemoAsync(CancellationToken cancellationToken)
    {
        bool repaired = await _live.RepairAssignmentIfNeededAsync(cancellationToken);
        _live.PlayCommand.Execute(null);
        ShowPage("Live");
        _live.Status = repaired ? UiText.ReoptimizedForLayout : UiText.StatusReady;
    }

    [RelayCommand(CanExecute = nameof(CanStopSimulation))]
    private void StopSimulation() => _live.PauseCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanContinueSimulation))]
    private void ContinueSimulation() => _live.ContinueCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanResetSimulation))]
    private void ResetSimulation() => _live.ResetPlaybackCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void LoadExample()
    {
        RefreshDirty();
        if (HasUnsavedChanges)
        {
            LoadExampleConfirmationVisible = true;
            return;
        }

        ApplyExample();
    }

    [RelayCommand]
    private void CancelLoadExample() => LoadExampleConfirmationVisible = false;

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void ConfirmLoadExample()
    {
        LoadExampleConfirmationVisible = false;
        ApplyExample();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void NewProject()
    {
        Adopt(_store.CreateBlank());
        Status = UiText.StatusReady;
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task OpenProjectAsync()
    {
        string? path = await _dialogs.OpenProjectAsync();
        if (path is null)
        {
            return;
        }

        Adopt(_store.Load(path));
        Status = UiText.StatusReady;
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        string? path = await _dialogs.SaveProjectAsync(Project.Name + ".json");
        if (path is null)
        {
            return;
        }

        _store.Save(path, Project);
        MarkClean();
        Status = UiText.Saved;
    }

    [RelayCommand]
    private async Task ExportJsonAsync() => await ExportAsync(ProjectGrid.ToAssignmentJson(Project), "assignment.json", "json");

    [RelayCommand]
    private async Task ExportCsvAsync() => await ExportAsync(ProjectGrid.ToAssignmentCsv(Project), "assignment.csv", "csv");

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void RequestModeChange() => ModeConfirmationVisible = true;

    [RelayCommand]
    private void CancelModeChange() => ModeConfirmationVisible = false;

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void ConfirmModeChange()
    {
        Project.AssignmentMode = Project.AssignmentMode == AssignmentMode.Automatic
            ? AssignmentMode.Manual
            : AssignmentMode.Automatic;
        _live.SetMode(Project.AssignmentMode);
        ModeText = Project.AssignmentMode == AssignmentMode.Automatic ? UiText.ModeAutomatic : UiText.ModeManual;
        ModeConfirmationVisible = false;
        Status = ModeText;
    }

    public void Adopt(DistributorProject project)
    {
        Project = project;
        MarkClean();
        _messenger.Send(new ProjectChangedMessage { Project = project });
    }

    private void ApplyExample()
    {
        Adopt(PrepareExample(_store));
        ShowPage("Live");
        Status = UiText.StatusReady;
    }

    private void MarkClean()
    {
        _cleanSnapshot = ProjectSerializer.Serialize(Project);
        HasUnsavedChanges = false;
    }

    private void RefreshDirty()
    {
        bool dirty = !string.Equals(ProjectSerializer.Serialize(Project), _cleanSnapshot, StringComparison.Ordinal);
        if (dirty != HasUnsavedChanges)
        {
            HasUnsavedChanges = dirty;
        }
    }

    private void Note(DistributorProject project)
    {
        if (!ReferenceEquals(Project, project))
        {
            Project = project;
        }

        IsExample = project.IsExampleData;
        ModeText = project.AssignmentMode == AssignmentMode.Automatic ? UiText.ModeAutomatic : UiText.ModeManual;
        Issues.Clear();
        foreach (var issue in ProjectValidator.Validate(project))
        {
            string prefix = issue.Severity == ValidationSeverity.Error ? "Error" : "Warning";
            Issues.Add(prefix + ": " + UiText.FormatIssue(issue));
        }

        IssueSummary = Issues.Count == 0 ? UiText.NoIssues : Issues.Count + " checks";
        RefreshDirty();
    }

    private bool CanMutate() => !_lock.IsConfigurationLocked;

    private bool CanStartDemo() => !_live.IsBusy;

    private bool CanStopSimulation() => _live.PauseCommand.CanExecute(null);

    private bool CanContinueSimulation() => _live.ContinueCommand.CanExecute(null);

    private bool CanResetSimulation() => _live.ResetPlaybackCommand.CanExecute(null);

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveDemoViewModel.Phase) or nameof(LiveDemoViewModel.IsConfigurationLocked))
        {
            _lock.SetLocked(_live.IsConfigurationLocked);
            StopSimulationCommand.NotifyCanExecuteChanged();
            ContinueSimulationCommand.NotifyCanExecuteChanged();
            ResetSimulationCommand.NotifyCanExecuteChanged();
            NewProjectCommand.NotifyCanExecuteChanged();
            OpenProjectCommand.NotifyCanExecuteChanged();
            LoadExampleCommand.NotifyCanExecuteChanged();
            ConfirmLoadExampleCommand.NotifyCanExecuteChanged();
            RequestModeChangeCommand.NotifyCanExecuteChanged();
            ConfirmModeChangeCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(LiveDemoViewModel.IsBusy))
        {
            StartDemoCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(LiveDemoViewModel.Status))
        {
            Status = _live.Status;
        }
    }

    private async Task ExportAsync(string contents, string name, string extension)
    {
        string? path = await _dialogs.SaveTextAsync(name, extension);
        if (path is null)
        {
            return;
        }

        File.WriteAllText(path, contents);
        Status = UiText.Exported;
    }
}
