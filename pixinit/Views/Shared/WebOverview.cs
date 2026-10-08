using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using pixinit.Core.Diagnostics.Common;
using pixinit.ViewModels.Shared;
using pixinit.ViewModels.Nvme;
using pixinit.ViewModels.Sata;

namespace pixinit.Views.Shared;

// Presentation only: no host objects, incoming commands, or storage operations.
public sealed class WebOverview : ContentControl
{
    public static readonly DependencyProperty BusyProperty = DependencyProperty.Register(nameof(Busy), typeof(bool), typeof(WebOverview), new PropertyMetadata(false, Refresh));
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(string), typeof(WebOverview), new PropertyMetadata("", Refresh));
    public bool Busy { get => (bool)GetValue(BusyProperty); set => SetValue(BusyProperty, value); }
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    private WebView2? browser;
    private object? fallback;
    private INotifyPropertyChanged? observed;
    private bool ready, queued;
    private int generation;
    internal string UserDataFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PIXINIT", "WebView2");
    internal string? InitializationError { get; private set; }
    public WebOverview()
    {
        Loaded += Start;
        Unloaded += Stop;
        DataContextChanged += (_, _) => { Observe(); QueueUpdate(); };
    }
    private static void Refresh(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WebOverview)d).QueueUpdate();
    private void Observe()
    {
        if (observed is not null) observed.PropertyChanged -= Changed;
        observed = IsLoaded ? DataContext as INotifyPropertyChanged : null;
        if (observed is not null) observed.PropertyChanged += Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) => QueueUpdate();
    private async void Start(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this) || browser is not null) return;
        Observe();
        InitializationError = null;
        int attempt = ++generation;
        fallback = Content; Content = null;
        var grid = new Grid();
        var native = new ContentPresenter { Content = fallback };
        grid.Children.Add(native);
        var web = browser = new WebView2 { Visibility = Visibility.Visible };
        grid.Children.Add(web); Content = grid;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
            if (attempt != generation) return;
            await web.EnsureCoreWebView2Async(environment);
            if (attempt != generation) return;
            var core = web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += (_, args) => { if (args.Uri != "about:blank") args.Cancel = true; };
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.ProcessFailed += (_, _) => { ready = false; web.Visibility = Visibility.Hidden; native.Visibility = Visibility.Visible; };
            core.NavigationCompleted += (_, args) =>
            {
                if (attempt != generation || !args.IsSuccess) return;
                ready = true; native.Visibility = Visibility.Collapsed; web.Visibility = Visibility.Visible; QueueUpdate();
            };
            using var stream = typeof(WebOverview).Assembly.GetManifestResourceStream("pixinit.Overview.html") ?? throw new IOException("Overview resource missing");
            using var reader = new StreamReader(stream);
            core.NavigateToString(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (attempt != generation) return;
            InitializationError = ex.ToString();
            ready = false; web.Visibility = Visibility.Hidden;
            native.Visibility = Visibility.Visible;
            native.ToolTip = "HTML overview unavailable; using the native overview. " + ex.GetType().Name;
        }
    }
    private void Stop(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        generation++; ready = false;
        if (observed is not null) observed.PropertyChanged -= Changed;
        observed = null;
        if (browser is null) return;
        browser.Dispose(); browser = null;
        if (Content is Grid grid)
        {
            foreach (var presenter in grid.Children.OfType<ContentPresenter>()) presenter.Content = null;
            grid.Children.Clear();
        }
        Content = fallback; fallback = null;
    }
    private void QueueUpdate()
    {
        if (queued || !IsLoaded) return;
        queued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            queued = false;
            if (ready && browser?.CoreWebView2 is { } core)
                core.PostWebMessageAsJson(JsonSerializer.Serialize(OverviewSnapshot.Create(DataContext as DeviceViewModel, Busy, Status)));
        }));
    }
}

internal sealed record OverviewMetric(string Label, string Value, double? Percent = null);
internal sealed record OverviewSnapshot(string Protocol, string Model, string Serial, string State, string Assessment, string Coverage,
    string Observed, string Warning, string Evidence, bool Busy, string Status, OverviewMetric[] Metrics)
{
    private static double? Percent(Metric<double>? metric) => metric?.Availability == Availability.Available && metric.Value is double value && double.IsFinite(value) && value >= 0 ? value : null;
    internal static OverviewSnapshot Create(DeviceViewModel? vm, bool busy, string status) => vm switch
    {
        NvmeViewModel n => new(n.ProtocolDisplay, n.Model, n.Serial, n.Result?.Assessment?.State.ToString() ?? "Unknown", n.Assessment, n.Coverage,
            n.ObservedLocal, n.WarningSummary + "\n" + n.IdentityStatus, n.DataStatus + "\n" + n.MediaEvidence + "\n" + n.Scope, busy, status,
            [new("Temperature", n.Temperature), new("Available spare", n.AvailableSpare, Percent(n.Result?.AvailableSpare)),
             new("Endurance consumed", n.PercentageUsed, Percent(n.Result?.PercentageUsed)), new("Host reads", n.HostReads), new("Host writes", n.HostWrites),
             new("Power-on time", n.PowerOnHours), new("Power cycles", n.PowerCycles), new("Unsafe shutdowns", n.UnsafeShutdowns), new("Media errors", n.MediaErrors)]),
        SataViewModel s => new(s.ProtocolDisplay, s.Model, s.Serial, s.Result?.Assessment?.State.ToString() ?? "Unknown", s.Assessment, s.Coverage,
            s.ObservedLocal, s.SmartExplanation + "\n" + s.Discrepancies, s.DataStatus + "\n" + s.MediaEvidence, busy, status,
            [new("SMART status", s.SmartStatus), new("Temperature", s.Temperature), new("Verified wear", s.Wear)]),
        _ => new("Not established", "No drive selected", "Unavailable", "Unknown", "Assessment unavailable", "Checklist coverage unavailable", "Not observed", "No readings", "Select a drive", busy, status, [])
    };
}
