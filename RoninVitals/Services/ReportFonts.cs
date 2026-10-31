using System.Collections;
using System.IO;
using System.Reflection;
using System.Resources;

namespace RoninVitals.Services;

/// <summary>
/// Builds the <c>@font-face</c> rules that let a report use the app's own display and data fonts (both SIL Open Font License,
/// which allows embedding) on a PC that doesn't have them installed. Reads the fonts the app already carries as resources, so
/// the report stays one self-contained file. Returns nothing when a font can't be found, and the report falls back to system fonts.
/// </summary>
internal static class ReportFonts
{
    private static readonly (string ResourceName, string Family, int Weight)[] Embedded =
    [
        ("assets/fonts/spacegrotesk-semibold.ttf", "Space Grotesk", 600),
        ("assets/fonts/ibmplexmono-regular.ttf", "IBM Plex Mono", 400),
    ];

    public static string BuildFontFaceCss()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var resourceStream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.g.resources");
            if (resourceStream is null) return string.Empty;

            var fontsByName = ReadFonts(resourceStream);
            var rules = Embedded
                .Where(font => fontsByName.ContainsKey(font.ResourceName))
                .Select(font => $"@font-face {{ font-family: \"{font.Family}\"; font-weight: {font.Weight}; font-style: normal; font-display: swap; " +
                                $"src: url(data:font/ttf;base64,{Convert.ToBase64String(fontsByName[font.ResourceName])}) format(\"truetype\"); }}");
            return string.Join(Environment.NewLine, rules);
        }
        catch (Exception exception) when (exception is IOException or MissingManifestResourceException or ArgumentException)
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, byte[]> ReadFonts(Stream resourceStream)
    {
        var wanted = Embedded.Select(font => font.ResourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fonts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        using var reader = new ResourceReader(resourceStream);
        foreach (DictionaryEntry entry in reader)
        {
            if (entry.Key is not string name || !wanted.Contains(name)) continue;

            switch (entry.Value)
            {
                case byte[] bytes:
                    fonts[name] = bytes;
                    break;
                case Stream stream:
                    using (var copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        fonts[name] = copy.ToArray();
                    }
                    break;
            }
        }
        return fonts;
    }
}
