using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class ArticleDraft : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessageResourceName = nameof(UiText.Validation_IdRequired), ErrorMessageResourceType = typeof(UiText))]
    private string _id = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessageResourceName = nameof(UiText.Validation_NameRequired), ErrorMessageResourceType = typeof(UiText))]
    private string _name = "";

    [ObservableProperty] private AbcClass _class = AbcClass.B;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10000, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private double _length = 400;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10000, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private double _width = 300;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10000, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private double _height = 280;

    [ObservableProperty] private FlowAxis _flow = FlowAxis.Length;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10000, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private int _cratesPerPallet = 40;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 10000, ErrorMessageResourceName = nameof(UiText.Validation_NonNegative), ErrorMessageResourceType = typeof(UiText))]
    private int _offset = 4;

    [ObservableProperty] private double _classWeight = 1;
    [ObservableProperty] private double _inflow;
    [ObservableProperty] private string _color = "#4C8DFF";

    /// <summary>Inflow last loaded from the article. A grid edit can change the row without touching this draft.</summary>
    public double BaselineInflow { get; private set; }

    public void Load(Article article)
    {
        Id = article.Id;
        Name = article.Name;
        Class = article.Class;
        Length = article.Dimensions.LengthMillimeters;
        Width = article.Dimensions.WidthMillimeters;
        Height = article.Dimensions.HeightMillimeters;
        Flow = article.Dimensions.FlowAxis;
        CratesPerPallet = article.CratesPerPallet;
        Offset = article.Offset;
        ClassWeight = article.ClassWeight;
        Inflow = article.InflowCratesPerHour;
        BaselineInflow = Inflow;
        Color = article.ColorHex;
    }

    public void AcceptInflowBaseline() => BaselineInflow = Inflow;

    public void Validate() => ValidateAllProperties();

    public void Write(Article article)
    {
        article.Id = Id.Trim();
        article.Name = Name.Trim();
        article.Class = Class;
        article.Dimensions.LengthMillimeters = Length;
        article.Dimensions.WidthMillimeters = Width;
        article.Dimensions.HeightMillimeters = Height;
        article.Dimensions.FlowAxis = Flow;
        article.CratesPerPallet = CratesPerPallet;
        article.Offset = Offset;
        article.ClassWeight = ClassWeight;
        article.InflowCratesPerHour = Inflow;
        article.ColorHex = string.IsNullOrWhiteSpace(Color) ? "#4C8DFF" : Color.Trim();
    }
}

