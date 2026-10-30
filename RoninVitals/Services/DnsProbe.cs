using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static RoninVitals.Services.WebAccessRules;

namespace RoninVitals.Services;

internal sealed record DnsLookup(bool Succeeded, long Milliseconds, string Detail);

/// <summary>Looks up a host name two ways, through this PC's own DNS and by asking a public DNS server directly, so the answers can be compared.</summary>
internal static class DnsProbe
{
    public const string PublicResolver = "1.1.1.1";

    private const int DnsPort = 53;
    private const int PublicLookupTimeoutMilliseconds = 3000;
    private static readonly TimeSpan SystemLookupTimeout = TimeSpan.FromSeconds(5);

    public static DnsLookup ResolveWithSystemDns(string hostName)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var lookup = Dns.GetHostAddressesAsync(hostName);
            if (!lookup.Wait(SystemLookupTimeout)) return new DnsLookup(false, stopwatch.ElapsedMilliseconds, "timed out");

            var addresses = lookup.Result;
            return addresses.Length > 0
                ? new DnsLookup(true, stopwatch.ElapsedMilliseconds, string.Join("/", addresses.Take(2)))
                : new DnsLookup(false, stopwatch.ElapsedMilliseconds, "no address returned");
        }
        catch (AggregateException exception) when (exception.InnerException is SocketException socketException)
        {
            return new DnsLookup(false, stopwatch.ElapsedMilliseconds, socketException.SocketErrorCode.ToString());
        }
        catch (AggregateException exception)
        {
            return new DnsLookup(false, stopwatch.ElapsedMilliseconds, exception.InnerException?.GetType().Name ?? "lookup failed");
        }
    }

    public static DnsLookup ResolveWithPublicDns(string hostName)
    {
        var transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = PublicLookupTimeoutMilliseconds;
            udpClient.Client.SendTimeout = PublicLookupTimeoutMilliseconds;
            udpClient.Connect(PublicResolver, DnsPort);

            var query = BuildDnsQuery(hostName, transactionId);
            udpClient.Send(query, query.Length);

            IPEndPoint? sender = null;
            var reply = ParseDnsReply(udpClient.Receive(ref sender), transactionId);
            var detail = reply.Valid ? $"{DescribeResponseCode(reply.ResponseCode)}, {reply.AnswerCount} answer(s)" : "unrecognized reply";
            return new DnsLookup(reply.Resolved, stopwatch.ElapsedMilliseconds, detail);
        }
        catch (SocketException exception)
        {
            var detail = exception.SocketErrorCode == SocketError.TimedOut ? "no reply (UDP port 53 may be blocked)" : exception.SocketErrorCode.ToString();
            return new DnsLookup(false, stopwatch.ElapsedMilliseconds, detail);
        }
    }
}
