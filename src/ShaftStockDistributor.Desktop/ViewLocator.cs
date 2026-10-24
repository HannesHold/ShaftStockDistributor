using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShaftStockDistributor.Desktop;

public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock();
        }

        var name = param.GetType().FullName!
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = typeof(ViewLocator).Assembly.GetType(name);
        if (type is null)
        {
            return new TextBlock { Text = "Missing view for " + name };
        }

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ObservableObject;
}
