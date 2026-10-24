using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ShaftStockDistributor.Desktop.Controls;

public static class ChartTheme
{
    public static void Apply(CartesianChart chart)
    {
        var text = new SolidColorPaint(new SKColor(186, 202, 220));
        chart.LegendTextPaint = text;
        chart.LegendBackgroundPaint = new SolidColorPaint(new SKColor(16, 24, 34, 230));
        chart.TooltipTextPaint = new SolidColorPaint(SKColors.White);
        chart.TooltipBackgroundPaint = new SolidColorPaint(new SKColor(28, 40, 56));
    }
}