public partial class ArticlesViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private DistributorProject? _project;

    public ArticlesViewModel(IMessenger messenger)
    {
        _messenger = messenger;
        messenger.Register<ProjectChangedMessage>(this, (_, message) => Load(message.Project));
    }

    public ObservableCollection<Article> Rows { get; } = new();

    public ArticleDraft Draft { get; } = new();

    public AbcClass[] Classes { get; } = Enum.GetValues<AbcClass>();

    public FlowAxis[] FlowAxes { get; } = Enum.GetValues<FlowAxis>();

    [ObservableProperty] private Article? _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _generateCount = 100;
    [ObservableProperty] private double _percentA = 20;
    [ObservableProperty] private double _percentB = 30;
    [ObservableProperty] private double _percentC = 50;
    [ObservableProperty] private bool _replaceExisting;
    [ObservableProperty] private string _seedText = "";
    [ObservableProperty] private bool _replaceConfirmationVisible;

    partial void OnSelectedChanged(Article? value)
    {
        if (value is not null)
        {
            Draft.Load(value);
        }
    }

    [RelayCommand]
    private void Add()
    {
        if (_project is null)
        {
            return;
        }

        var article = new Article
        {
            Id = "NEW-" + (_project.Articles.Count + 1),
            Name = "New crate",
            Class = AbcClass.B
        };
        _project.Articles.Add(article);
        Rows.Add(article);
        Selected = article;
        Publish();
    }

    [RelayCommand]
    private void Remove()
    {
        if (_project is null || Selected is null)
        {
            return;
        }

        string id = Selected.Id;
        _project.Articles.Remove(Selected);
        foreach (var slot in _project.Assignments.Where(slot => slot.ArticleId == id))
        {
            slot.ArticleId = null;
        }

        Rows.Remove(Selected);
        Selected = Rows.FirstOrDefault();
        Publish();
    }

    [RelayCommand]
    private void ApplyDraft()
    {
        if (Selected is null)
        {
            return;
        }

        Draft.Validate();
        if (Draft.HasErrors)
        {
            Status = Draft.GetErrors().Cast<ValidationResult>().FirstOrDefault()?.ErrorMessage ?? UiText.Validation_Positive;
            return;
        }

        bool formEditedInflow = Math.Abs(Draft.Inflow - Draft.BaselineInflow) > 1e-6;
        if (!formEditedInflow)
        {
            Draft.Inflow = Selected.InflowCratesPerHour;
        }

        string name = string.IsNullOrWhiteSpace(Draft.Name) ? Selected.Name : Draft.Name.Trim();
        Draft.Write(Selected);
        bool inflowChanged = Math.Abs(Selected.InflowCratesPerHour - Draft.BaselineInflow) > 1e-6;
        Draft.AcceptInflowBaseline();
        Status = inflowChanged
            ? string.Format(CultureInfo.InvariantCulture, UiText.InflowUpdated, name)
            : string.Format(CultureInfo.InvariantCulture, UiText.ArticleUpdated, name);
        Publish();
    }

    [RelayCommand]
    private void RequestGenerate()
    {
        if (!TryCreateRequest(out var request))
        {
            ReplaceConfirmationVisible = false;
            return;
        }

        if (ReplaceExisting && _project!.Articles.Count > 0)
        {
            ReplaceConfirmationVisible = true;
            return;
        }

        ReplaceConfirmationVisible = false;
        CommitGenerate(request);
    }

    [RelayCommand]
    private void ConfirmGenerate()
    {
        ReplaceConfirmationVisible = false;
        if (!TryCreateRequest(out var request))
        {
            return;
        }

        CommitGenerate(request with { ReplaceExisting = true });
    }

    [RelayCommand]
    private void CancelGenerate() => ReplaceConfirmationVisible = false;

    private bool TryCreateRequest(out ArticleGenerationRequest request)
    {
        request = new ArticleGenerationRequest();
        if (_project is null)
        {
            return false;
        }

        int? seed = null;
        if (!string.IsNullOrWhiteSpace(SeedText))
        {
            if (!int.TryParse(SeedText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                Status = UiText.GeneratorSeed;
                return false;
            }

            seed = parsed;
        }

        if (_project.CrateTypes.Count == 0)
        {
            _project.CrateTypes = CrateTypeCatalog.CreateStandard();
        }

        double total = _project.Scenarios
            .FirstOrDefault(scenario => scenario.Name == _project.ActiveScenarioName)?.TotalCratesPerHour
            ?? _project.Scenarios.FirstOrDefault()?.TotalCratesPerHour
            ?? 1800;
        request = new ArticleGenerationRequest
        {
            Count = GenerateCount,
            PercentA = PercentA,
            PercentB = PercentB,
            PercentC = PercentC,
            ReplaceExisting = ReplaceExisting,
            Seed = seed,
            TotalCratesPerHour = total,
            CrateTypes = _project.CrateTypes
        };
        var error = ArticleGenerator.Validate(request);
        if (error != ArticleGenerationError.None)
        {
            Status = GeneratorStatus(error);
            return false;
        }

        return true;
    }

    private void CommitGenerate(ArticleGenerationRequest request)
    {
        if (_project is null)
        {
            return;
        }

        var result = ArticleGenerator.Apply(_project, request);
        if (!result.Succeeded)
        {
            Status = GeneratorStatus(result.Error);
            return;
        }

        Load(_project);
        Status = string.Format(
            CultureInfo.InvariantCulture,
            UiText.GeneratedArticles,
            result.Articles.Count,
            result.CountA,
            result.CountB,
            result.CountC);
        Publish();
    }

    private static string GeneratorStatus(ArticleGenerationError error) => error switch
    {
        ArticleGenerationError.Count => UiText.GeneratorCount,
        ArticleGenerationError.NegativePercent => UiText.GeneratorPercent,
        ArticleGenerationError.PercentSum => UiText.GeneratorPercentSum,
        ArticleGenerationError.NoCrateTypes => UiText.GeneratorNoCrateTypes,
        _ => UiText.StatusReady
    };

    private void Load(DistributorProject project)
    {
        _project = project;
        string? selectedId = Selected?.Id;
        Rows.Clear();
        foreach (var article in project.Articles)
        {
            Rows.Add(article);
        }

        Selected = selectedId is null
            ? Rows.FirstOrDefault()
            : Rows.FirstOrDefault(row => row.Id == selectedId) ?? Rows.FirstOrDefault();
    }

    private void Publish()
    {
        if (_project is not null)
        {
            _messenger.Send(new ProjectChangedMessage { Project = _project });
        }
    }
}
