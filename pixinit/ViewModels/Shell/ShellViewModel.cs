using System.Collections.ObjectModel;
using System.Text;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Application.Discovery;
using pixinit.Application.Selection;
using pixinit.Application.Scanning;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.ViewModels.Shared;
using pixinit.ViewModels.Sata;
using pixinit.ViewModels.Nvme;
using pixinit.Core.History;
using pixinit.Core.Benchmark;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Application.Benchmarking;
using pixinit.ViewModels.Benchmark;
using pixinit.Infrastructure.Windows;

namespace pixinit.ViewModels.Shell;

public sealed class ShellViewModel : ObservableObject
{
    private readonly DiscoveryCoordinator discovery;
    private readonly DeviceSelection selection = new();
    private CancellationTokenSource? cancellation;
    private bool applying;
    private long generation;
    private bool closed;
    private bool initialScanStarted;
    private bool automaticDiagnosticsEnabled;
    private long automaticDiagnosticsGeneration;
    public Task AutomaticDiagnosticsTask { get; private set; } = Task.CompletedTask;
    private readonly SataOperationCoordinator? sataCoordinator;
    private CancellationTokenSource? sataCancellation;
    private long sataGeneration;
    public bool SataBusy { get; private set; }
    public string SataOperationStatus { get; private set; } = "Not queried · Select a SATA drive to read diagnostics";
    public ActionCommand ReadSataCommand { get; }
    private readonly NvmeOperationCoordinator? nvmeCoordinator;
    private CancellationTokenSource? nvmeCancellation;
    private long nvmeGeneration;
    public bool NvmeBusy { get; private set; }
    public string NvmeOperationStatus { get; private set; } = "Not queried · Select an NVMe drive to read diagnostics";
    public ActionCommand ReadNvmeCommand { get; }
    private int workspaceIndex;
    public int WorkspaceIndex { get => workspaceIndex; set { workspaceIndex = value; Changed(); if (value == 0) QueueQcRefresh(); } }
    public bool IsStale { get; private set; }
    public string Freshness => IsStale ? "Previous discovery is stale" : "Diagnostics read automatically for the selected drive";
    private StorageDevice? selectedOther;
    public StorageDevice? SelectedOther
    {
        get => selectedOther;
        set { selectedOther = value; Benchmark.SelectionChanged(); Benchmark.RefreshDiagnosticContext(); Benchmark.SetSuggestedTarget(value); Changed(); Changed(nameof(SelectedDisk)); Changed(nameof(ActiveTab)); RefreshDiagnosticsView(); }
    }
    public StorageDevice? BenchmarkDevice => SelectedOther ?? (ActiveTab == 0 ? SelectedSata : SelectedNvme);
    public SataViewModel Sata { get; } = new();
    public NvmeViewModel Nvme { get; } = new();
    public BenchmarkViewModel Benchmark { get; }
    public pixinit.ViewModels.Qc.QcDashboard Qc { get; } = new();
    public bool IsBenchmarkDone { get; private set; }
    public string LastBenchmarkResult { get; private set; } = "Run Benchmark to measure";
    public string LastBenchmarkSpeed { get; private set; } = "";
    private void RefreshQc()
    {
        Qc.Update(SelectedDisk, Nvme.Result, Sata.Result, Benchmark.Result, IsStale, IsBenchmarkDone, LastBenchmarkResult);
        foreach (var name in new[] { nameof(DashboardIdentity), nameof(DashboardHealth), nameof(DashboardRefreshCommand), nameof(DashboardFooter), nameof(AdvancedSmartTable), nameof(IsBenchmarkDone), nameof(LastBenchmarkResult), nameof(LastBenchmarkSpeed) }) Changed(name);
    }
    private void QueueQcRefresh(Action? update = null)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            update?.Invoke();
            RefreshQc();
            return;
        }
        _ = dispatcher.InvokeAsync(() =>
        {
            update?.Invoke();
            RefreshQc();
        });
    }
    public sealed record DashboardField(string Label, string Value);
    private string DashboardModel => SelectedDisk is { } d ? PickerText(d).Split(" - ")[0] : "Select a drive";
    public IReadOnlyList<DashboardField> DashboardIdentity
    {
        get
        {
            var d = SelectedDisk;
            var n = d?.Protocol == StorageProtocol.Nvme && Nvme.Result?.DeviceId == d.Id ? Nvme.Result : null;
            var s = d?.Protocol == StorageProtocol.Sata && Sata.Result?.DeviceId == d.Id ? Sata.Result : null;
            string Present(string? value) => string.IsNullOrWhiteSpace(value) ? "Unavailable: refresh diagnostics" : value;
            return [new("Model", DashboardModel), new("Serial", Present(n?.Controller?.Serial ?? s?.Identity?.Serial ?? d?.Serial)),
                new("Firmware", Present(n?.Controller?.Firmware ?? s?.Identity?.Firmware ?? d?.Firmware)), new("Size", d?.CapacityDisplay ?? "Unavailable: select a drive"),
                new("Interface", Present(d?.InterfaceLabel ?? d?.Protocol.ToString())), new("Bridge", d?.BridgeChip ?? "Direct connection")];
        }
    }
    public IReadOnlyList<DashboardField> DashboardHealth
    {
        get
        {
            DeviceViewModel page = ShowNvmeDiagnostics ? Nvme : Sata;
            string Field(string label)
            {
                string value = page.EntireSsdInfo.FirstOrDefault(r => r.Label == label)?.Value ?? "Unavailable: refresh diagnostics";
                int note = value.IndexOf(" (", StringComparison.Ordinal);
                if (note >= 0) value = value[..note];
                int dualUnit = value.IndexOf(" / ", StringComparison.Ordinal);
                return dualUnit >= 0 ? value[..dualUnit] : value;
            }
            return [new("Health", string.IsNullOrWhiteSpace(page.HealthLine) ? "Unavailable" : page.HealthLine),
                new("Temperature", Field("Observed temperature")), new("Power on", Field("Power-on hours")),
                new("Cycles", Field("Power cycles")), new("Host reads", Field("Host reads")), new("Host writes", Field("Host writes"))];
        }
    }
    public ActionCommand DashboardRefreshCommand => ShowNvmeDiagnostics ? ReadNvmeCommand : ReadSataCommand;
    public string AdvancedSmartTable => ShowNvmeDiagnostics ? Nvme.SmartTable : ShowSataDiagnostics ? Sata.SmartTable : "SMART table unavailable: select a SATA or NVMe drive and refresh diagnostics.";
    public string DashboardFooter => $"{DeviceSummary} - Admin: {(IsAdministrator ? "Yes" : "No")}";
    private static bool IsAdministrator
    {
        get
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    public ActionCommand ScanCommand { get; }
    public ActionCommand CancelCommand { get; }
    public ScanState State { get; private set; }
    public string OperationStatus { get; private set; }
    public string FooterSummary
    {
        get
        {
            if (State == ScanState.Running || IsStale) return OperationStatus;
            var device = BenchmarkDevice;
            bool queried = SelectedOther is null && (ActiveTab == 1 ? Nvme.Result is not null : Sata.Result is not null);
            var writes = SelectedOther is null ? ActiveTab == 1 ? Nvme.Result?.Tbw : Sata.Result?.Tbw : null;
            var temp = CurrentTemperature();
            return $"{selection.Devices.Count} device{(selection.Devices.Count == 1 ? "" : "s")} | {(queried ? "Diagnostics queried" : "Diagnostics not yet queried")} | {device?.Protocol.ToString() ?? "No selection"}" +
                (device?.Protocol == StorageProtocol.Nvme ? $" | {device.PcieLinkDisplay}" : "") +
                (writes?.WrittenTB is double value ? $" | {value:F3} TB host writes" : queried ? " | Host writes unavailable" : "") +
                (temp?.Celsius is double c ? $" | {c:F1} C" : "");
        }
    }
    public string FooterWrites
    {
        get
        {
            var writes = SelectedOther is null ? ActiveTab == 1 ? Nvme.Result?.Tbw : Sata.Result?.Tbw : null;
            return writes?.WrittenTB is double value ? $"{value:F3} TB host writes" : "";
        }
    }
    public string FooterBase => FooterWrites.Length == 0 ? FooterSummary : FooterSummary.Replace($" | {FooterWrites}", "", StringComparison.Ordinal);
    public string FooterWritesDisplay => FooterWrites.Length == 0 ? "" : $" | {FooterWrites}";
    private void RefreshFooter() { Changed(nameof(FooterSummary)); Changed(nameof(FooterBase)); Changed(nameof(FooterWrites)); Changed(nameof(FooterWritesDisplay)); }
    public string ScanExplanation => Benchmark.Busy ? "Filesystem benchmark in progress. Cancel or wait before rescanning or reading diagnostics." : (SataBusy || NvmeBusy) ? "Storage diagnostic operation in progress. Cancel or wait before rescanning; blocked native calls must return first." : discovery.IsAvailable ? "Scan Drives discovers disks and reads available diagnostics for the selected drive." : discovery.UnavailableReason;
    public IReadOnlyList<StorageDevice> SataDevices => selection.Devices.Where(d => d.Protocol == StorageProtocol.Sata).ToArray();
    public IReadOnlyList<StorageDevice> NvmeDevices => selection.Devices.Where(d => d.Protocol == StorageProtocol.Nvme).ToArray();
    public IReadOnlyList<StorageDevice> OtherDevices => selection.Devices.Where(d => d.Protocol == StorageProtocol.Unknown).ToArray();
    private readonly Dictionary<string, string> pickerReadings = new();
    public int PickerRevision { get; private set; }
    private void PickerChanged() { PickerRevision++; Changed(nameof(PickerRevision)); Changed(nameof(DashboardIdentity)); }
    public string PickerText(StorageDevice device) => pickerReadings.TryGetValue(device.Id, out var text) ? text : PickerLine(device, null, "Health unavailable: refresh diagnostics", "Temperature unavailable");
    private static string PickerLine(StorageDevice d, string? model, string health, string temperature)
    {
        model ??= string.IsNullOrWhiteSpace(d.BridgeChip) || !string.Equals(d.Model, d.BridgeChip, StringComparison.OrdinalIgnoreCase) ? d.Model : "SSD model unavailable: Identify required";
        var letters = d.MountPoints.Where(m => m.Length >= 2 && m[1] == ':').Select(m => m[..2]).Distinct();
        return $"{model} - {string.Join(", ", letters.DefaultIfEmpty("No drive letter"))} - {d.CapacityDisplay} - {health} - {temperature}";
    }
    private void RememberPicker(StorageDevice d, Core.Diagnostics.Nvme.NvmeDiagnostics? n = null, SataDiagnostics? s = null)
    {
        string? model = n?.Controller?.Model ?? s?.Identity?.Model ?? IdentifyText(s?.Commands.FirstOrDefault(c => c.Operation == AtaOperation.Identify && c.Succeeded)?.Payload);
        string health = "Health unavailable: SMART not reported";
        if (n?.Health is { } h) health = $"{(h.Warning == 0 ? "Good" : "Warning")} {Math.Clamp(100 - (int)h.Used, 0, 100)}%";
        if (s is not null && SataHostCounters.Decode(new(s.Attributes, []), s.Commands.Any(c => c.Operation == AtaOperation.SmartData && c.Succeeded)) is not null)
        {
            var life = s.Attributes.FirstOrDefault(a => a.Id == 0xA9)?.Current;
            bool zeroErrors = s.Attributes.FirstOrDefault(a => a.Id == 5)?.RawBytes is { Length: 6 } raw && raw.All(b => b == 0);
            if (life is >= 0 and <= 100) health = $"{(zeroErrors && s.Temperature.Value < 75 ? "Good" : "Caution")} {life}%";
        }
        double? temp = n?.Temperature.Value ?? s?.Temperature.Value;
        pickerReadings[d.Id] = PickerLine(d, model, health, temp is double t ? $"{t:F0}C" : "Temperature unavailable: SMART not reported");
        PickerChanged();
    }
    public async Task RefreshPickerAsync()
    {
        await AutomaticDiagnosticsTask;
        if (closed || IsStale || Benchmark.Busy || Benchmark.PreparingDiagnostics || SataBusy || NvmeBusy || State != ScanState.Completed) return;
        SataBusy = NvmeBusy = true;
        RefreshOperations();
        try
        {
            foreach (var d in AllDevices.ToArray())
            {
                if (closed) break;
                if (d.CanQueryNvme && nvmeCoordinator is not null) RememberPicker(d, n: await nvmeCoordinator.ReadAsync(d, CancellationToken.None));
                else if (d.CanQuerySata && sataCoordinator is not null) RememberPicker(d, s: await sataCoordinator.ReadAsync(d, CancellationToken.None));
            }
        }
        finally { SataBusy = NvmeBusy = false; RefreshOperations(); }
    }
    public ObservableCollection<StorageDevice> AllDevices { get; } = new();
    public bool ShowSataDiagnostics => SelectedDisk?.Protocol == StorageProtocol.Sata;
    public bool ShowNvmeDiagnostics => SelectedDisk?.Protocol == StorageProtocol.Nvme;
    public bool ShowDiagnosticsPlaceholder => !ShowSataDiagnostics && !ShowNvmeDiagnostics;
    private void RefreshDiagnosticsView() { Changed(nameof(ShowSataDiagnostics)); Changed(nameof(ShowNvmeDiagnostics)); Changed(nameof(ShowDiagnosticsPlaceholder)); }
    public StorageDevice? SelectedDisk
    {
        get
        {
            var current = BenchmarkDevice;
            if (current is null) return null;
            return AllDevices.FirstOrDefault(d => d.Id == current.Id && d.Protocol == current.Protocol) ?? current;
        }
        set
        {
            if (value is null || applying || (SelectedDisk?.Id == value.Id && SelectedDisk.Protocol == value.Protocol)) return;
            CancelSata(); CancelNvme();
            if (value.Protocol == StorageProtocol.Nvme)
            {
                ActiveTab = 1; SelectedNvme = value;
                Nvme.UpdateVolumesForSelectedDisk();
            }
            else if (value.Protocol == StorageProtocol.Sata)
            {
                ActiveTab = 0; SelectedSata = value;
                Sata.UpdateVolumesForSelectedDisk();
            }
            else { SelectedOther = value; Changed(nameof(ActiveTab)); }
            Changed();
            RefreshDiagnosticsView();
            QueueAutomaticDiagnostics();
        }
    }
    public string DeviceSummary => $"Found {selection.Devices.Count} SSD{(selection.Devices.Count == 1 ? "" : "s")}";
    public bool SataEmpty => SataDevices.Count == 0;
    public bool NvmeEmpty => NvmeDevices.Count == 0;
    public int ActiveTab
    {
        get => selection.ActiveProtocol == StorageProtocol.Nvme ? 1 : 0;
        set { if (applying || value == ActiveTab) return; if (selectedOther is not null) { SelectedOther = null; } Benchmark.SelectionChanged(); selection.SelectTab(value == 1 ? StorageProtocol.Nvme : StorageProtocol.Sata); Benchmark.SetSuggestedTarget(value == 1 ? SelectedNvme : SelectedSata); Changed(); RefreshDiagnosticsView(); RefreshFooter(); }
    }
    public StorageDevice? SelectedSata
    {
        get => SataDevices.FirstOrDefault(d => d.Id == selection.SataId);
        set { if (selectedOther is not null) { SelectedOther = null; } if (applying || value?.Id == selection.SataId) return; Benchmark.SelectionChanged(); CancelSata(); sataGeneration++; selection.SelectDevice(StorageProtocol.Sata, value?.Id); Sata.SetDevice(value); Sata.SetStale(IsStale); SataOperationStatus = "Not queried · Select a SATA drive to read diagnostics"; Changed(nameof(SataOperationStatus)); Benchmark.RefreshDiagnosticContext(); Benchmark.SetSuggestedTarget(value); Changed(); Changed(nameof(SelectedDisk)); RefreshDiagnosticsView(); ReadSataCommand.Refresh(); ReadNvmeCommand.Refresh(); }
    }
    public StorageDevice? SelectedNvme
    {
        get => NvmeDevices.FirstOrDefault(d => d.Id == selection.NvmeId);
        set { if (selectedOther is not null) { SelectedOther = null; } if (applying || value?.Id == selection.NvmeId) return; Benchmark.SelectionChanged(); CancelNvme(); nvmeGeneration++; selection.SelectDevice(StorageProtocol.Nvme, value?.Id); Nvme.SetDevice(value); Nvme.SetStale(IsStale); NvmeOperationStatus = "Not queried · Select an NVMe drive to read diagnostics"; Changed(nameof(NvmeOperationStatus)); Benchmark.RefreshDiagnosticContext(); Benchmark.SetSuggestedTarget(value); Changed(); Changed(nameof(SelectedDisk)); RefreshDiagnosticsView(); ReadSataCommand.Refresh(); ReadNvmeCommand.Refresh(); }
    }

    public ShellViewModel(DiscoveryCoordinator discovery, SataOperationCoordinator? sataCoordinator = null, NvmeOperationCoordinator? nvmeCoordinator = null, FileBenchmarkEngine? benchmarkEngine = null, IWriteConsent? writeConsent = null, StorageOperationGate? operationGate = null)
    {
        operationGate ??= new();
        this.discovery = discovery;
        this.sataCoordinator = sataCoordinator; this.nvmeCoordinator = nvmeCoordinator;
        Benchmark = new(benchmarkEngine ?? new FileBenchmarkEngine(), () => selection.Devices, () => BenchmarkDevice, RefreshOperations, writeConsent, CurrentTemperature, () => !closed && !SataBusy && !NvmeBusy && State != ScanState.Running, operationGate, () => SelectedOther is not null ? null : ActiveTab == 1 ? Nvme.Result?.Tbw : Sata.Result?.Tbw, () => SelectedOther is not null ? null : ActiveTab == 1 ? Nvme.ThermalSnapshot : Sata.ThermalSnapshot, ReadSelectedDiagnosticsAsync, () => SelectedOther is not null || (ActiveTab == 1 ? Nvme.Result is not null : Sata.Result is not null), () => WorkspaceIndex = 1,
            percentageUsed: () => SelectedOther is null && ActiveTab == 1 && Nvme.Result?.PercentageUsed.Availability == Availability.Available ? Nvme.Result.PercentageUsed.Value : null);
        Sata.PropertyChanged += (_, e) => { if (e.PropertyName == "Result") UpdateDiagnosticsStatus(); RefreshFooter(); };
        Nvme.PropertyChanged += (_, e) => { if (e.PropertyName == "Result") UpdateDiagnosticsStatus(); RefreshFooter(); };
        PropertyChanged += (_, e) => { if (e.PropertyName is nameof(SelectedDisk) or nameof(IsStale)) QueueQcRefresh(); };
        Sata.PropertyChanged += (_, e) => { if (e.PropertyName == "Result") QueueQcRefresh(); };
        Nvme.PropertyChanged += (_, e) => { if (e.PropertyName == "Result") QueueQcRefresh(); };
        Benchmark.PropertyChanged += (_, e) =>
        {
            QueueQcRefresh(() =>
            {
                if (e.PropertyName == nameof(BenchmarkViewModel.Result) && Benchmark.Result?.Completion == BenchmarkCompletion.Completed)
                {
                    IsBenchmarkDone = true;
                    LastBenchmarkSpeed = Benchmark.Result.Results.FirstOrDefault(r => r.Operation == BenchmarkOperation.SequentialRead)?.MegabytesPerSecond.ToString("F1") ?? "";
                    LastBenchmarkResult = LastBenchmarkSpeed.Length == 0 ? "Benchmark done" : $"Benchmark done - Seq Read {LastBenchmarkSpeed} MB/s";
                }
            });
        };
        RefreshQc();
        OperationStatus = discovery.IsAvailable ? "Ready to discover drives" : "Discovery unavailable · Phase 1 foundation";
        ScanCommand = new(async () => await ScanAsync(), () => !closed && !Benchmark.Busy && !Benchmark.PreparingDiagnostics && !SataBusy && !NvmeBusy && discovery.IsAvailable && State != ScanState.Running);
        ReadSataCommand = new(async () => await ReadSataAsync(), () => !closed && !Benchmark.Busy && !Benchmark.PreparingDiagnostics && !SataBusy && !NvmeBusy && !IsStale && State != ScanState.Running && this.sataCoordinator is not null && SelectedSata is { CanQuerySata: true });
        ReadNvmeCommand = new(async () => await ReadNvmeAsync(), () => !closed && !Benchmark.Busy && !Benchmark.PreparingDiagnostics && !NvmeBusy && !SataBusy && !IsStale && State != ScanState.Running && this.nvmeCoordinator is not null && SelectedNvme is { CanQueryNvme: true });
        CancelCommand = new(Cancel, () => Benchmark.Busy || Benchmark.PreparingDiagnostics || (State == ScanState.Running && cancellation?.IsCancellationRequested == false) || (SataBusy && sataCancellation?.IsCancellationRequested == false) || (NvmeBusy && nvmeCancellation?.IsCancellationRequested == false));

    }

    public async Task ScanAsync()
    {
        if (!ScanCommand.CanExecute(null)) return;
        var previousSerial = SelectedDisk?.Serial;
        automaticDiagnosticsGeneration++;
        var revision = selection.BeginDiscovery();
        long scanGeneration = ++generation;
        sataGeneration++; nvmeGeneration++;
        using var source = new CancellationTokenSource();
        cancellation = source;
        using var timeoutStop = new CancellationTokenSource();
        _ = RequestTimeoutAsync(source, timeoutStop.Token);
        MarkStale(selection.Devices.Count > 0);
        SetState(ScanState.Running, "Discovering connected drives...");
        try
        {
            var devices = await discovery.DiscoverAsync(source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (closed || scanGeneration != generation) return;
            applying = true;
            try
            {
                selection.Apply(devices, revision);
                pickerReadings.Clear(); PickerChanged();
                PublishDevices();
                var retained = !string.IsNullOrWhiteSpace(previousSerial) ? AllDevices.Where(d => d.Serial == previousSerial).ToArray() : [];
                var initial = retained.Length == 1 ? retained[0] : SelectedDisk ?? AllDevices.FirstOrDefault();
                if (initial is not null)
                {
                    selection.SelectTab(initial.Protocol);
                    selection.SelectDevice(initial.Protocol, initial.Id);
                    selectedOther = initial.Protocol == StorageProtocol.Unknown ? initial : null;
                }
                // Clear old readings even for a retained identity after a new discovery.
                Sata.SetDevice(SelectedSata); Nvme.SetDevice(SelectedNvme);
                selectedOther = AllDevices.FirstOrDefault(d => d.Id == selectedOther?.Id); Changed(nameof(SelectedOther));
                Benchmark.SetSuggestedTarget(ActiveTab == 0 ? SelectedSata : SelectedNvme);
                MarkStale(false);
                foreach (var name in new[] { nameof(AllDevices), nameof(SelectedDisk), nameof(SataDevices), nameof(NvmeDevices), nameof(OtherDevices), nameof(SelectedSata), nameof(SelectedNvme), nameof(ActiveTab), nameof(SataEmpty), nameof(NvmeEmpty), nameof(DeviceSummary) }) Changed(name);
                RefreshDiagnosticsView();
            }
            finally { applying = false; }
            SetState(ScanState.Completed, devices.Count == 0 ? "Discovery complete · No disks found" :
                SataEmpty && NvmeEmpty ? $"{devices.Count} disks found · SATA/NVMe protocol not established for diagnostics" : $"Discovery complete · {devices.Count} disks · {NvmeDevices.Count} NVMe + {SataDevices.Count} SATA · Diagnostics not yet queried");
            QueueAutomaticDiagnostics();
        }
        catch (OperationCanceledException) { MarkStale(selection.Devices.Count > 0); SetState(ScanState.Cancelled, "Discovery cancelled · Previous device list retained as stale"); }
        catch (Exception ex) { pixinit.Infrastructure.Logging.DiscoveryLog.Write("Discovery scan failed; previous device list retained", ex); MarkStale(selection.Devices.Count > 0); SetState(ScanState.Failed, $"Discovery failed · Previous list retained · {ex.Message}"); }
        finally { timeoutStop.Cancel(); cancellation = null; CancelCommand.Refresh(); }
    }

    private void SetState(ScanState state, string message)
    {
        State = state; OperationStatus = message;
        Changed(nameof(State)); Changed(nameof(OperationStatus)); RefreshFooter(); ScanCommand.Refresh(); CancelCommand.Refresh(); ReadSataCommand.Refresh(); ReadNvmeCommand.Refresh();
    }
    public void Cancel() { automaticDiagnosticsGeneration++; Benchmark.Cancel(); CancelSata(); CancelNvme(); RequestCancellation("Cancellation requested · Waiting for the current Windows call to return; new scans are blocked"); }
    private void RequestCancellation(string message)
    {
        if (cancellation is null || cancellation.IsCancellationRequested) return;
        var source = cancellation;
        generation++;
        MarkStale(selection.Devices.Count > 0);
        SetState(ScanState.Running, message);
        source.Cancel();
        CancelCommand.Refresh();
    }
    public void Close() { closed = true; automaticDiagnosticsGeneration++; Benchmark.Close(); Cancel(); ScanCommand.Refresh(); }
    public async Task CloseAsync() { closed = true; automaticDiagnosticsGeneration++; Cancel(); await Benchmark.CancelAndWaitAsync(); while (SataBusy || NvmeBusy) await Task.Delay(20); ScanCommand.Refresh(); }
    public Task EnableAutomaticDiagnosticsAsync()
    {
        automaticDiagnosticsEnabled = true;
        QueueAutomaticDiagnostics();
        return AutomaticDiagnosticsTask;
    }
    private void QueueAutomaticDiagnostics()
    {
        long request = ++automaticDiagnosticsGeneration;
        if (!automaticDiagnosticsEnabled) return;
        string? deviceId = SelectedDisk?.Id;
        AutomaticDiagnosticsTask = ReadAutomaticallyAsync(request, deviceId);
    }
    private async Task ReadAutomaticallyAsync(long request, string? deviceId)
    {
        try
        {
            await Task.Yield();
            while (SataBusy || NvmeBusy || Benchmark.Busy || Benchmark.PreparingDiagnostics)
            {
                if (closed || request != automaticDiagnosticsGeneration) return;
                await Task.Delay(50);
            }
            if (closed || request != automaticDiagnosticsGeneration || State != ScanState.Completed || IsStale ||
                deviceId is null || SelectedDisk?.Id != deviceId) return;
            if (SelectedDisk.Protocol == StorageProtocol.Sata)
            {
                if (SelectedSata?.CanQuerySata == true) await ReadSataAsync();
                else SetSataStatus("Direct SATA diagnostics unavailable through this controller");
            }
            else if (SelectedDisk.Protocol == StorageProtocol.Nvme)
            {
                if (SelectedNvme?.CanQueryNvme == true) await ReadNvmeAsync();
                else SetNvmeStatus("Direct NVMe diagnostics unavailable through this controller");
            }
        }
        catch (Exception ex) { pixinit.Infrastructure.Logging.DiscoveryLog.Write("Automatic diagnostics failed", ex); }
    }
    public async Task StartInitialScanAsync()
    {
        if (initialScanStarted) return;
        initialScanStarted = true;
        await Benchmark.InitializeAsync();
        await ScanAsync();
    }
    private async Task RequestTimeoutAsync(CancellationTokenSource source, CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(120), stop);
            if (ReferenceEquals(cancellation, source))
            {
                RequestCancellation("Scan budget reached · Waiting for Windows; results will be discarded");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { pixinit.Infrastructure.Logging.CrashLog.Write("Scan timeout", ex); }
    }
    private void ShowIdentifyModel(StorageDevice device, SataDiagnostics result)
    {
        if (string.IsNullOrWhiteSpace(device.BridgeChip)) return;
        string bridge = device.BridgeChip.Trim();
        bool titledWithBridge = string.Equals(device.Model.Trim(), bridge, StringComparison.OrdinalIgnoreCase)
            || string.Equals(device.Model.Trim(), device.WindowsFriendlyName?.Trim(), StringComparison.OrdinalIgnoreCase);
        if (!titledWithBridge) return;
        string? identified = result.Identity?.Model?.Trim();
        if (string.IsNullOrWhiteSpace(identified))
            identified = IdentifyText(result.Commands.FirstOrDefault(c => c.Operation == AtaOperation.Identify && c.Succeeded)?.Payload);
        if (string.IsNullOrWhiteSpace(identified) || string.Equals(identified, bridge, StringComparison.OrdinalIgnoreCase)) return;
        for (int i = 0; i < AllDevices.Count; i++)
        {
            if (AllDevices[i].Id != device.Id || AllDevices[i].Protocol != device.Protocol) continue;
            AllDevices[i] = AllDevices[i] with { Model = identified };
            Changed(nameof(SelectedDisk));
            return;
        }
    }
    private static string? IdentifyText(byte[]? payload)
    {
        if (payload is not { Length: >= 94 }) return null;
        var bytes = new byte[40];
        for (int i = 0; i < 40; i += 2) { bytes[i] = payload[54 + i + 1]; bytes[i + 1] = payload[54 + i]; }
        string text = bytes.All(b => b == 0 || (b >= 32 && b <= 126))
            ? Encoding.ASCII.GetString(bytes).Trim(' ', '\0')
            : string.Join(' ', Encoding.ASCII.GetString(bytes.Where(b => b >= 32 && b <= 126).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? null : text;
    }
    private void PublishDevices()
    {
        AllDevices.Clear();
        foreach (var device in selection.Devices) AllDevices.Add(device);
    }
    private void MarkStale(bool stale)
    {
        IsStale = stale; Sata.SetStale(stale); Nvme.SetStale(stale);
        Changed(nameof(IsStale)); Changed(nameof(Freshness));
    }

    public async Task ReadSataAsync(bool capture = false)
    {
        if (capture ? !CanReadProtocol(false) : !ReadSataCommand.CanExecute(null)) return;
        var device = SelectedSata;
        var coordinator = sataCoordinator;
        if (device is null || coordinator is null) { SetSataStatus("Select a SATA-classified drive before reading diagnostics"); return; }
        long operation = ++sataGeneration;
        using var source = new CancellationTokenSource();
        sataCancellation = source;
        SataBusy = true; Sata.SetDevice(device); SetSataStatus("Opening selected drive");
        var progress = new Progress<string>(stage =>
        {
            if (!closed && operation == sataGeneration && SataBusy && !source.IsCancellationRequested) SetSataStatus(stage);
        });
        try
        {
            var result = await coordinator.ReadAsync(device, source.Token, progress);
            source.Token.ThrowIfCancellationRequested();
            if (!closed && operation == sataGeneration && SelectedSata?.Id == device.Id)
            { Sata.Apply(result); RememberPicker(device, s: result); ShowIdentifyModel(device, result); SetSataStatus(result.Summary); }
        }
        catch (OperationCanceledException) { if (!closed) { SetSataStatus("SATA read cancelled · Partial data discarded; no complete result published"); } }
        catch (Exception ex) { if (!closed && operation == sataGeneration) { SetSataStatus($"SATA read failed · {ex.Message}"); } }
        finally { SataBusy = false; sataCancellation = null; RefreshOperations(); }
    }
    private void CancelSata()
    {
        if (sataCancellation is null || sataCancellation.IsCancellationRequested) return;
        var source = sataCancellation;
        sataGeneration++;
        SetSataStatus("SATA cancellation requested · Waiting for Windows; new storage operations are blocked");
        source.Cancel(); CancelCommand.Refresh();
    }
    private void SetSataStatus(string message) { SataOperationStatus = message; Changed(nameof(SataOperationStatus)); RefreshOperations(); }
    public async Task ReadNvmeAsync(bool capture = false)
    {
        if (capture ? !CanReadProtocol(true) : !ReadNvmeCommand.CanExecute(null)) return;
        var device = SelectedNvme;
        var coordinator = nvmeCoordinator;
        if (device is null || coordinator is null) { SetNvmeStatus("Select an NVMe-classified drive before reading diagnostics"); return; }
        long operation = ++nvmeGeneration;
        using var source = new CancellationTokenSource();
        nvmeCancellation = source;
        NvmeBusy = true; Nvme.SetDevice(device); SetNvmeStatus("Opening selected drive");
        var progress = new Progress<string>(stage =>
        {
            if (!closed && operation == nvmeGeneration && NvmeBusy && !source.IsCancellationRequested) SetNvmeStatus(stage);
        });
        try
        {
            var result = await coordinator.ReadAsync(device, source.Token, progress);
            source.Token.ThrowIfCancellationRequested();
            if (!closed && operation == nvmeGeneration && SelectedNvme?.Id == device.Id)
            { Nvme.Apply(result); RememberPicker(device, n: result); SetNvmeStatus(result.Summary); }
        }
        catch (OperationCanceledException) { if (!closed) { SetNvmeStatus("NVMe read cancelled · Partial data discarded; no complete result published"); } }
        catch (Exception ex) { if (!closed && operation == nvmeGeneration) { SetNvmeStatus($"NVMe read failed · {ex.Message}"); } }
        finally { NvmeBusy = false; nvmeCancellation = null; RefreshOperations(); }
    }
    private void CancelNvme()
    {
        if (nvmeCancellation is null || nvmeCancellation.IsCancellationRequested) return;
        var source = nvmeCancellation;
        nvmeGeneration++;
        SetNvmeStatus("NVMe cancellation requested · Waiting for Windows; new storage operations are blocked");
        source.Cancel(); CancelCommand.Refresh();
    }
    private void SetNvmeStatus(string message) { NvmeOperationStatus = message; Changed(nameof(NvmeOperationStatus)); RefreshOperations(); }
    private void UpdateDiagnosticsStatus()
    {
        if (State != ScanState.Completed) return;
        bool queried = SelectedOther is null && (ActiveTab == 1 ? Nvme.Result is not null : Sata.Result is not null);
        var writes = SelectedOther is null ? ActiveTab == 1 ? Nvme.Result?.Tbw : Sata.Result?.Tbw : null;
        OperationStatus = $"Discovery complete · {selection.Devices.Count} disks · {(queried ? "Diagnostics queried" : "Diagnostics not yet queried")}" + (writes?.WrittenTB is double value ? $" · {value:F3} TB host writes" : queried ? " · Host writes unavailable" : "");
        Changed(nameof(OperationStatus));
    }
    private void RefreshOperations()
    {
        if (!SataBusy && !NvmeBusy) UpdateDiagnosticsStatus();
        Benchmark.RefreshDiagnosticContext(); RefreshFooter();
        Changed(nameof(SataBusy)); Changed(nameof(NvmeBusy)); Changed(nameof(ScanExplanation));
        ReadSataCommand.Refresh(); ReadNvmeCommand.Refresh(); ScanCommand.Refresh(); CancelCommand.Refresh(); Benchmark.StartCommand.Refresh();
    }
    private bool CanReadProtocol(bool nvme) => !closed && !SataBusy && !NvmeBusy && !IsStale && State != ScanState.Running && (nvme ? nvmeCoordinator is not null && SelectedNvme is { CanQueryNvme: true } : sataCoordinator is not null && SelectedSata is { CanQuerySata: true });
    private async Task ReadSelectedDiagnosticsAsync(CancellationToken token)
    {
        bool nvme = ActiveTab == 1;
        using var registration = token.Register(() => { if (nvme) CancelNvme(); else CancelSata(); });
        token.ThrowIfCancellationRequested();
        if (nvme) await ReadNvmeAsync(true); else await ReadSataAsync(true);
        token.ThrowIfCancellationRequested();
    }
    private BenchmarkTemperature? CurrentTemperature()
    {
        if (SelectedOther is not null) return null;
        if (ActiveTab == 1 && Nvme.Result?.Temperature is { Value: double value, Availability: Availability.Available } metric)
            return new(value, metric.Availability.ToString(), metric.Source ?? "NVMe SMART/Health", Nvme.Result.Assessment?.ProtocolScope ?? "NVMe scope unavailable", metric.ObservedAt);
        if (ActiveTab == 0 && Sata.Result?.Temperature is { Value: double sataValue, Availability: Availability.Available } sataMetric)
            return new(sataValue, sataMetric.Availability.ToString(), sataMetric.Source ?? "SATA diagnostics", "Selected SATA device", sataMetric.ObservedAt);
        return null;
    }
}


public sealed class DiskPickerTextConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.FirstOrDefault() is not StorageDevice device) return "Select a physical disk";
        string text = values.ElementAtOrDefault(1) is ShellViewModel shell ? shell.PickerText(device) : device.DisplayName;
        string[] parts = text.Split(" - ");
        return (parameter as string) switch
        {
            "Short" => parts.Length >= 5 ? $"{parts[0]} - {parts[1]} - {parts[3]} - {parts[4]}" : text,
            "Model" => parts[0],
            "Location" => parts.Length >= 3 ? string.Join(" - ", parts.Skip(1).Take(2)) : device.CapacityDisplay,
            "Health" => parts.Length >= 5 ? string.Join(" - ", parts.Skip(3)) : "Health / temperature unavailable",
            _ => text
        };
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DashboardScaleConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        values.Length >= 2 && values[0] is double width && values[1] is double height && width > 0 && height > 0
            ? Math.Min(1, Math.Min(width / 760, height / 536)) : 1d;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DashboardSingleLineConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        string.Join(" | ", (value?.ToString() ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class QcReasonConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string category = values.ElementAtOrDefault(0)?.ToString() ?? "";
        string status = values.ElementAtOrDefault(1)?.ToString() ?? "";
        string reason = values.ElementAtOrDefault(2)?.ToString() ?? "";
        if (category == "Performance" && status == "CAUTION") return "Not measured - Run Benchmark";
        return string.Join(" | ", reason.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
