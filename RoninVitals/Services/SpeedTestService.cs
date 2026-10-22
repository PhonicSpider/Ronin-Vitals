using System.Diagnostics;
using System.Net.Http;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Approximate download-throughput check: times a bounded download against Cloudflare's public
/// speed-test backend (the same endpoint several open-source speed-test tools use, no API key or
/// SDK needed) rather than bundling a vendor speed-test binary. Single connection, single server -
/// a genuine approximation, not a lab-grade multi-stream test like Ookla or Fast.com. Bounded by
/// both a byte cap and a timeout, so a slow connection still finishes quickly - the rate is
/// computed from whatever was downloaded before whichever limit hit first, which is exactly how a
/// bounded-window speed sample is supposed to work. Thresholds are generic broadband bands, not
/// tied to the user's actual plan, since Vitals has no way to know what they're subscribed to.
/// </summary>
public static class SpeedTestService
{
    private const string DownloadTestUrl = "https://speed.cloudflare.com/__down?bytes=10000000"; // 10 MB cap
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    public static CheckResult CheckDownloadSpeed()
    {
        var buffer = new byte[81920];
        long bytesRead = 0;
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new HttpClient { Timeout = RequestTimeout };
            using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, DownloadTestUrl), HttpCompletionOption.ResponseHeadersRead);
            using var stream = response.Content.ReadAsStream();

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                bytesRead += read;
        }
        catch
        {
            // Timed out, connection dropped mid-download, or couldn't connect at all - fall
            // through and judge by whatever bytesRead/elapsed accumulated before that happened.
        }
        sw.Stop();

        var techCommand = $"Invoke-WebRequest '{DownloadTestUrl}'";

        if (bytesRead == 0 || sw.Elapsed.TotalMilliseconds < 20)
            return new CheckResult(CheckStatus.Warn, "Couldn't run a speed test - check your internet connection.", techCommand, "0 bytes received before failure/timeout.");

        var mbps = bytesRead * 8.0 / 1_000_000.0 / sw.Elapsed.TotalSeconds;
        var techDetail = $"{bytesRead:N0} bytes in {sw.ElapsedMilliseconds} ms = {mbps:F2} Mb/s";

        return mbps switch
        {
            >= 25 => new CheckResult(CheckStatus.Pass, $"Getting {mbps:F0} Mb/s down - solid for typical browsing and streaming.", techCommand, techDetail),
            >= 5 => new CheckResult(CheckStatus.Warn, $"Getting {mbps:F0} Mb/s down - usable, but slow for HD streaming or multiple devices at once.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Getting {mbps:F1} Mb/s down - that's slow enough to notice on most everyday tasks.", techCommand, techDetail),
        };
    }
}
