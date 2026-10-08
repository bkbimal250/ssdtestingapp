using pixinit.Core.Diagnostics.Common;

namespace pixinit.ViewModels.Shared;

public abstract partial class DeviceViewModel
{
    protected virtual ThermalInfo? ObservedThermal => null;
    private ThermalInfo? captured;
    private CancellationTokenSource? captureCancellation;
    private long captureGeneration;
    public bool ThermalCaptureBusy { get; private set; }
    public DateTimeOffset? LastBenchmarkFinishedUtc { get; private set; }
    public ThermalInfo? ThermalSnapshot => ObservedThermal is { } observed ? observed with
    { IdleC = captured?.IdleC, LoadC = captured?.LoadC, IdleTempObservedAt = captured?.IdleTempObservedAt, LoadTempObservedAt = captured?.LoadTempObservedAt, IdleCondition = captured?.IdleCondition ?? "Not captured", LoadCondition = captured?.LoadCondition ?? "Not captured" } : captured;
    public double? CurrentTemp => ThermalSnapshot?.ObservedC;
    public double? IdleTemp => ThermalSnapshot?.IdleC;
    public double? LoadTemp => ThermalSnapshot?.LoadC;
    public DateTimeOffset? IdleTempObservedAt => ThermalSnapshot?.IdleTempObservedAt;
    public DateTimeOffset? LoadTempObservedAt => ThermalSnapshot?.LoadTempObservedAt;
    public string ThermalCaptureStatus { get; private set; } = "Capture Idle verifies ten minutes without reported disk activity. Manual load is a labeled observation, not a proven workload condition.";
    public string CurrentTempDisplay => ThermalSnapshot is { ObservedC: double c } t ? $"Current: {c:F1} C {t.State}; observation {t.ObservedAt?.ToLocalTime():dd-MM-yyyy HH:mm:ss}{(t.InterpretationVerified ? "" : " (unverified encoding)")}" : "Current: N/A - Run Diagnostics";
    public string IdleTempDisplay => IdleTemp is double c ? $"Idle: {c:F1} C; observed {IdleTempObservedAt?.ToLocalTime():g}" : "Idle: Unavailable - capture required";
    public string LoadTempDisplay => LoadTemp is double c ? $"Load: {c:F1} C; observed {LoadTempObservedAt?.ToLocalTime():g}; {ThermalSnapshot?.LoadCondition}" : "Load: Unavailable - capture required";
    private Func<CancellationToken, Task>? captureRead;
    private Func<CancellationToken, Task<bool>>? verifyIdle;
    private Func<bool> canCapture = () => false;
    private Action captureChanged = () => { };
    public ActionCommand CaptureIdleCommand { get; }
    public ActionCommand CaptureLoadCommand { get; }
    public ActionCommand CancelThermalCaptureCommand { get; }
    protected DeviceViewModel()
    {
        CaptureIdleCommand = new(async () => await CaptureTemperatureAsync(true), () => !ThermalCaptureBusy && Device is not null && canCapture());
        CaptureLoadCommand = new(async () => await CaptureTemperatureAsync(false), () => !ThermalCaptureBusy && Device is not null && canCapture());
        CancelThermalCaptureCommand = new(CancelThermalCapture, () => ThermalCaptureBusy);
    }
    public void ConfigureThermalCapture(Func<CancellationToken, Task> read, Func<CancellationToken, Task<bool>> idle, Func<bool> allowed, Action changed)
    { captureRead = read; verifyIdle = idle; canCapture = allowed; captureChanged = changed; NotifyThermalCapture(); }
    public void MarkBenchmarkFinished(DateTimeOffset finished) { LastBenchmarkFinishedUtc = finished; Changed(nameof(LastBenchmarkFinishedUtc)); }
    public async Task CaptureTemperatureAsync(bool idle, CancellationToken token = default, bool automatic = false)
    {
        if (ThermalCaptureBusy || Device is null || captureRead is null || (!automatic && !canCapture())) return;
        if (idle && LastBenchmarkFinishedUtc is { } last && DateTimeOffset.UtcNow - last <= TimeSpan.FromMinutes(10))
        { ThermalCaptureStatus = "Wait 10 min idle then capture. Last benchmark finished less than ten minutes ago."; NotifyThermalCapture(); return; }
        string id = Device.Id; long run = ++captureGeneration;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token); captureCancellation = source;
        ThermalCaptureBusy = true; ThermalCaptureStatus = idle ? "Verifying ten minutes without disk activity; Cancel is available" : "Capturing a fresh read-only temperature snapshot"; NotifyThermalCapture(); captureChanged();
        try
        {
            if (idle && (verifyIdle is null || !await verifyIdle(source.Token)))
            { ThermalCaptureStatus = "Wait 10 min idle then capture. Disk activity was observed or cannot be verified."; return; }
            DateTimeOffset requested = DateTimeOffset.UtcNow;
            await captureRead(source.Token); source.Token.ThrowIfCancellationRequested();
            if (run != captureGeneration || Device?.Id != id) return;
            var observed = ObservedThermal;
            if (observed?.ObservedC is not double value || observed.ObservedAt < requested || observed.ObservedAt is null)
            { ThermalCaptureStatus = "Temperature capture unavailable - diagnostic read did not provide a fresh reading"; return; }
            captured = ThermalSnapshot ?? observed;
            captured = idle ? captured with { IdleC = value, IdleTempObservedAt = observed.ObservedAt, IdleCondition = "Ten-minute zero reported transfers/queue window before fresh diagnostic read" }
                : captured with { LoadC = value, LoadTempObservedAt = observed.ObservedAt, LoadCondition = automatic ? "Post-sustained observation; does not prove peak load temperature" : "Manual observation; load condition not established" };
            ThermalCaptureStatus = idle ? "Idle observation captured with verified activity window" : "Load observation captured; measurement conditions retained";
        }
        catch (OperationCanceledException) { ThermalCaptureStatus = "Temperature capture cancelled; no new observation saved"; }
        catch (Exception ex) { ThermalCaptureStatus = "Temperature capture unavailable: " + ex.Message; }
        finally { ThermalCaptureBusy = false; captureCancellation = null; NotifyThermalCapture(); captureChanged(); }
    }
    public void CancelThermalCapture() => captureCancellation?.Cancel();
    private void ResetThermalCapture() { captureGeneration++; CancelThermalCapture(); captured = null; LastBenchmarkFinishedUtc = null; NotifyThermalCapture(); }
    public void NotifyThermalCapture()
    {
        foreach (var n in new[] { nameof(ThermalSnapshot), nameof(CurrentTemp), nameof(IdleTemp), nameof(LoadTemp), nameof(IdleTempObservedAt), nameof(LoadTempObservedAt), nameof(CurrentTempDisplay), nameof(IdleTempDisplay), nameof(LoadTempDisplay), nameof(ThermalCaptureStatus), nameof(ThermalCaptureBusy) }) Changed(n);
        CaptureIdleCommand?.Refresh(); CaptureLoadCommand?.Refresh(); CancelThermalCaptureCommand?.Refresh();
    }
}
