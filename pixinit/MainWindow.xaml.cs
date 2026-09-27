using System.Windows;
using pixinit.ViewModels.Shell;
namespace pixinit;
public partial class MainWindow : Window
{
    private bool closingAfterCleanup;
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        ContentRendered += async (_, _) => await viewModel.StartInitialScanAsync();
        Closing += async (_, args) =>
        {
            if (closingAfterCleanup || !viewModel.Benchmark.Busy) return;
            args.Cancel = true;
            await viewModel.CloseAsync();
            closingAfterCleanup = true;
            Close();
        };
        Closed += (_, _) => viewModel.Close();
    }
}
