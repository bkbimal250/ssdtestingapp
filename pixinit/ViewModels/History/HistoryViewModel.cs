using System.Collections.ObjectModel;
using Microsoft.Win32;
using pixinit.Application.Reporting;
using pixinit.Core.History;
using pixinit.Infrastructure.History;
using pixinit.Infrastructure.Logging;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.History;

public sealed class HistoryViewModel : ObservableObject
{
    private readonly SqliteHistoryStore store;
    private DiagnosticSnapshot? selectedSnapshot;
    private HistoryRow? selected;
    private int offset;
    private string protocolFilter = "All";
    private string? deviceKeyFilter;
    public ObservableCollection<HistoryRow> Sessions { get; } = [];
    public HistoryRow? Selected
    {
        get => selected;
        set { selected = value; Changed(); _ = LoadSelectedAsync(); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh(); FilterSelectedDeviceCommand.Refresh(); }
    }
    public IReadOnlyList<string> ProtocolFilters { get; } = ["All", "Sata", "Nvme"];
    public string ProtocolFilter { get => protocolFilter; set { protocolFilter = value; Changed(); } }
    public string FilterDescription => deviceKeyFilter is null ? $"Protocol: {ProtocolFilter}" : $"Protocol: {ProtocolFilter} · selected device identity";
    public string Detail { get; private set; } = "Select a historical diagnostic session. Historical results retain their original rule version.";
    public string Comparison { get; private set; } = "Comparison unavailable until two compatible sessions exist.";
    public string Status { get; private set; } = "History not initialized";
    public int RetentionDays { get; set; } = 2;
    public bool ExportRedactSerial { get; set; } = true;
    public bool ExportIncludeRawPayloads { get; set; }
    public ActionCommand LoadMoreCommand { get; }
    public ActionCommand ApplyRetentionCommand { get; }
    public ActionCommand ExportTextCommand { get; }
    public ActionCommand ExportJsonCommand { get; }
    public ActionCommand ApplyFiltersCommand { get; }
    public ActionCommand FilterSelectedDeviceCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public HistoryViewModel(SqliteHistoryStore store)
    {
        this.store = store;
        LoadMoreCommand = new(async () => await LoadPageAsync(false));
        ApplyRetentionCommand = new(async () => await ApplyRetentionAsync());
        ExportTextCommand = new(async () => await ExportAsync(false), () => selectedSnapshot is not null);
        ExportJsonCommand = new(async () => await ExportAsync(true), () => selectedSnapshot is not null);
        ApplyFiltersCommand = new(async () => await LoadPageAsync(true));
        FilterSelectedDeviceCommand = new(async () => { deviceKeyFilter = selectedSnapshot?.Device.MatchKey; Changed(nameof(FilterDescription)); await LoadPageAsync(true); }, () => selectedSnapshot?.Device.IdentityReliable == true);
        ClearFiltersCommand = new(async () => { ProtocolFilter = "All"; deviceKeyFilter = null; Changed(nameof(FilterDescription)); await LoadPageAsync(true); });
    }
    public async Task InitializeAsync()
    {
        try { await store.InitializeAsync(); RetentionDays = await store.GetRetentionDaysAsync(); Changed(nameof(RetentionDays)); await LoadPageAsync(true); _ = CleanupAsync(); }
        catch (Exception ex) { Error("History unavailable", ex); }
    }
    public async Task SaveAsync(DiagnosticSnapshot snapshot)
    {
        try
        {
            await store.SaveAsync(snapshot);
            selectedSnapshot = snapshot; selected = null; Changed(nameof(Selected));
            Detail = $"Live diagnostic snapshot · {snapshot.ObservedAtUtc:O}\n{snapshot.Device.Model} · {snapshot.Protocol}\nAssessment: {snapshot.Assessment.StateDisplay}\nCoverage: {snapshot.Assessment.Coverage}\nChecklist: {snapshot.Assessment.ChecklistDisplay}\nRules: {snapshot.RuleSetVersion}\nScope: {snapshot.Scope}\n\n{snapshot.Assessment.Explanation}\n\n{Core.Assessment.HealthAssessment.Boundary}";
            Comparison = "Select a historical session to load its compatible previous comparison.";
            Status = "Live snapshot saved to history";
            Changed(nameof(Detail)); Changed(nameof(Comparison)); Changed(nameof(Status)); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh(); FilterSelectedDeviceCommand.Refresh();
            await LoadPageAsync(true);
        }
        catch (Exception ex) { Error("Live diagnostics available · history save failed", ex); }
    }
    public async Task ApplyRetentionAsync()
    {
        try { await store.SetRetentionDaysAsync(RetentionDays); await CleanupAsync(); Status = $"Retention set to {RetentionDays} days"; Changed(nameof(Status)); }
        catch (Exception ex) { Error("Retention update failed", ex); }
    }
    public async Task LoadPageAsync(bool reset)
    {
        try
        {
            if (reset) { offset = 0; Sessions.Clear(); }
            string? protocol = ProtocolFilter == "All" ? null : ProtocolFilter;
            var rows = await store.ListAsync(offset, 25, protocol, deviceKeyFilter); foreach (var row in rows) Sessions.Add(row); offset += rows.Count;
            Status = Sessions.Count == 0 ? "No saved diagnostic sessions" : $"Loaded {Sessions.Count} historical sessions"; Changed(nameof(Status));
        }
        catch (Exception ex) { Error("History load failed", ex); }
    }
    private async Task LoadSelectedAsync()
    {
        if (selected is null) return;
        try
        {
            selectedSnapshot = await store.LoadAsync(selected.Id); var s = selectedSnapshot;
            Detail = s is null ? "Historical snapshot unavailable." : $"Historical snapshot · {s.ObservedAtUtc:O}\n{s.Device.Model} · {s.Protocol}\nAssessment: {s.Assessment.StateDisplay}\nCoverage: {s.Assessment.Coverage}\nChecklist: {s.Assessment.ChecklistDisplay}\nOriginal rules: {s.RuleSetVersion}\nScope: {s.Scope}\nIdentity comparison: {(s.Device.IdentityReliable ? "eligible when key/scope match" : "disabled; identity evidence is uncertain")}\n\n{s.Assessment.Explanation}\n\n{Core.Assessment.HealthAssessment.Boundary}";
            Comparison = "No earlier compatible snapshot.";
            if (s?.Device.MatchKey is string key)
            {
                var rows = await store.ListAsync(0, 200, s.Protocol, key); var priorRow = rows.FirstOrDefault(r => r.ObservedAtUtc < s.ObservedAtUtc);
                if (priorRow is not null && await store.LoadAsync(priorRow.Id) is { } prior)
                { var diff = SnapshotComparison.Compare(prior, s); Comparison = diff.Count == 0 ? "Compatible session found; no compatible metric values." : string.Join("\n", diff.Select(d => $"{d.Key}: {d.Previous} → {d.Current}; {(d.Delta is null ? d.Status : "delta " + d.Delta)}")); }
            }
            Changed(nameof(Detail)); Changed(nameof(Comparison)); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh();
            FilterSelectedDeviceCommand.Refresh();
        }
        catch (Exception ex) { Error("History detail load failed", ex); }
    }
    private async Task CleanupAsync()
    { try { int removed = await store.CleanupAsync(DateTimeOffset.UtcNow, RetentionDays); if (removed > 0) await LoadPageAsync(true); } catch (Exception ex) { Error("History cleanup failed; live diagnostics unaffected", ex); } }
    private async Task ExportAsync(bool json)
    {
        if (selectedSnapshot is null) return;
        var dialog = new SaveFileDialog { Filter = json ? "JSON report (*.json)|*.json" : "Text report (*.txt)|*.txt", DefaultExt = json ? ".json" : ".txt", AddExtension = true, FileName = $"pixinit-{selectedSnapshot.Protocol}-{selectedSnapshot.ObservedAtUtc:yyyyMMdd-HHmmss}" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var options = new ExportOptions(ExportRedactSerial, ExportIncludeRawPayloads);
            if (json) await DiagnosticReportExporter.ExportJsonAsync(selectedSnapshot, dialog.FileName, options); else await DiagnosticReportExporter.ExportTextAsync(selectedSnapshot, dialog.FileName, options);
            Status = $"Exported {(ExportRedactSerial ? "serial-redacted" : "unredacted")} {(json ? "JSON" : "TXT")} report · raw payloads {(ExportIncludeRawPayloads ? "included" : "omitted")}"; Changed(nameof(Status));
        }
        catch (OperationCanceledException) { Status = "Export cancelled"; Changed(nameof(Status)); }
        catch (Exception ex) { Error("Export failed; snapshot retained", ex); }
    }
    private void Error(string message, Exception ex) { Status = $"{message}: {ex.Message}"; Changed(nameof(Status)); DiscoveryLog.Write(message, ex); }
}
