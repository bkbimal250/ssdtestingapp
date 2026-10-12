using System.Windows;
using System.Windows.Controls;
namespace pixinit.Views.Benchmark;
public partial class BenchmarkView : UserControl
{
    public BenchmarkView() => InitializeComponent();
    private void LayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PerformanceCards is null || ContextCards is null) return;
        PerformanceCards.Columns = Math.Clamp((int)(ActualWidth / 230), 1, 5);
        ContextCards.Columns = ActualWidth < 850 ? 1 : 2;
    }
}
