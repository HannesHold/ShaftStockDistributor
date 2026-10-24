using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ShaftStockDistributor.Desktop.Views;

public partial class ArticlesView : UserControl
{
    public ArticlesView() => InitializeComponent();

    /// <summary>
    /// A click on Apply moves focus before the command runs, and the grid would otherwise cancel the open cell.
    /// Commit the inflow cell first so the row value is the one Apply keeps.
    /// </summary>
    private void CommitArticleGrid(object? sender, RoutedEventArgs e) =>
        ArticleGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
