using pixinit.Application.Discovery;
using pixinit.Application.Selection;
using pixinit.Application.Scanning;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.ViewModels.Shared;
using pixinit.ViewModels.Sata;
using pixinit.ViewModels.Nvme;
using pixinit.ViewModels.History;
using pixinit.Core.History;
using pixinit.Infrastructure.History;
using pixinit.Core.Benchmark;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Application.Benchmarking;
using pixinit.ViewModels.Benchmark;

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
    private readonly SataOperationCoordinator? sataCoordinator;
    private CancellationTokenSource? sataCancellation;
    private long sataGeneration;
    public bool SataBusy { get; private set; }
    public string SataOperationStatus { get; private set; } = "Not queried · Select a confirmed SATA drive to read diagnostics";
    public ActionCommand ReadSataCommand { get; }
    private readonly NvmeOperationCoordinator? nvmeCoordinator;
    private CancellationTokenSource? nvmeCancellation;
    private long nvmeGeneration;
    public bool NvmeBusy { get; private set; }
    public string NvmeOperationStatus { get; private set; } = "Not queried · Select a confirmed NVMe drive to read diagnostics";
    public ActionCommand ReadNvmeCommand { get; }
    public bool IsStale { get; private set; }
    public bool OtherExpanded { get; set; }
    public string Freshness => IsStale ? "Previous discovery is stale" : "Discovery and diagnostics are separate read-only actions";
    public SataViewModel Sata { get; } = new();
    public NvmeViewModel Nvme { get; } = new();
    public HistoryViewModel? History { get; }
    public BenchmarkViewModel Benchmark { get; }
    private readonly Guid applicationSessionId = Guid.NewGuid();
    public ActionCommand ScanCommand { get; }
    public ActionCommand CancelCommand { get; }
    public ScanState State { get; private set; }
    public string OperationStatus { get; private set; }
    public string ScanExplanation => Benchmark.Busy ? "Filesystem benchmark in progress. Cancel or wait before rescanning or reading diagnostics." : (SataBusy || NvmeBusy) ? "Storage diagnostic operation in progress. Cancel or wait before rescanning; blocked native calls must return first." : discovery.IsAvailable ? "Scan Drives discovers disks only. Diagnostics and filesystem benchmarks are explicit actions." : discovery.UnavailableReason;
    public IReadOnlyList<StorageDevice> SataDevices => selection.Devices.Where(d => d.Protocol == StorageProtocol.Sata).ToArray();
    public IReadOnlyList<StorageDevice> NvmeDevices => selection.Devices.Where(d => d.Protocol == StorageProtocol.Nvme).ToArray();
    public IReadOnlyList<StorageDevice> OtherDevices => selection.Devices.Where(d => d.Protocol == StorageProtocol.Unknown).ToArray();
    public string OtherSummary => $"Other / Unidentified · {OtherDevices.Count}";
    public string DeviceSummary => $"{selection.Devices.Count} devices discovered";
    public bool SataEmpty => SataDevices.Count == 0;
    public bool NvmeEmpty => NvmeDevices.Count == 0;
    public int ActiveTab
    {
        get => selection.ActiveProtocol == StorageProtocol.Nvme ? 1 : 0;
        set { if (applying || value == ActiveTab) return; Benchmark.SelectionChanged(); selection.SelectTab(value == 1 ? StorageProtocol.Nvme : StorageProtocol.Sata); Benchmark.SetSuggestedTarget(value == 1 ? SelectedNvme : SelectedSata); Changed(); }
    }
    public StorageDevice? SelectedSata
    {
        get => SataDevices.FirstOrDefault(d => d.Id == selection.SataId);
        set { if (applying || value?.Id == selection.SataId) return; Benchmark.SelectionChanged(); CancelSata(); sataGeneration++; selection.SelectDevice(StorageProtocol.Sata, value?.Id); Sata.SetDevice(value); Sata.SetStale(IsStale); Benchmark.SetSuggestedTarget(value); Changed(); ReadSataCommand.Refresh(); ReadNvmeCommand.Refresh(); }
    }
    public StorageDevice? SelectedNvme
    {
        get => NvmeDevices.FirstOrDefault(d => d.Id == selection.NvmeId);
        set { if (applying || value?.Id == selection.NvmeId) return; Benchmark.SelectionChanged(); CancelNvme(); nvmeGeneration++; selection.SelectDevice(StorageProtocol.Nvme, value?.Id); Nvme.SetDevice(value); Nvme.SetStale(IsStale); Benchmark.SetSuggestedTarget(value); Changed(); ReadNvmeCommand.Refresh(); }
    }

    public ShellViewModel(DiscoveryCoordinator discovery, SataOperationCoordinator? sataCoordinator = null, NvmeOperationCoordinator? nvmeCoordinator = null, SqliteHistoryStore? historyStore = null, FileBenchmarkEngine? benchmarkEngine = null, IWriteConsent? writeConsent = null)
    {
        this.discovery = discovery;
        this.sataCoordinator = sataCoordinator; this.nvmeCoordinator = nvmeCoordinator; History = historyStore is null ? null : new(historyStore);
        Benchmark = new(benchmarkEngine ?? new FileBenchmarkEngine(), historyStore, () => selection.Devices, () => ActiveTab == 0 ? SelectedSata : SelectedNvme, RefreshOperations, writeConsent, CurrentTemperature, () => !closed && !SataBusy && !NvmeBusy && State != ScanState.Running);
        OperationStatus = discovery.IsAvailable ? "Ready to discover drives" : "Discovery unavailable · Phase 1 foundation";
        ScanCommand = new(async () => await ScanAsync(), () => !closed && !Benchmark.Busy && !SataBusy && !NvmeBusy && discovery.IsAvailable && State != ScanState.Running);
        ReadSataCommand = new(async () => await ReadSataAsync(), () => !closed && !Benchmark.Busy && !SataBusy && !NvmeBusy && !IsStale && State != ScanState.Running && this.sataCoordinator is not null && SelectedSata is { Protocol: StorageProtocol.Sata, Bus: ConnectionBus.Sata, NativeBusType: 11 });
        ReadNvmeCommand = new(async () => await ReadNvmeAsync(), () => !closed && !Benchmark.Busy && !NvmeBusy && !SataBusy && !IsStale && State != ScanState.Running && this.nvmeCoordinator is not null && SelectedNvme is { Protocol: StorageProtocol.Nvme, Bus: ConnectionBus.Nvme, NativeBusType: 17 });
        CancelCommand = new(Cancel, () => Benchmark.Busy || (State == ScanState.Running && cancellation?.IsCancellationRequested == false) || (SataBusy && sataCancellation?.IsCancellationRequested == false) || (NvmeBusy && nvmeCancellation?.IsCancellationRequested == false));
    }

    public async Task ScanAsync()
    {
        if (!ScanCommand.CanExecute(null)) return;
        var revision = selection.BeginDiscovery();
        long scanGeneration = ++generation;
        sataGeneration++; nvmeGeneration++;
        using var source = new CancellationTokenSource();
        cancellation = source;
        using var timeoutStop = new CancellationTokenSource();
        _ = RequestTimeoutAsync(source, timeoutStop.Token);
        MarkStale(selection.Devices.Count > 0);
        SetState(ScanState.Running, "Discovering connected drives…");
        try
        {
            var devices = await discovery.DiscoverAsync(source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (closed || scanGeneration != generation) return;
            applying = true;
            try
            {
                selection.Apply(devices, revision);
                // Clear old readings even for a retained identity after a new discovery.
                Sata.SetDevice(SelectedSata); Nvme.SetDevice(SelectedNvme);
                Benchmark.SetSuggestedTarget(ActiveTab == 0 ? SelectedSata : SelectedNvme);
                MarkStale(false);
                OtherExpanded = OtherDevices.Count > 0 && SataEmpty && NvmeEmpty;
                Changed(nameof(OtherExpanded));
                foreach (var name in new[] { nameof(SataDevices), nameof(NvmeDevices), nameof(OtherDevices), nameof(SelectedSata), nameof(SelectedNvme), nameof(ActiveTab), nameof(SataEmpty), nameof(NvmeEmpty), nameof(OtherSummary), nameof(DeviceSummary) }) Changed(name);
            }
            finally { applying = false; }
            SetState(ScanState.Completed, devices.Count == 0 ? "Discovery complete · No disks found" :
                SataEmpty && NvmeEmpty ? $"{devices.Count} disks found · See Other / Unidentified" : $"Discovery complete · {devices.Count} disks · Diagnostics not yet queried");
        }
        catch (OperationCanceledException) { MarkStale(selection.Devices.Count > 0); SetState(ScanState.Cancelled, "Discovery cancelled · Previous device list retained as stale"); }
        catch (Exception ex) { MarkStale(selection.Devices.Count > 0); SetState(ScanState.Failed, $"Discovery failed · Previous list retained · {ex.Message}"); }
        finally { timeoutStop.Cancel(); cancellation = null; CancelCommand.Refresh(); }
    }

    private void SetState(ScanState state, string message)
    {
        State = state; OperationStatus = message;
        Changed(nameof(State)); Changed(nameof(OperationStatus)); ScanCommand.Refresh(); CancelCommand.Refresh(); ReadSataCommand.Refresh(); ReadNvmeCommand.Refresh();
    }
    public void Cancel() { Benchmark.Cancel(); CancelSata(); CancelNvme(); RequestCancellation("Cancellation requested · Waiting for the current Windows call to return; new scans are blocked"); }
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
    public void Close() { closed = true; Benchmark.Close(); Cancel(); ScanCommand.Refresh(); }
    public async Task CloseAsync() { closed = true; await Benchmark.CancelAndWaitAsync(); Cancel(); ScanCommand.Refresh(); }
    public async Task StartInitialScanAsync()
    {
        if (initialScanStarted) return;
        initialScanStarted = true;
        if (History is not null) _ = History.InitializeAsync();
        _ = Benchmark.InitializeAsync();
        await ScanAsync();
    }
    private async Task RequestTimeoutAsync(CancellationTokenSource source, CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stop);
            if (ReferenceEquals(cancellation, source))
            {
                RequestCancellation("30-second scan budget reached · Waiting for Windows; results will be discarded");
            }
        }
        catch (OperationCanceledException) { }
    }
    private void MarkStale(bool stale)
    {
        IsStale = stale; Sata.SetStale(stale); Nvme.SetStale(stale);
        Changed(nameof(IsStale)); Changed(nameof(Freshness));
    }

    public async Task ReadSataAsync()
    {
        if (!ReadSataCommand.CanExecute(null)) return;
        var device = SelectedSata!;
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
            var result = await sataCoordinator!.ReadAsync(device, source.Token, progress);
            source.Token.ThrowIfCancellationRequested();
            if (!closed && operation == sataGeneration && SelectedSata?.Id == device.Id)
            { Sata.Apply(result); SetSataStatus(result.Summary); if (History is not null) await History.SaveAsync(SnapshotFactory.Create(applicationSessionId, device, result)); }
        }
        catch (OperationCanceledException) { if (!closed) { SetSataStatus("SATA read cancelled · Partial data discarded; no complete result published"); if (History is not null) await History.SaveAsync(SnapshotFactory.CreateAttempt(applicationSessionId, device, "Cancelled", "The diagnostic session was cancelled before a complete result was published.")); } }
        catch (Exception ex) { if (!closed && operation == sataGeneration) { SetSataStatus($"SATA read failed · {ex.Message}"); if (History is not null) await History.SaveAsync(SnapshotFactory.CreateAttempt(applicationSessionId, device, "Failed", ex.Message)); } }
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
    public async Task ReadNvmeAsync()
    {
        if (!ReadNvmeCommand.CanExecute(null)) return;
        var device = SelectedNvme!;
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
            var result = await nvmeCoordinator!.ReadAsync(device, source.Token, progress);
            source.Token.ThrowIfCancellationRequested();
            if (!closed && operation == nvmeGeneration && SelectedNvme?.Id == device.Id)
            { Nvme.Apply(result); SetNvmeStatus(result.Summary); if (History is not null) await History.SaveAsync(SnapshotFactory.Create(applicationSessionId, device, result)); }
        }
        catch (OperationCanceledException) { if (!closed) { SetNvmeStatus("NVMe read cancelled · Partial data discarded; no complete result published"); if (History is not null) await History.SaveAsync(SnapshotFactory.CreateAttempt(applicationSessionId, device, "Cancelled", "The diagnostic session was cancelled before a complete result was published.")); } }
        catch (Exception ex) { if (!closed && operation == nvmeGeneration) { SetNvmeStatus($"NVMe read failed · {ex.Message}"); if (History is not null) await History.SaveAsync(SnapshotFactory.CreateAttempt(applicationSessionId, device, "Failed", ex.Message)); } }
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
    private void RefreshOperations()
    {
        Changed(nameof(SataBusy)); Changed(nameof(NvmeBusy)); Changed(nameof(ScanExplanation));
        ReadSataCommand.Refresh(); ReadNvmeCommand.Refresh(); ScanCommand.Refresh(); CancelCommand.Refresh(); Benchmark.StartCommand.Refresh();
    }
    private BenchmarkTemperature? CurrentTemperature()
    {
        if (ActiveTab == 1 && Nvme.Result?.Temperature is { Value: double value, Availability: Availability.Available } metric)
            return new(value, metric.Availability.ToString(), metric.Source ?? "NVMe SMART/Health", Nvme.Result.Assessment?.ProtocolScope ?? "NVMe scope unavailable", metric.ObservedAt);
        if (ActiveTab == 0 && Sata.Result?.Temperature is { Value: double sataValue, Availability: Availability.Available } sataMetric)
            return new(sataValue, sataMetric.Availability.ToString(), sataMetric.Source ?? "SATA diagnostics", "Selected SATA device", sataMetric.ObservedAt);
        return null;
    }
}

