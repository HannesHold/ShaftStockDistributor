using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ShaftStockDistributor.Desktop.Views;

public partial class LayoutEditorView : UserControl
{
    public LayoutEditorView() => InitializeComponent();

    private void CommitShuttleGrid(object? sender, RoutedEventArgs e) =>
        ShuttleGrid.CommitEdit(DataGridEditingUnit.Row, true);
}