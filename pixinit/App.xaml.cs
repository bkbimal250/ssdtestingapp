using System.Windows;
using pixinit.Application.Discovery;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.ViewModels.Shell;
using pixinit.Application.Scanning;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.Infrastructure.Windows.Nvme;
namespace pixinit;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("Run as administrator required for SSD detection on Windows 11", "PIXINIT", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }
        var operations = new StorageOperationGate();
        MainWindow = new MainWindow(new ShellViewModel(new DiscoveryCoordinator(new WindowsDiskDiscovery(), operations),
            new SataOperationCoordinator(new WindowsSataDiagnosticsProvider(), operations), new NvmeOperationCoordinator(new WindowsNvmeDiagnosticsProvider(), operations), operationGate: operations));
        MainWindow.Show();
    }
}

