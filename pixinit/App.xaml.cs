using System.Windows;
using pixinit.Application.Discovery;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.ViewModels.Shell;
using pixinit.Application.Scanning;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.Infrastructure.Logging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
namespace pixinit;
public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;
    private bool ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write("AppDomain", args.ExceptionObject);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => { CrashLog.Write("Unobserved task", args.Exception); args.SetObserved(); };
        base.OnStartup(e);
        instanceMutex = new Mutex(false, @"Local\PIXINIT.SingleInstance", out bool firstInstance);
        if (firstInstance || TryAcquireInstanceMutex(instanceMutex))
        {
            ownsInstance = true;
        }
        else
        {
            IntPtr existing = FindWindowW(null, "PIXINIT");
            if (existing != IntPtr.Zero)
            {
                ShowWindow(existing, 9);
                SetForegroundWindow(existing);
                instanceMutex.Dispose();
                instanceMutex = null;
                Shutdown();
                return;
            }
            // A mutex cannot be released by another process. If no PIXINIT window exists after
            // the acquisition timeout, do not block a new UI on an orphaned/background owner.
            instanceMutex.Dispose();
            instanceMutex = null;
        }
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
        MainWindow.Closed += (_, _) => ReleaseInstanceMutex();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseInstanceMutex();
        base.OnExit(e);
    }

    private static bool TryAcquireInstanceMutex(Mutex mutex)
    {
        try { return mutex.WaitOne(TimeSpan.FromSeconds(2)); }
        catch (AbandonedMutexException) { return true; }
    }

    private void ReleaseInstanceMutex()
    {
        if (ownsInstance) try { instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        ownsInstance = false;
        instanceMutex?.Dispose();
        instanceMutex = null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindWindowW(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        CrashLog.Write("Dispatcher", args.Exception);
        args.Handled = args.Exception is not OutOfMemoryException and not AccessViolationException;
    }
}

