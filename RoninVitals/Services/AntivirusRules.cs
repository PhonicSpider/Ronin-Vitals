using RoninVitals.Models;
using static RoninVitals.Services.CheckText;

namespace RoninVitals.Services;

public enum AntivirusState { On, Off, Snoozed, Expired }

/// <summary>One entry as Windows Security Center keeps it. The same product can be registered more than once.</summary>
public sealed record AntivirusRegistration(string Name, int ProductState, string? Location);

public sealed record AntivirusProduct(string Name, AntivirusState State, bool DefinitionsOutOfDate, string? Location, int ProductState, string? Maker = null)
{
    public bool IsOn => State == AntivirusState.On;
    public bool IsMicrosoft => AntivirusRules.IsMicrosoftDefender(Name);
}

/// <summary>
/// Turns what Windows Security Center lists into a verdict and a report table, with no machine access so it can be tested
/// with sample data. The list is what products tell Windows about themselves: it names the antivirus whatever it is, but
/// it can't prove the product works, and a product that doesn't register won't be on it.
/// </summary>
public static class AntivirusRules
{
    private const int StateMask = 0xF000;
    private const int OnState = 0x1000;
    private const int SnoozedState = 0x2000;
    private const int ExpiredState = 0x3000;
    private const int DefinitionsMask = 0x00F0;
    private const int OutOfDateDefinitions = 0x0010;

    /// <summary>
    /// Security Center lists Microsoft's own product as "Windows Defender" or "Microsoft Defender Antivirus". Matching the word
    /// anywhere in the name would also swallow Bitdefender, so only those two prefixes count.
    /// </summary>
    public static bool IsMicrosoftDefender(string productName) =>
        productName.StartsWith("Windows Defender", StringComparison.OrdinalIgnoreCase)
        || productName.StartsWith("Microsoft Defender", StringComparison.OrdinalIgnoreCase);

    /// <summary>Security Center's productState keeps on, snoozed and expired in bits 12-15; anything else is off.</summary>
    public static AntivirusState ReadState(int productState) => (productState & StateMask) switch
    {
        OnState => AntivirusState.On,
        SnoozedState => AntivirusState.Snoozed,
        ExpiredState => AntivirusState.Expired,
        _ => AntivirusState.Off,
    };

    /// <summary>Bits 4-7 of productState: 0 is up to date, 1 is out of date.</summary>
    public static bool AreDefinitionsOutOfDate(int productState) => (productState & DefinitionsMask) == OutOfDateDefinitions;

    /// <summary>One product per name, since Security Center can list the same one several times. The most protective entry wins.</summary>
    public static IReadOnlyList<AntivirusProduct> Combine(IEnumerable<AntivirusRegistration> registrations) =>
        registrations
            .GroupBy(registration => registration.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .Select(registration => (Registration: registration, State: ReadState(registration.ProductState), OutOfDate: AreDefinitionsOutOfDate(registration.ProductState)))
                    .OrderBy(entry => entry.State)
                    .ThenBy(entry => entry.OutOfDate)
                    .First();
                var location = group.Select(registration => registration.Location).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
                return new AntivirusProduct(best.Registration.Name, best.State, best.OutOfDate, location, best.Registration.ProductState);
            })
            .OrderBy(product => product.IsOn ? 0 : 1)
            .ThenBy(product => product.IsMicrosoft ? 1 : 0)
            .ThenBy(product => product.Name, StringComparer.Ordinal)
            .ToList();

    public static CheckResult Judge(IReadOnlyList<AntivirusProduct> products)
    {
        if (products.Count == 0)
            return new CheckResult(CheckStatus.Bad, "Windows doesn't list any antivirus on this PC, so nothing it knows of is watching for threats. An antivirus that doesn't register itself with Windows won't show up here.");

        var active = products.Where(product => product.IsOn).ToList();
        if (active.Count == 0)
            return new CheckResult(CheckStatus.Bad, $"No antivirus is protecting this PC right now: {string.Join(" and ", products.Select(DescribeInactive))}.");

        var thirdParty = active.Where(product => !product.IsMicrosoft).ToList();
        if (thirdParty.Count > 1)
            return new CheckResult(CheckStatus.Warn, $"{JoinNames(thirdParty)} are all switched on. Two antivirus programs running together can slow the PC and interfere with each other; keep one and remove the other.");

        var yours = thirdParty.Count > 0 ? thirdParty : active;
        var outOfDate = yours.Where(product => product.DefinitionsOutOfDate).ToList();
        if (outOfDate.Count > 0)
            return new CheckResult(CheckStatus.Warn, $"{JoinNames(outOfDate)} {Plural(outOfDate.Count, "is", "are")} switched on, but the virus definitions are out of date, so newer threats can slip past. Open the program and update it.");

        return new CheckResult(CheckStatus.Pass, $"{JoinNames(yours)} {Plural(yours.Count, "is", "are")} your antivirus: switched on, with up-to-date virus definitions.");
    }

    public static IReadOnlyList<ReportTable> BuildTables(IReadOnlyList<AntivirusProduct> products)
    {
        if (products.Count == 0) return [];

        var anyActive = products.Any(product => product.IsOn);
        var otherAntivirusActive = products.Any(product => product.IsOn && !product.IsMicrosoft);
        var rows = products.Select(product => new ReportRow(
            [product.Name, DescribeState(product.State), product.DefinitionsOutOfDate ? "Out of date" : "Up to date", product.Maker ?? "Unknown", product.Location ?? "Not given"],
            RowEmphasis(product, anyActive, otherAntivirusActive))).ToList();

        return
        [
            new ReportTable(
                Title: "Antivirus programs Windows knows about",
                Columns: ["Antivirus", "Status", "Virus definitions", "Maker", "Location"],
                Rows: rows,
                Note: "This is the list Windows Security keeps. A program appears here because it told Windows about itself, which doesn't prove it is working, and an antivirus that doesn't register itself won't be listed. Microsoft's own Defender is normally switched off or on standby while another antivirus is on.")
        ];
    }

    private static ReportRowEmphasis RowEmphasis(AntivirusProduct product, bool anyActive, bool otherAntivirusActive)
    {
        if (product.IsMicrosoft && otherAntivirusActive) return ReportRowEmphasis.Muted; // Windows lists Defender as on while it stands by
        if (product.IsOn) return product.DefinitionsOutOfDate ? ReportRowEmphasis.Notable : ReportRowEmphasis.Normal;
        return anyActive ? ReportRowEmphasis.Muted : ReportRowEmphasis.Notable;
    }

    private static string DescribeState(AntivirusState state) => state switch
    {
        AntivirusState.On => "On",
        AntivirusState.Snoozed => "Paused",
        AntivirusState.Expired => "Expired",
        _ => "Off",
    };

    private static string DescribeInactive(AntivirusProduct product) => product.State switch
    {
        AntivirusState.Snoozed => $"{product.Name} is paused",
        AntivirusState.Expired => $"{product.Name} has expired",
        _ => $"{product.Name} is switched off",
    };

    /// <summary>"A", "A and B", "A, B and C".</summary>
    private static string JoinNames(IReadOnlyList<AntivirusProduct> products)
    {
        var names = products.Select(product => product.Name).ToList();
        return names.Count <= 1 ? string.Concat(names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }
}
