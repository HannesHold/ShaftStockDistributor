using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
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
        Color = article.ColorHex;
    }

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

        Draft.Write(Selected);
        Status = UiText.Saved;
        Publish();
    }

    private void Load(DistributorProject project)
    {
        _project = project;
        Rows.Clear();
        foreach (var article in project.Articles)
        {
            Rows.Add(article);
        }

        Selected = Rows.FirstOrDefault();
    }

    private void Publish()
    {
        if (_project is not null)
        {
            _messenger.Send(new ProjectChangedMessage { Project = _project });
        }
    }
}
