using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using static RoninVitals.Services.WebAccessRules;

namespace RoninVitals.Services;

internal sealed record PopularSite(string Name, string Group, string Url);

internal sealed record CaptivePortalResult(bool Detected, string Detail);

/// <summary>Tries to reach web sites and reports how each attempt ended, including a "sign in to this network" page.</summary>
internal static class SiteProbe
{
    private const string ConnectivityTestUrl = "http://www.msftconnecttest.com/connecttest.txt"; // Windows' own "am I online" page
    private const int MaxRedirects = 5;
    private const int ServerErrorStatusCode = 500;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CaptivePortalTimeout = TimeSpan.FromSeconds(5);

    public static readonly IReadOnlyList<PopularSite> PopularSites =
    [
        new("Google", "General", "https://www.google.com"),
        new("Microsoft", "General", "https://www.microsoft.com"),
        new("Cloudflare", "General", "https://www.cloudflare.com"),
        new("Steam", "Gaming", "https://store.steampowered.com"),
        new("Epic Games", "Gaming", "https://store.epicgames.com"),
        new("Xbox", "Gaming", "https://www.xbox.com"),
        new("Battle.net", "Gaming", "https://battle.net"),
        new("Riot Games", "Gaming", "https://www.riotgames.com"),
        new("EA", "Gaming", "https://www.ea.com"),
        new("Discord", "Gaming", "https://discord.com"),
    ];

    public static HttpClient CreateClient(bool followRedirects, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            MaxAutomaticRedirections = MaxRedirects,
            ConnectTimeout = ConnectTimeout,
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        // Several of these sites refuse a request that has no browser-like agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) RoninVitals/0.1");
        return client;
    }

    public static async Task<SiteResult> ProbeAsync(HttpClient client, PopularSite site)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, site.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var statusCode = (int)response.StatusCode;

            // Any real answer proves the site is reachable; HEAD is often refused with 403/405 and that still counts as loaded.
            var outcome = statusCode >= ServerErrorStatusCode ? SiteOutcome.ServerError : SiteOutcome.Loaded;
            return new SiteResult(site.Name, site.Group, outcome, stopwatch.ElapsedMilliseconds, $"HTTP {statusCode}");
        }
        catch (TaskCanceledException)
        {
            return new SiteResult(site.Name, site.Group, SiteOutcome.TimedOut, stopwatch.ElapsedMilliseconds, "timed out");
        }
        catch (HttpRequestException exception)
        {
            var outcome = ClassifyFailure(exception);
            var detail = outcome switch
            {
                SiteOutcome.DnsFailed => "DNS lookup failed",
                SiteOutcome.TlsFailed => "secure connection (TLS) failed",
                SiteOutcome.TimedOut => "timed out",
                _ => exception.InnerException?.Message ?? exception.Message,
            };
            return new SiteResult(site.Name, site.Group, outcome, stopwatch.ElapsedMilliseconds, detail);
        }
    }

    private static SiteOutcome ClassifyFailure(HttpRequestException exception) => exception.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => SiteOutcome.DnsFailed,
        HttpRequestError.SecureConnectionError => SiteOutcome.TlsFailed,
        _ => exception.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut } ? SiteOutcome.TimedOut : SiteOutcome.OtherFailure,
    };

    /// <summary>A sign-in page (hotel, cafe, campus Wi-Fi) makes every site fail until it is accepted. No answer at all tells us nothing, so it counts as "not detected".</summary>
    public static async Task<CaptivePortalResult> DetectCaptivePortalAsync()
    {
        try
        {
            using var client = CreateClient(followRedirects: false, CaptivePortalTimeout);
            using var response = await client.GetAsync(ConnectivityTestUrl);
            var body = await response.Content.ReadAsStringAsync();

            var statusCode = (int)response.StatusCode;
            var detected = LooksLikeCaptivePortal(statusCode, body);
            return new CaptivePortalResult(detected, $"connecttest.txt -> HTTP {statusCode}, {(detected ? "not the expected page" : "expected page")}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new CaptivePortalResult(false, $"connecttest.txt unreachable ({exception.GetType().Name}).");
        }
    }
}
