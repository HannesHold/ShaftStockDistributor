using System.Collections.ObjectModel;
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

    public ShellViewModel(
        LiveDemoViewModel live,
        LayoutEditorViewModel layout,
        ArticlesViewModel articles,
        OptimizerViewModel optimizer,
        StudyViewModel study,
        OperationViewModel operation,
        IMessenger messenger,
        IProjectStore store,
        IFileDialogs dialogs)
    {
        Live = live;
        Layout = layout;
        Articles = articles;
        Optimizer = optimizer;
        Study = study;
        Operation = operation;
        _live = live;
        _messenger = messenger;
        _store = store;
        _dialogs = dialogs;
        CurrentPage = live;
        messenger.Register<ProjectChangedMessage>(this, (_, message) => Note(message.Project));
        Adopt(PrepareExample(store));
    }

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
    [ObservableProperty] private string _issueSummary = UiText.NoIssues;

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

    [RelayCommand]
    private async Task StartDemoAsync(CancellationToken cancellationToken)
    {
        var project = await _live.BuildDemoAsync(cancellationToken);
        Adopt(project);
        _live.PlayCommand.Execute(null);
        ShowPage("Live");
        Status = UiText.StatusReady;
    }

    [RelayCommand]
    private void NewProject()
    {
        Adopt(_store.CreateBlank());
        Status = UiText.StatusReady;
    }

    [RelayCommand]
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
        Status = UiText.Saved;
    }

    [RelayCommand]
    private async Task ExportJsonAsync() => await ExportAsync(ProjectGrid.ToAssignmentJson(Project), "assignment.json", "json");

    [RelayCommand]
    private async Task ExportCsvAsync() => await ExportAsync(ProjectGrid.ToAssignmentCsv(Project), "assignment.csv", "csv");

    [RelayCommand]
    private void RequestModeChange() => ModeConfirmationVisible = true;

    [RelayCommand]
    private void CancelModeChange() => ModeConfirmationVisible = false;

    [RelayCommand]
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
        _messenger.Send(new ProjectChangedMessage { Project = project });
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
