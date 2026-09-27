using System.Windows;
using pixinit.Application.Discovery;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.ViewModels.Shell;
using pixinit.Application.Scanning;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.Infrastructure.History;
namespace pixinit;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var operations = new StorageOperationGate();
        MainWindow = new MainWindow(new ShellViewModel(new DiscoveryCoordinator(new WindowsDiskDiscovery(), operations),
            new SataOperationCoordinator(new WindowsSataDiagnosticsProvider(), operations), new NvmeOperationCoordinator(new WindowsNvmeDiagnosticsProvider(), operations), new SqliteHistoryStore()));
        MainWindow.Show();
    }
}

