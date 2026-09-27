using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using pixinit.Application.Discovery;
using pixinit.Application.Selection;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;

internal static partial class Program
{
    private static int passed;
    private static StorageDevice Drive(string id, StorageProtocol protocol, bool system = false) =>
        new(id, id, "Test serial", protocol, ConnectionBus.Unknown, DiagnosticCapabilities.Identity, DeviceAccess.Limited, system);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine($"PASS {++passed}: {name}");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            SelectionTests();
            StateTests().GetAwaiter().GetResult();
            DiscoveryTests();
            GenerationTests().GetAwaiter().GetResult();
            SataParserAndPolicyTests();
            SataIntegrationTests().GetAwaiter().GetResult();
            NvmeParserAndPolicyTests();
            NvmeIntegrationTests().GetAwaiter().GetResult();
            Phase5Tests().GetAwaiter().GetResult();
            if (args.Contains("--hardware")) HardwareTests();
            else if (args.Contains("--ui")) UiTests();
            Console.WriteLine($"All {passed} checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void SelectionTests()
    {
        var sata = Drive("s1", StorageProtocol.Sata);
        var nvme = Drive("n1", StorageProtocol.Nvme);
        var state = new DeviceSelection();
        state.Apply([nvme], state.BeginDiscovery());
        Check(state.ActiveProtocol == StorageProtocol.Nvme, "NVMe-only discovery selects NVMe");
        state = new(); state.Apply([sata], state.BeginDiscovery());
        Check(state.ActiveProtocol == StorageProtocol.Sata, "SATA-only discovery selects SATA");
        state = new(); state.Apply([nvme with { IsUnambiguousSystemDisk = true }, sata], state.BeginDiscovery());
        Check(state.ActiveProtocol == StorageProtocol.Nvme && state.SataId == "s1", "Unambiguous system drive preferred; both protocol lists retain selection");
        state = new(); state.Apply([nvme with { IsUnambiguousSystemDisk = true }, sata with { IsUnambiguousSystemDisk = true }], state.BeginDiscovery());
        Check(state.ActiveProtocol == StorageProtocol.Sata, "Ambiguous system mapping uses SATA-first fallback");
        state = new(); state.Apply([Drive("z", StorageProtocol.Sata), sata, nvme], state.BeginDiscovery());
        Check(state.SataId == "s1", "Fallback orders stable IDs ordinally");
        state.SelectDevice(StorageProtocol.Sata, "z"); state.SelectTab(StorageProtocol.Nvme);
        state.Apply([sata, nvme, Drive("z", StorageProtocol.Sata)], state.BeginDiscovery());
        Check(state.SataId == "z" && state.NvmeId == "n1" && state.ActiveProtocol == StorageProtocol.Nvme, "Rescan preserves independent selections and active tab");
        var token = state.BeginDiscovery(); state.SelectTab(StorageProtocol.Sata); state.SelectDevice(StorageProtocol.Sata, "s1");
        state.Apply([sata, nvme], token);
        Check(state.ActiveProtocol == StorageProtocol.Sata && state.SataId == "s1", "User selection during discovery wins");
        state.Apply([nvme], state.BeginDiscovery());
        Check(state.SataId is null && state.ActiveProtocol == StorageProtocol.Sata, "Disconnected selection clears while explicitly chosen tab remains");
        state = new(); token = state.BeginDiscovery(); state.SelectTab(StorageProtocol.Nvme); state.Apply([sata], token);
        Check(state.ActiveProtocol == StorageProtocol.Nvme, "User-selected empty tab is not overridden by discovery");
        state = new(); state.Apply([Drive("usb", StorageProtocol.Unknown)], state.BeginDiscovery());
        Check(state.SataId is null && state.NvmeId is null, "Unknown protocol never enters SATA or NVMe selection");
        var zero = new Metric<int>(0, "errors", Availability.Available, "test", DateTimeOffset.UtcNow);
        Check(zero.Value == 0 && Metric<int>.Missing().Value is null, "Available zero differs from missing data");
        bool rejected = false;
        try { _ = new Metric<int>(0, "", Availability.AccessDenied); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Unavailable readings reject fabricated values");
    }
    private static async Task StateTests()
    {
        var provider = new ControlledDiscovery();
        var shell = new ShellViewModel(new DiscoveryCoordinator(provider));
        var pending = shell.ScanAsync();
        Check(!shell.ScanCommand.CanExecute(null), "Concurrent scan command disabled");
        shell.ActiveTab = 1;
        provider.Completion.SetResult([Drive("s1", StorageProtocol.Sata), Drive("s2", StorageProtocol.Sata), Drive("n1", StorageProtocol.Nvme)]);
        await pending;
        Check(shell.ActiveTab == 1 && shell.State == ScanState.Completed, "Asynchronous completion preserves user tab choice");
        var reading = new SataDiagnostics("s1", Metric<bool>.Missing(), new(42, "°C", Availability.Available, "test", DateTimeOffset.UtcNow), Metric<double>.Missing(), [], null, null, null);
        shell.Sata.Apply(reading);
        Check(shell.Sata.Temperature.Contains("42"), "Matching device accepts provider result");
        shell.SelectedSata = shell.SataDevices[1];
        shell.Sata.Apply(reading);
        Check(shell.Sata.Temperature == "Unavailable", "Device switch clears readings and rejects late results from previous device");
        provider.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = shell.ScanAsync(); shell.Cancel(); await pending;
        Check(shell.State == ScanState.Cancelled && shell.SataDevices.Count == 2, "Cancellation retains previous list and reports cancellation");
        provider.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = shell.ScanAsync(); provider.Completion.SetException(new IOException("Test discovery failure")); await pending;
        Check(shell.State == ScanState.Failed && shell.ScanCommand.CanExecute(null), "Discovery error reported and retry enabled");
        var unavailable = new ShellViewModel(new DiscoveryCoordinator(new UnavailableDiscovery()));
        Check(!unavailable.ScanCommand.CanExecute(null) && unavailable.SataEmpty && unavailable.NvmeEmpty, "Unavailable backend stays empty with scan disabled");
    }
    private static void UiTests()
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/pixinit;component/Resources/Themes/Light.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/pixinit;component/Resources/Styles/Controls.xaml", UriKind.Relative) });
        var vm = new ShellViewModel(new DiscoveryCoordinator(new UnavailableDiscovery()));
        var window = new pixinit.MainWindow(vm);
        window.Show(); Pump();
        Check(window.IsVisible, "Actual WPF window starts");
        Console.WriteLine($"Host DPI: {VisualTreeHelper.GetDpi(window).PixelsPerInchX}; work area: {SystemParameters.WorkArea}");
        var tabs = Find<TabControl>(window).Single();
        Check(tabs.Items.Count == 2, "Both empty protocol tabs remain visible");
        window.Width = 1280; window.Height = 700; Pump();
        Capture(window, "sata-laptop.png");
        tabs.SelectedIndex = 1; Pump();
        Check(vm.ActiveTab == 1 && Find<pixinit.Views.Nvme.NvmeView>(window).Any(), "NVMe tab switches actual content");
        Capture(window, "nvme-laptop.png");
        var tab = (TabItem)tabs.Items[0];
        tab.Focus(); Pump();
        Check(tab.IsKeyboardFocused, "Protocol tab accepts keyboard focus");
        tab.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); Pump();
        Check(Keyboard.FocusedElement is not null, "Keyboard focus traversal remains available");
        tabs.SelectedIndex = 0; window.Width = 1800; window.Height = 1000; Pump();
        Capture(window, "sata-desktop.png");
        tabs.SelectedIndex = 1; Pump(); Capture(window, "nvme-desktop.png");
        window.Width = 760; window.Height = 640; Pump();
        Check(window.ActualWidth == 760 && window.ActualHeight == 640, "Practical minimum window size runs");
        tabs.SelectedIndex = 0; Pump(); Capture(window, "sata-minimum.png");
        vm.Sata.SetDevice(Drive("test-table", StorageProtocol.Sata));
        vm.Sata.Apply(new SataDiagnostics("test-table", Metric<bool>.Missing(), Metric<double>.Missing(), Metric<double>.Missing(),
            Enumerable.Range(0, 2000).Select(i => new SataSmartAttribute((byte)(i % 256), $"Test attribute {i}", null, null, null, "Unavailable", "Unavailable", "Test fixture only")).ToArray(), null, null, null));
        Pump();
        var table = Find<DataGrid>(window).Single();
        Check(table.ActualHeight > 100 && Find<DataGridRow>(table).Count() is > 0 and < 100, "SMART table keeps a bounded, virtualized viewport at minimum size");
        tabs.SelectedIndex = 1; Pump();
        vm.Nvme.SetDevice(Drive("test-long", StorageProtocol.Nvme) with { Model = new string('M', 240), Serial = new string('S', 240) });
        Pump(); Capture(window, "test-only-long-identity.png");
        Check(Find<pixinit.Views.Nvme.NvmeView>(window).Single().ActualWidth < 760, "Long test identity stays inside minimum-width view");
        window.Close(); app.Shutdown();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
    private static void Capture(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        Directory.CreateDirectory("docs/screenshots");
        using var stream = File.Create(Path.Combine("docs/screenshots", name));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        Console.WriteLine($"Captured actual running WPF client: {name}, {content.ActualWidth} × {content.ActualHeight} DIPs");
    }
    private sealed class ControlledDiscovery : IDeviceDiscovery
    {
        public bool IsAvailable => true;
        public string UnavailableReason => "";
        public TaskCompletionSource<IReadOnlyList<StorageDevice>> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken cancellationToken) => Completion.Task.WaitAsync(cancellationToken);
    }
}
