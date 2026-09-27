using System.Windows;
using System.Windows.Controls;
namespace pixinit.Views.Nvme;
public partial class NvmeView : UserControl
{
    public NvmeView() => InitializeComponent();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Metrics.Columns = ActualWidth < 1000 ? 2 : 4;
        Sections.Columns = ActualWidth < 850 ? 1 : 2;
    }
}
