using System.Windows;
using System.Windows.Interop;
using pixinit.ViewModels.Shell;
using pixinit.Infrastructure.Logging;
namespace pixinit;
public partial class MainWindow : Window
{
    private bool closingAfterCleanup;
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _ = viewModel.EnableAutomaticDiagnosticsAsync();
        ContentRendered += async (_, _) =>
        {
            try { await viewModel.StartInitialScanAsync(); await viewModel.RefreshPickerAsync(); SyncDeviceSelection(); }
            catch (Exception ex) { CrashLog.Write("Initial scan", ex); }
        };
        Closing += async (_, args) =>
        {
            if (closingAfterCleanup || (!viewModel.Benchmark.Busy && !viewModel.Benchmark.PreparingDiagnostics && !viewModel.SataBusy && !viewModel.NvmeBusy)) return;
            args.Cancel = true;
            try { await viewModel.CloseAsync(); }
            catch (Exception ex) { CrashLog.Write("Window close cleanup", ex); }
            finally { closingAfterCleanup = true; Close(); }
        };
        Closed += (_, _) =>
        {
            try { viewModel.Close(); }
            catch (Exception ex) { CrashLog.Write("Window closed cleanup", ex); }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(OnDeviceChange);
    }

    private System.Windows.Threading.DispatcherTimer? deviceChangeTimer;

    private IntPtr OnDeviceChange(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int deviceChange = 0x0219;
        nint kind = wParam;
        if (message == deviceChange && kind is 0x8000 or 0x8004)
        {
            deviceChangeTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            deviceChangeTimer.Stop();
            deviceChangeTimer.Tick -= RescanAfterDeviceChange;
            deviceChangeTimer.Tick += RescanAfterDeviceChange;
            deviceChangeTimer.Start();
        }
        return IntPtr.Zero;
    }

    private async void RescanAfterDeviceChange(object? sender, EventArgs e)
    {
        deviceChangeTimer?.Stop();
        if (DataContext is not ShellViewModel viewModel || !viewModel.ScanCommand.CanExecute(null)) return;
        try { await viewModel.ScanAsync(); }
        catch (Exception ex) { CrashLog.Write("Device change rescan", ex); }
    }

    private void SyncDeviceSelection()
    {
        DiskPicker.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedItemProperty)?.UpdateTarget();
    }

    private async void PhysicalDiskPicker_Opened(object? sender, EventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        try { await vm.RefreshPickerAsync(); }
        catch (Exception ex) { CrashLog.Write("Physical disk picker refresh", ex); }
    }

    private void ShowBenchmarkTab(object sender, RoutedEventArgs e) => RightTabs.SelectedIndex = 0;

    private void CopyField(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string text } && text.Length > 0)
            System.Windows.Clipboard.SetText(text);
    }

}
