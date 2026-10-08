using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Views.Shared;
using pixinit.ViewModels.Nvme;

namespace pixinit.Tests;
internal static partial class Program
{
    private static void Until(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(25);
        while (!condition() && DateTime.UtcNow < end)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        if (!condition()) throw new TimeoutException("WebView UI condition timed out");
    }
    private static string Script(WebView2 web, string script)
    {
        var task = web.ExecuteScriptAsync(script); Until(() => task.IsCompleted); return task.GetAwaiter().GetResult();
    }
    private static void WebUiTests()
    {
        var app = new System.Windows.Application();
        var vm = new NvmeViewModel();
        var view = new WebOverview { UserDataFolder = Path.Combine(Path.GetTempPath(), "pixinit-web-tests", Guid.NewGuid().ToString("N")), DataContext = vm, Content = new TextBlock { Text = "Native fallback" }, Status = "Not queried · Select a drive" };
        var window = new Window { Title = "PIXINIT HTML overview verification — no hardware reads", Content = view, Width = 1366, Height = 768 };
        window.Show();
        var web = Find<WebView2>(window).Single();
        Until(() => view.InitializationError is not null || (web.CoreWebView2 is not null && web.CoreWebView2.Source == "about:blank"));
        if (view.InitializationError is not null) throw new Exception(view.InitializationError);
        Until(() => Script(web, "document.getElementById('metrics').children.length") == "9");
        Check(Script(web, "document.getElementById('assessment').textContent").Contains("unavailable"), "Real WebView renders unavailable data honestly");
        Check(Script(web, "document.querySelectorAll('.ring').length") == "0", "Missing data creates no percentage graphs");
        view.Busy = true; view.Status = "Reading health log"; Pump();
        Until(() => Script(web, "document.getElementById('activity').hidden") == "false");
        Check(Script(web, "document.getElementById('activity').hasAttribute('aria-valuenow')") == "false", "Read progress is indeterminate rather than fabricated percent");
        vm.SetDevice(Drive("html-test", StorageProtocol.Nvme) with { Model = "<img src=x onerror=alert(1)> TEST ONLY" });
        vm.Apply(new NvmeDiagnostics("html-test", Metric<byte>.Missing(), new(42, "°C", Availability.Available, "fixture", DateTimeOffset.UtcNow),
            new(0, "%", Availability.Available, "fixture", DateTimeOffset.UtcNow), new(125, "%", Availability.Available, "fixture", DateTimeOffset.UtcNow), null, null, null, null));
        view.Busy = false; view.Status = "TEST FIXTURE ONLY"; Pump();
        Until(() => Script(web, "document.querySelectorAll('.ring').length") == "2");
        Check(Script(web, "document.querySelectorAll('.ring')[1].style.getPropertyValue('--p')") == "\"100%\"" && Script(web, "document.getElementById('metrics').textContent").Contains("125"), "Consumed endurance over 100 retains exact label with bounded graph");
        Check(Script(web, "document.querySelectorAll('img').length") == "0" && Script(web, "document.getElementById('model').textContent").Contains("<img"), "Identity data renders as text, never HTML");
        vm.SetDevice(null); view.Status = "Not queried · No drive selected"; Pump();
        Until(() => Script(web, "document.querySelectorAll('.ring').length") == "0");
        Check(Script(web, "document.getElementById('metrics').textContent").Contains("Unavailable"), "Selection clears old numeric readings and graphs");
        Directory.CreateDirectory("docs/screenshots");
        foreach (var item in new[] { (1366d, 768d, "html-overview-laptop.png"), (1800d, 1000d, "html-overview-desktop.png") })
        {
            window.Width = item.Item1; window.Height = item.Item2; Pump();
            Thread.Sleep(200); Pump();
            using var output = File.Create(Path.Combine("docs/screenshots", item.Item3));
            var capture = web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output); Until(() => capture.IsCompleted); capture.GetAwaiter().GetResult();
            Check(Script(web, "document.documentElement.scrollWidth <= innerWidth") == "true", "HTML overview fits viewport " + item.Item1);
        }
        web.CoreWebView2.Navigate("https://example.com"); Pump();
        Until(() => Script(web, "location.href") == "\"about:blank\"");
        Check(web.CoreWebView2.Source == "about:blank", "External navigation remains blocked");
        window.Content = null; Pump();
        Check(view.Content is TextBlock, "Unloading disposes browser and restores native fallback");
        window.Content = view; Pump();
        web = Find<WebView2>(window).Single(); Until(() => web.CoreWebView2 is not null && web.CoreWebView2.Source == "about:blank");
        Until(() => Script(web, "document.getElementById('metrics')?.children.length") == "9");
        Check(Script(web, "document.getElementById('metrics').children.length") == "9", "Overview recreates after tab-style unload and reload");
        window.Close(); app.Shutdown();
    }
}
