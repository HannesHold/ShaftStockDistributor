using Avalonia.Controls;
using ShaftStockDistributor.Desktop.Controls;

namespace ShaftStockDistributor.Desktop.Views;

public partial class StudyView : UserControl
{
    public StudyView()
    {
        InitializeComponent();
        ChartTheme.Apply(StudyChart);
    }
}
