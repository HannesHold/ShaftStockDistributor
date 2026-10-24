using Avalonia.Controls;
using ShaftStockDistributor.Desktop.Controls;

namespace ShaftStockDistributor.Desktop.Views;

public partial class LiveDemoView : UserControl
{
    public LiveDemoView()
    {
        InitializeComponent();
        ChartTheme.Apply(PalletChart);
        ChartTheme.Apply(UtilChart);
    }
}
