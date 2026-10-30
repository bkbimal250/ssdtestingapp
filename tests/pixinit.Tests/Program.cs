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
            if (args.Contains("--no-persistence-only")) { NoPersistenceTests().GetAwaiter().GetResult(); Console.WriteLine($"All {passed} no-persistence checks passed."); return 0; }
            if (args.Contains("--revamp")) { BenchmarkRevampTests().GetAwaiter().GetResult(); Console.WriteLine($"All {passed} benchmark presentation checks passed."); return 0; }
            if (args.Contains("--ui-only")) { UiTests(); Console.WriteLine($"All {passed} WPF UI checks passed."); return 0; }
            if (args.Contains("--discovery-only")) { foreach (var d in new WindowsDiskDiscovery().DiscoverAsync(default).GetAwaiter().GetResult()) Console.WriteLine($"{d.Model}: {d.PcieLinkDisplay}; source: {d.PcieLinkSource ?? "Not reported"}"); return 0; }
            NoPersistenceTests().GetAwaiter().GetResult();
            SelectionTests();
            StateTests().GetAwaiter().GetResult();
            DiscoveryTests();
            GenerationTests().GetAwaiter().GetResult();
            SataParserAndPolicyTests();
            SataIntegrationTests().GetAwaiter().GetResult();
            NvmeParserAndPolicyTests();
            NvmeIntegrationTests().GetAwaiter().GetResult();
            Phase5Tests().GetAwaiter().GetResult();
            Phase6Tests().GetAwaiter().GetResult();
            Phase7Tests().GetAwaiter().GetResult();
            StorageSpecsTests().GetAwaiter().GetResult();
            StorageOverviewTests().GetAwaiter().GetResult();
            CompanyWorkflowTests().GetAwaiter().GetResult();
            UnbufferedBenchmarkTests().GetAwaiter().GetResult();
            AccurateResultTests().GetAwaiter().GetResult();
            BenchmarkRevampTests().GetAwaiter().GetResult();
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
        Check(shell.Sata.Serial == "Test serial", "SATA exposes the complete discovered serial");
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
        Check(!Find<Button>(window).Any(b => b.Content?.ToString()?.StartsWith("Export", StringComparison.OrdinalIgnoreCase) == true), "Shell exposes no proof, PDF, QR or JSON export buttons");
        var navigationLists = Find<ListBox>(window).Where(b => b.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path?.Path == "AllDevices").ToArray();
        Check(navigationLists.Length >= 1 && navigationLists.All(b => b.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedItemProperty)?.ParentBinding.Path?.Path == "SelectedDisk"), "Diagnostics sidebar uses the complete physical inventory and shared disk selector");

        Console.WriteLine($"Host DPI: {VisualTreeHelper.GetDpi(window).PixelsPerInchX}; work area: {SystemParameters.WorkArea}");
        var tabs = (TabControl)window.FindName("ProtocolTabs");
        Check(tabs.Items.Count == 2, "Only SATA and NVMe protocol tabs remain; duplicate device overview removed");
        var sataSections = Find<pixinit.Views.Sata.SataView>(window).SelectMany(Find<TabControl>).Single();
        Check(!Find<Button>(window).Any(b => Equals(b.Content, "Capture Idle") || Equals(b.Content, "Capture Load")), "Diagnostic overview has no temperature capture controls");
        Check(sataSections.Items.Count == 3, "SATA retains Overview, SMART, and Details navigation");
        window.Width = 1280; window.Height = 700; Pump();
        Capture(window, "sata-laptop.png");
        tabs.SelectedIndex = 1; Pump();
        Check(vm.ActiveTab == 1 && Find<pixinit.Views.Nvme.NvmeView>(window).Any(), "NVMe tab switches actual content");
        var nvmeSections = Find<pixinit.Views.Nvme.NvmeView>(window).SelectMany(Find<TabControl>).Single();
        Check(nvmeSections.Items.Count == 3, "NVMe provides Overview, SMART, and Details navigation");
        Capture(window, "nvme-laptop.png");
        var tab = (TabItem)tabs.Items[0];
        tab.Focus(); Pump();
        Check(tab.IsKeyboardFocused, "Protocol tab accepts keyboard focus");
        tab.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); Pump();
        Check(Keyboard.FocusedElement is not null, "Keyboard focus traversal remains available");
        var benchmark = (TabItem)window.FindName("BenchmarkTab"); benchmark.IsSelected = true;
        window.Width = 1366; window.Height = 768; Pump(); Capture(window, "phase7-responsive-1366x768.png");
        Check(Find<TextBlock>(window).Any(t => t.Text.StartsWith("Unbuffered file benchmark")), "Benchmark limitation stays visible in its tab");
        var advanced = Find<Expander>(window).Single(e => Equals(e.Header, "Advanced"));
        Check(!advanced.IsExpanded, "Benchmark opens with Advanced collapsed");
        advanced.IsExpanded = true; Pump();
        Check(Find<TextBlock>(window).Count(t => new[] { "Sequential read", "Sequential write", "Random read", "Random write", "Sustained average" }.Contains(t.Text)) == 5 && Find<Button>(window).Any(b => Equals(b.Content, "1024 MiB")), "Five performance cards and segmented file sizes remain reachable at laptop width");
        advanced.IsExpanded = false; Pump();
        Check(!Find<Button>(window).Any(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Capture ") || Equals(b.Content, "Cancel capture")) && Find<TextBlock>(window).Any(t => t.Text == "Temperature"), "Benchmark retains current temperature card without manual capture controls");
        Check(Find<ScrollViewer>(window).Any(v => v.ScrollableHeight > 0) && !Find<Button>(window).Any(b => Equals(b.Content, "Export benchmark JSON")) && !Find<Expander>(window).Any(e => e.Header?.ToString()?.StartsWith("Exact per-pass") == true || e.Header?.ToString()?.StartsWith("Historical benchmarks") == true), "Laptop benchmark remains scrollable without the removed detail/export footer");
        window.Width = 1800; window.Height = 1000; Pump(); Capture(window, "phase7-responsive-desktop.png");
        Check(Find<Button>(window).Where(b => b.IsVisible).All(b => b.ActualWidth > 0 && b.ActualHeight > 0), "Benchmark controls retain accessible size on the desktop layout");
        var benchmarkView = Find<pixinit.Views.Benchmark.BenchmarkView>(window).Single();
        var benchmarkScroll = Find<ScrollViewer>(window).Single(v => v.Content == benchmarkView);
        var cards = (System.Windows.Controls.Primitives.UniformGrid)benchmarkView.FindName("PerformanceCards");
        benchmarkScroll.ScrollToVerticalOffset(cards.TransformToAncestor(benchmarkView).Transform(new Point()).Y); Pump();
        Capture(window, "benchmark-cards-1800x1000.png");
        window.Width = 1366; window.Height = 768; Pump(); Capture(window, "benchmark-cards-1366x768.png");
        window.Width = 760; window.Height = 640; Pump(); Capture(window, "benchmark-minimum.png");
        Check(cards.Columns <= 3 && ((System.Windows.Controls.Primitives.UniformGrid)benchmarkView.FindName("ContextCards")).Columns == 1 && benchmarkScroll.ScrollableHeight > 0, "Benchmark cards adapt and context stacks at the practical minimum width");
        window.Width = 1800; window.Height = 1000; benchmarkScroll.ScrollToTop(); Pump();
        var workspace = (TabControl)window.FindName("WorkspaceTabs");
        Check(!Find<TextBlock>(window).Any(t => t.Text == "Historical diagnostic snapshots") && !Find<Button>(window).Any(b => Equals(b.Content, "Close history")), "History popup and actions are completely removed");
        workspace.SelectedIndex = 0; Pump();
        Check(workspace.Items.Count == 2 && !Find<Expander>(window).Any(e => Equals(e.Header, "Additional Devices · 0")), "Only Diagnostics and Benchmark workspace tabs remain");
        ((TabControl)window.FindName("WorkspaceTabs")).SelectedIndex = 0;
        tabs.SelectedIndex = 1; window.Width = 1366; window.Height = 768; Pump(); Capture(window, "post-phase7-nvme-1366x768.png");
        window.Width = 1800; window.Height = 1000; Pump(); Capture(window, "post-phase7-nvme-desktop.png");
        tabs.SelectedIndex = 0; window.Width = 1800; window.Height = 1000; Pump();
        Capture(window, "sata-desktop.png");
        tabs.SelectedIndex = 1; Pump(); Capture(window, "nvme-desktop.png");
        nvmeSections = Find<pixinit.Views.Nvme.NvmeView>(window).SelectMany(Find<TabControl>).Single();
        nvmeSections.SelectedIndex = 1; Pump(); Capture(window, "diagnostics-nvme-smart-desktop.png");
        Check(Find<DataGrid>(window).Single().Items.Count == 15, "NVMe SMART shows health fields and all ten exact counters without ATA interpretation");
        nvmeSections.SelectedIndex = 2; Pump(); Capture(window, "diagnostics-nvme-details-desktop.png");
        var nvmeView = Find<pixinit.Views.Nvme.NvmeView>(window).Single();
        var detailFixture = new pixinit.ViewModels.Nvme.NvmeViewModel();
        var fixtureDevice = SyntheticNvme(); detailFixture.SetDevice(fixtureDevice);
        var controllerBytes = SyntheticController(); var healthBytes = SyntheticHealth();
        Array.Clear(healthBytes, 200, 16); healthBytes[0] = 0;
        var now = DateTimeOffset.UtcNow;
        var fixtureResult = pixinit.Core.Diagnostics.Nvme.NvmeResultMapper.Map(fixtureDevice,
            pixinit.Core.Diagnostics.Nvme.NvmeControllerParser.Parse(controllerBytes),
            pixinit.Core.Diagnostics.Nvme.NvmeHealthParser.Parse(healthBytes), [],
            [new(pixinit.Core.Diagnostics.Nvme.NvmeOperation.Controller, pixinit.Core.Diagnostics.Nvme.NvmeOutcome.Success, "Synthetic test evidence", controllerBytes, now, "Controller"),
             new(pixinit.Core.Diagnostics.Nvme.NvmeOperation.Health, pixinit.Core.Diagnostics.Nvme.NvmeOutcome.Success, "Synthetic test evidence", healthBytes, now, "Selected physical NVMe device through Windows; controller versus namespace scope is uncertain because no numeric NSID was established")]);
        detailFixture.Apply(fixtureResult); nvmeView.DataContext = detailFixture; Pump();
        nvmeSections.SelectedIndex = 1; window.Width = 1366; window.Height = 768; Pump(); Capture(window, "smart-nvme-1366x768.png");
        window.Width = 1800; window.Height = 1000; Pump(); Capture(window, "smart-nvme-1800x1000.png");
        nvmeSections.SelectedIndex = 2; Pump();
        Check(detailFixture.NotImplementedSensors == "Not Implemented 8" && detailFixture.ReportedSensors == "Reported 0" && detailFixture.ThermalRows[0].Value.Contains("26.85"), "Zero individual sensors remain not implemented while independent composite temperature is visible");
        Check(detailFixture.ExactCounterRows.Count == 8 && detailFixture.ExactCounterRows[0].Value.Length > 20, "Details preserves exact 128-bit usage counter values");
        Check(detailFixture.ControllerRows.Any(r => r.Value == "SYNTHETIC-NVME") && detailFixture.NamespaceDisclosure.Contains("No namespace ID guessed"), "Details retains full Identify serial and unmapped namespace disclosure");
        Check(Find<Expander>((DependencyObject)((TabItem)nvmeSections.Items[2]).Content).All(e => e.IsExpanded), "Every Details expander starts expanded");
        window.Width = 1366; window.Height = 768; Pump(); Capture(window, "details-nvme-1366x768.png");
        window.Width = 1800; window.Height = 1000; Pump(); Capture(window, "details-nvme-1800x1000.png");
        var detailsScroll = (ScrollViewer)((TabItem)nvmeSections.Items[2]).Content;
        var thermalHeading = Find<TextBlock>(nvmeView).First(t => t.Text == "Thermal information");
        detailsScroll.ScrollToVerticalOffset(thermalHeading.TransformToAncestor((Visual)detailsScroll.Content).Transform(new Point()).Y - 12); Pump(); Capture(window, "details-nvme-thermal-desktop.png");
        detailFixture.SetDevice(null); Pump();
        Check(detailFixture.ExactCounterRows.All(r => r.Value.StartsWith("N/A")) && detailFixture.UnavailableSensors == "Unavailable 8", "Details device switch clears counter and thermal evidence without zero filling");
        nvmeSections.SelectedIndex = 0;
        var deviceNavigation = Find<ListBox>(window).Single(list => System.Windows.Automation.AutomationProperties.GetName(list) == "All detected disks in NVMe diagnostics");
        deviceNavigation.ItemsSource = new[] { fixtureDevice }; deviceNavigation.SelectedIndex = 0; Pump();
        detailFixture.SetDevice(fixtureDevice); detailFixture.Apply(fixtureResult); nvmeView.DataContext = detailFixture;
        var emptyNavigationLabel = Find<TextBlock>(window).Single(text => text.Text == "No NVMe devices discovered");
        emptyNavigationLabel.Visibility = Visibility.Collapsed;
        window.Width = 1366; window.Height = 768; Pump(); Capture(window, "diagnostic-layout-laptop.png");
        var identityCard = Find<Border>(nvmeView).First(border => border.Child is DockPanel);
        Check(identityCard.ActualWidth > nvmeView.ActualWidth * .9 && nvmeSections.ActualWidth > nvmeView.ActualWidth * .9, "Diagnostic header and sections consistently fill the laptop content pane");
        var navigationItem = Find<ListBoxItem>(deviceNavigation).Single();
        Check(navigationItem.ActualWidth > deviceNavigation.ActualWidth * .85, "Selected device card fills the navigation rail instead of wrapping one concatenated label");
        window.Width = 1800; window.Height = 1000; Pump(); Capture(window, "diagnostic-layout-desktop.png");
        window.Width = 760; window.Height = 640; Pump(); Capture(window, "diagnostic-layout-minimum.png");
        Check(identityCard.ActualWidth <= nvmeView.ActualWidth && nvmeSections.ActualWidth <= nvmeView.ActualWidth, "Diagnostic header and sections stay bounded at minimum width");
        deviceNavigation.ItemsSource = null; emptyNavigationLabel.ClearValue(UIElement.VisibilityProperty);
        nvmeView.DataContext = vm.Nvme; Pump();
        nvmeSections.SelectedIndex = 2; Pump();
        Check(!Find<TabItem>(window).Any(t => t.Header?.ToString()?.Contains("Raw Data", StringComparison.Ordinal) == true), "Raw Data is removed from diagnostic navigation");
        nvmeSections.SelectedIndex = 0; Pump();
        window.Width = 760; window.Height = 640; Pump();
        Check(window.ActualWidth == 760 && window.ActualHeight == 640, "Practical minimum window size runs");
        tabs.SelectedIndex = 0; Pump(); Capture(window, "sata-minimum.png");
        vm.Sata.SetDevice(Drive("test-table", StorageProtocol.Sata));
        vm.Sata.Apply(new SataDiagnostics("test-table", Metric<bool>.Missing(), Metric<double>.Missing(), Metric<double>.Missing(),
            Enumerable.Range(0, 2000).Select(i => new SataSmartAttribute((byte)(i % 256), $"Test attribute {i}", null, null, null, "Unavailable", "Unavailable", "Test fixture only")).ToArray(), null, null, null));
        Pump();
        sataSections = Find<pixinit.Views.Sata.SataView>(window).SelectMany(Find<TabControl>).Single(); sataSections.SelectedIndex = 1; Pump();
        var table = Find<DataGrid>(window).Single();
        var realizedRows = Find<DataGridRow>(table).Count();
        Console.WriteLine($"SMART viewport: {table.ActualWidth} x {table.ActualHeight}; realized rows: {realizedRows}");
        Check(table.ActualHeight > 100 && realizedRows is > 0 and < 100, "SMART table keeps a bounded, virtualized viewport at minimum size");
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
        var content = (FrameworkElement)window;
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


