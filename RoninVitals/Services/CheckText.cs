namespace RoninVitals.Services;

/// <summary>Small wording helpers shared by the checks that build plain-language results.</summary>
internal static class CheckText
{
    public static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;

    /// <summary>"a, b, c and 4 more": the first few distinct items, then how many were left out.</summary>
    public static string Examples(IEnumerable<string> items, int maxShown = 3)
    {
        var distinct = items.Distinct().ToList();
        var shown = string.Join(", ", distinct.Take(maxShown));
        return distinct.Count > maxShown ? $"{shown} and {distinct.Count - maxShown} more" : shown;
    }

    public static string DescribeException(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";
}
