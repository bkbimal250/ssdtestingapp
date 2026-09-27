using System.Windows;
using System.Windows.Controls;
namespace pixinit.Views.Sata;
public partial class SataView : UserControl
{
    public SataView() => InitializeComponent();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = ActualWidth < 1050;
        // Explicitly bound the table viewport even inside the section scroller.
        // This preserves virtualization and keeps details reachable on short displays.
        DetailsGrid.Height = narrow ? 540 : Math.Max(260, ActualHeight - 210);
        DetailColumn.Width = new GridLength(narrow ? 0 : 310);
        DetailRow.Height = narrow ? new GridLength(0.65, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(DetailPanel, narrow ? 0 : 1);
        Grid.SetRow(DetailPanel, narrow ? 1 : 0);
        DetailPanel.Margin = narrow ? new Thickness(0, 10, 10, 0) : new Thickness(0);
    }
}
