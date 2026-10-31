using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using RoninVitals.ViewModels;

namespace RoninVitals.Views;

/// <summary>
/// Shows a report's HTML in the app using the Edge WebView2 control. The page is served from memory under a made-up address,
/// so nothing is written to disk, and the browser is locked down: no scripts, no menus, no downloads, no new windows, and no
/// way to leave the report. The report's own content policy also forbids loading anything from outside it.
/// </summary>
public partial class ReportPreviewView : UserControl
{
    private const string ReportAddress = "https://ronin-vitals-report.invalid/report.html";
    private const string ReportFilter = ReportAddress;
    private const string BlankPage = "about:blank";

    private static readonly string UserDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ronin Vitals", "WebView2");

    private bool _started;

    public ReportPreviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => ReportBrowser.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
    {
        GoBackButton.Focus();
        if (_started || DataContext is not ReportPreviewViewModel preview) return;
        _started = true;
        await ShowReportAsync(preview);
    }

    private async Task ShowReportAsync(ReportPreviewViewModel preview)
    {
        try
        {
            if (FindResource("Vitals.Surface.Brush") is SolidColorBrush surface)
                ReportBrowser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(surface.Color.A, surface.Color.R, surface.Color.G, surface.Color.B);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
            await ReportBrowser.EnsureCoreWebView2Async(environment);
            LockDown(ReportBrowser.CoreWebView2);
            ServeFromMemory(ReportBrowser.CoreWebView2, environment, preview.Html);
            ReportBrowser.CoreWebView2.Navigate(ReportAddress);
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException or IOException)
        {
            // Saving still works, so the user can open the saved file in their own browser.
            ReportBrowser.Visibility = Visibility.Collapsed;
            LoadingText.Text = "The report can't be shown here because Microsoft Edge WebView2 isn't available on this PC. " +
                               "You can still save it and open the file in your browser.";
        }
    }

    private static void LockDown(CoreWebView2 browser)
    {
        var settings = browser.Settings;
        settings.IsScriptEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false; // keeps Ctrl+S and Ctrl+P from saving or printing around the Save button
        settings.IsStatusBarEnabled = false;
        settings.IsWebMessageEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;

        browser.NavigationStarting += (_, args) =>
        {
            if (args.Uri != ReportAddress && args.Uri != BlankPage) args.Cancel = true;
        };
        browser.NewWindowRequested += (_, args) => args.Handled = true;
        browser.DownloadStarting += (_, args) => args.Cancel = true;
    }

    private static void ServeFromMemory(CoreWebView2 browser, CoreWebView2Environment environment, string html)
    {
        var page = Encoding.UTF8.GetBytes(html);
        browser.AddWebResourceRequestedFilter(ReportFilter, CoreWebView2WebResourceContext.All);
        browser.WebResourceRequested += (_, args) =>
        {
            if (args.Request.Uri != ReportAddress) return;
            args.Response = environment.CreateWebResourceResponse(new MemoryStream(page), 200, "OK", "Content-Type: text/html; charset=utf-8");
        };
    }
}
