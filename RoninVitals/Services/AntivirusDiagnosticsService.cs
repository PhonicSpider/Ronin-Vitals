using System.Diagnostics;
using System.IO;
using System.Management;
using RoninVitals.Models;
using static RoninVitals.Services.CheckText;

namespace RoninVitals.Services;

/// <summary>
/// Read-only list of the antivirus programs Windows Security Center knows about, whatever their maker. It never starts,
/// stops or changes a program. How the list is judged is decided in <see cref="AntivirusRules"/>.
/// </summary>
public static class AntivirusDiagnosticsService
{
    private const string TechCommand = "Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct";
    private const string MicrosoftMaker = "Microsoft Corporation";

    public static CheckResult CheckAntivirus()
    {
        try
        {
            var products = ReadProducts();
            return AntivirusRules.Judge(products) with
            {
                TechCommand = TechCommand,
                TechDetail = DescribeForTechnician(products),
                Tables = AntivirusRules.BuildTables(products),
            };
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException)
        {
            // Windows Server has no Security Center list, so this is expected there rather than a fault.
            return new CheckResult(CheckStatus.Warn,
                "Couldn't read the list of antivirus programs from Windows. Windows Server doesn't keep one; on other editions, open Windows Security to see what is protecting this PC.",
                TechCommand, DescribeException(exception));
        }
    }

    /// <summary>Throws <see cref="ManagementException"/> when Security Center isn't available (for example on Windows Server).</summary>
    internal static IReadOnlyList<AntivirusProduct> ReadProducts() =>
        AntivirusRules.Combine(ReadRegistrations())
            .Select(product => product with { Maker = ReadMaker(product) })
            .ToList();

    private static List<AntivirusRegistration> ReadRegistrations()
    {
        var registrations = new List<AntivirusRegistration>();
        using var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState, pathToSignedProductExe FROM AntiVirusProduct");
        foreach (ManagementBaseObject product in searcher.Get())
        {
            using (product)
            {
                registrations.Add(new AntivirusRegistration(
                    Name: product["displayName"] as string ?? "Unknown antivirus",
                    ProductState: Convert.ToInt32(product["productState"] ?? 0),
                    Location: product["pathToSignedProductExe"] as string));
            }
        }
        return registrations;
    }

    private static string? ReadMaker(AntivirusProduct product)
    {
        if (product.IsMicrosoft) return MicrosoftMaker; // its registered location is a windowsdefender:// link, not a file
        if (string.IsNullOrWhiteSpace(product.Location) || !Path.IsPathRooted(product.Location)) return null;
        try
        {
            var company = FileVersionInfo.GetVersionInfo(product.Location).CompanyName;
            return string.IsNullOrWhiteSpace(company) ? null : company;
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static string DescribeForTechnician(IReadOnlyList<AntivirusProduct> products) =>
        products.Count == 0
            ? "Security Center lists no antivirus products."
            : "Security Center: " + string.Join("; ", products.Select(product =>
                $"{product.Name} (state 0x{product.ProductState:X6}, {product.State.ToString().ToLowerInvariant()}, definitions {(product.DefinitionsOutOfDate ? "out of date" : "up to date")})"));
}
