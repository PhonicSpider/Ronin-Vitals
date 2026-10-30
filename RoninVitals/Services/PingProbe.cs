using System.Net.NetworkInformation;

namespace RoninVitals.Services;

/// <summary>ICMP ping helpers. A lost or failed ping is <c>null</c>, never an exception.</summary>
internal static class PingProbe
{
    public static long? MeasureMilliseconds(string host, int timeoutMilliseconds = 1500)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, timeoutMilliseconds);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }

    /// <summary>Pings <paramref name="host"/> in order, keeping lost pings as <c>null</c> so loss and spikes both show.</summary>
    public static List<long?> Sample(string host, int count, int timeoutMilliseconds, int pauseMilliseconds = 0)
    {
        var roundTrips = new List<long?>(count);
        for (var attempt = 0; attempt < count; attempt++)
        {
            roundTrips.Add(MeasureMilliseconds(host, timeoutMilliseconds));
            if (pauseMilliseconds > 0) Thread.Sleep(pauseMilliseconds);
        }
        return roundTrips;
    }

    /// <summary>Average of the pings that got a reply, or <c>null</c> if none did.</summary>
    public static long? AverageMilliseconds(IEnumerable<long?> roundTrips)
    {
        var replies = roundTrips.OfType<long>().ToList();
        return replies.Count == 0 ? null : (long)replies.Average();
    }
}
