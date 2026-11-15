using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using pixinit.Application.Discovery;
using pixinit.Application.Selection;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Benchmark;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);
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
            if (args.Contains("--diagnostics-hardware-only")) { ReadOnlyHardwareDiagnostics().GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--no-persistence-only")) { NoPersistenceTests().GetAwaiter().GetResult(); Console.WriteLine($"All {passed} no-persistence checks passed."); return 0; }
            if (args.Contains("--revamp")) { BenchmarkRevampTests().GetAwaiter().GetResult(); Console.WriteLine($"All {passed} benchmark presentation checks passed."); return 0; }
            if (args.Contains("--dashboard-live-ui")) { UiTests(true); Console.WriteLine($"All {passed} live dashboard checks passed."); return 0; }
            if (args.Contains("--ui-only")) { UiTests(); Console.WriteLine($"All {passed} WPF UI checks passed."); return 0; }
            if (args.Contains("--discovery-only")) { foreach (var d in new WindowsDiskDiscovery().DiscoverAsync(default).GetAwaiter().GetResult()) Console.WriteLine($"{d.Model}: {d.PcieLinkDisplay}; source: {d.PcieLinkSource ?? "Not reported"}"); return 0; }
            NoPersistenceTests().GetAwaiter().GetResult();
            QcTests();
            SelectionTests();
            StateTests().GetAwaiter().GetResult();
            DiscoveryTests();
            GenerationTests().GetAwaiter().GetResult();
            SataParserAndPolicyTests();
            SataHostCounterTests();
            UsbSataTests().GetAwaiter().GetResult();
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
        Check(shell.SataDevices.Count == 2 && shell.SataDevices.All(device => device.Protocol == StorageProtocol.Sata) &&
            shell.NvmeDevices.Count == 1 && shell.NvmeDevices.All(device => device.Protocol == StorageProtocol.Nvme),
            "Protocol lists include every matching disk and exclude disks from the other tab");
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
        var gate = new pixinit.Application.Scanning.StorageOperationGate();
        var automaticDiscovery = new ControlledDiscovery();
        automaticDiscovery.Completion.SetResult([SyntheticNvme(), SyntheticSata()]);
        var blockedNvme = new BlockingNvmeProvider();
        var blockedSata = new BlockingSataProvider();
        var automaticShell = new ShellViewModel(new DiscoveryCoordinator(automaticDiscovery, gate),
            new pixinit.Application.Scanning.SataOperationCoordinator(blockedSata, gate),
            new pixinit.Application.Scanning.NvmeOperationCoordinator(blockedNvme, gate));
        await automaticShell.ScanAsync();
        var firstRead = automaticShell.EnableAutomaticDiagnosticsAsync();
        await blockedNvme.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        automaticShell.SelectedDisk = automaticShell.SataDevices.Single();
        Check(automaticShell.ShowSataDiagnostics && !automaticShell.ShowNvmeDiagnostics,
            "Selecting SATA routes diagnostics away from the previous NVMe drive");
        blockedNvme.Completion.SetResult(new pixinit.Core.Diagnostics.Nvme.NvmeDiagnostics("nvme-a", Metric<byte>.Missing(),
            Metric<double>.Missing(), Metric<double>.Missing(), Metric<double>.Missing(), null, null, null, null));
        await firstRead;
        await blockedSata.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        blockedSata.Completion.SetResult(new SataDiagnostics("sata-a", Metric<bool>.Missing(), Metric<double>.Missing(),
            Metric<double>.Missing(), [], null, null, null));
        await automaticShell.AutomaticDiagnosticsTask;
        Check(automaticShell.Sata.Result?.DeviceId == "sata-a" && automaticShell.Nvme.Result is null,
            "Automatic SATA read publishes only the newly selected drive after old NVMe work finishes");
    }
    private static void UiTests(bool live = false)
    {
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/pixinit;component/Resources/Themes/Light.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/pixinit;component/Resources/Styles/Controls.xaml", UriKind.Relative) });
        var discovered = new ControlledDiscovery();
        discovered.Completion.SetResult([Drive("ui-sata", StorageProtocol.Sata), SyntheticNvme(), Drive("ui-third", StorageProtocol.Sata)]);
        var gate = new pixinit.Application.Scanning.StorageOperationGate();
        var vm = live ? new ShellViewModel(new DiscoveryCoordinator(new WindowsDiskDiscovery(), gate),
            new pixinit.Application.Scanning.SataOperationCoordinator(new pixinit.Infrastructure.Windows.Sata.WindowsSataDiagnosticsProvider(), gate),
            new pixinit.Application.Scanning.NvmeOperationCoordinator(new pixinit.Infrastructure.Windows.Nvme.WindowsNvmeDiagnosticsProvider(), gate), operationGate: gate)
            : new ShellViewModel(new DiscoveryCoordinator(discovered));
        vm.ScanAsync().GetAwaiter().GetResult();
        if (live) vm.RefreshPickerAsync().GetAwaiter().GetResult();
        var window = new pixinit.MainWindow(vm);
        window.Show(); Pump();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        if (live)
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while ((vm.State == pixinit.Core.Diagnostics.Common.ScanState.Running || vm.SataBusy || vm.NvmeBusy || !vm.AutomaticDiagnosticsTask.IsCompleted) && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
            var refresh = vm.RefreshPickerAsync();
            while (!refresh.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
            refresh.GetAwaiter().GetResult(); Pump();
        }
        Check(window.Width == 760 && window.Height == 560, "Window opens at 760 by 560");
        Check(window.MinWidth == 760 && window.MaxWidth == 760, "Window width cannot change");
        Check(window.MinHeight == 560 && window.MaxHeight == 560, "Window height cannot change");
        Check(window.ResizeMode == ResizeMode.CanMinimize && window.WindowState == WindowState.Normal, "Window minimizes without resize or maximize");
        Check(window.WindowStartupLocation == WindowStartupLocation.CenterScreen, "Window requests screen centering");
        nint style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -16);
        Check((style & 0x50000) == 0, "Native maximize and resize styles are disabled");
        var picker = (ComboBox)window.FindName("DiskPicker");
        var root = (Grid)window.FindName("DashboardLayout");
        var viewport = (Grid)window.FindName("DashboardViewport");
        Check(ReferenceEquals(picker.ItemsSource, vm.AllDevices), "Picker uses observable inventory");
        Check(window.FindName("DeviceList") is null && !Find<ListBox>(window).Any(), "No duplicate device sidebar remains");
        Check(picker.Width == 260 && picker.Height == 28, "Header picker is the compact sole selector");
        Check(picker.MaxDropDownHeight == 180 && !picker.StaysOpenOnEdit, "Popup height is bounded and closes on selection");
        Check(picker.SelectedItem is not null && Equals(vm.SelectedDisk, picker.SelectedItem), "Picker initially selects the active disk");
        Check(vm.Qc.Rows.Count == 5, "All five QC categories retained");
        Check(window.FindName("QcRows") is null, "QC Dashboard is absent from the window");
        var overviewScroll = (ScrollViewer)window.FindName("OverviewScroll");
        var benchmarkScroll = (ScrollViewer)window.FindName("BenchmarkScroll");
        Check(overviewScroll.Height == 420 && overviewScroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && overviewScroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Diagnostics scrolls inside the fixed window");
        Check(benchmarkScroll.Height == 420 && benchmarkScroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && benchmarkScroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Benchmark scrolls inside the fixed window");
        var tabs = (TabControl)window.FindName("RightTabs");
        Check(tabs.Width == 720 && tabs.Height == 460, "All tabs use the full content width");
        Check(tabs.Items.Count == 2 && tabs.SelectedIndex == 0 && ((TabItem)tabs.Items[0]).Header as string == "Benchmark" && ((TabItem)tabs.Items[1]).Header as string == "Diagnostics", "Benchmark opens first and Diagnostics is the only other tab");
        Check(Find<Window>(window).Count() == 0 && System.Windows.Application.Current.Windows.Count == 1, "No child window is created for workspace tabs");
        tabs.SelectedIndex = 1; Pump();
        Check(((ItemsControl)window.FindName("IdentityFields")).Items.Count == 6, "Identity fields fit the drive identity card");
        Check(((ItemsControl)window.FindName("HealthFields")).Items.Count == 6, "Health fields fit the health card");
        Check(Find<Button>(overviewScroll).Count(b => Equals(b.Content, "Copy") && b.Visibility == Visibility.Visible) == 2, "Copy buttons show on firmware and serial");
        Check(Find<Button>(overviewScroll).Count(b => b.Content is string label && label.StartsWith("Read ", StringComparison.Ordinal) && b.ActualHeight == 32) == 1, "Only the top diagnostics button is shown");
        var scan = Find<Button>(window).Single(b => Equals(b.Content, "Scan"));
        Check(scan.ActualWidth > 0 && scan.ActualHeight == 32, "Scan button has explicit accessible size");
        tabs.SelectedIndex = 0; Pump();
        foreach (double dpi in new[] { 1d, 1.25, 1.5 })
        {
            var bitmap = new RenderTargetBitmap((int)(760*dpi), (int)(560*dpi), 96*dpi, 96*dpi, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var bounds = root.TransformToAncestor(viewport).TransformBounds(new Rect(0,0,root.ActualWidth,root.ActualHeight));
            Check(bounds.Width <= viewport.ActualWidth + .1 && bounds.Height <= viewport.ActualHeight + .1, $"Dashboard fits client bounds at {dpi:P0} render scale");
            Check(root.LayoutTransform is ScaleTransform { ScaleX: > 0, ScaleY: > 0 }, $"LayoutTransform preserves text layout at {dpi:P0}");
            Check(!Find<ScrollViewer>(root).Any(v => v.ComputedHorizontalScrollBarVisibility == Visibility.Visible), $"No horizontal cut-off at {dpi:P0}");
            Check(!Find<ScrollViewer>(root).Where(v => v.Name is not "OverviewScroll" and not "BenchmarkScroll").Any(v => v.ComputedVerticalScrollBarVisibility == Visibility.Visible), $"No outer dashboard scrollbar at {dpi:P0}");
            Directory.CreateDirectory("docs/screenshots");
            using var image = File.Create($"docs/screenshots/dashboard-{(live ? "live" : "fixture")}-{dpi*100:F0}.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(image);
        }
        picker.IsDropDownOpen = true; Pump();
        var popup = (System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup",picker);
        Check(popup.IsOpen && popup.Child is FrameworkElement { ActualHeight: > 0 and <= 180 }, "Dropdown opens with bounded visible content");
        Check(picker.Items.Count >= 3, "Three devices available in dropdown");
        for (int i=0; i<picker.Items.Count; i++)
        {
            var item = (ComboBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(i);
            Check(item.ActualHeight > 0 && Find<TextBlock>(item).Any(t => !string.IsNullOrWhiteSpace(t.Text)), $"Dropdown row {i+1} is visible and populated");
        }
        var popupView=(FrameworkElement)popup.Child;
        var popupBitmap=new RenderTargetBitmap((int)popupView.ActualWidth,(int)popupView.ActualHeight,96,96,PixelFormats.Pbgra32);
        popupBitmap.Render(popupView);
        using (var stream=File.Create($"docs/screenshots/dashboard-popup-{(live ? "live" : "fixture")}.png")) { var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(popupBitmap));encoder.Save(stream); }
        picker.IsDropDownOpen=false;
        bool everyPickerChoiceActivates = true;
        for (int i = 0; i < picker.Items.Count; i++)
        {
            picker.SelectedIndex = i; Pump();
            everyPickerChoiceActivates &= Equals(vm.SelectedDisk, picker.SelectedItem) && vm.DashboardIdentity.Count > 0;
        }
        Check(everyPickerChoiceActivates, "Dropdown selection drives every discovered disk");
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        int systemIndex = vm.AllDevices.ToList().FindIndex(d => d.MountPoints.Any(m => string.Equals(Path.GetPathRoot(m), systemRoot, StringComparison.OrdinalIgnoreCase)));
        if (systemIndex >= 0)
        {
            picker.SelectedIndex = systemIndex; Pump();
        }
        Check(vm.Qc.Rows.Count == 5, "QC data refreshes with selected disk and stays off the page");
        window.Width=1280; window.Height=800; Pump();
        Check(window.ActualWidth==760 && window.ActualHeight==560, "Programmatic larger size remains clamped");
        window.Width=600; window.Height=400; Pump();
        Check(window.ActualWidth==760 && window.ActualHeight==560, "Programmatic smaller size remains clamped");
        if (live) foreach(var d in vm.AllDevices) Console.WriteLine(vm.PickerText(d));
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


