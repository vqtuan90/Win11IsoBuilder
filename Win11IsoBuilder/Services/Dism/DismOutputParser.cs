using System.Globalization;
using System.Text.RegularExpressions;
using Win11IsoBuilder.Models;

namespace Win11IsoBuilder.Services.Dism;

/// <summary>One provisioned Appx package (bloatware candidate) from DISM.</summary>
public sealed record ProvisionedAppx(string DisplayName, string PackageName);

/// <summary>
/// Pure text parsing of DISM stdout. Kept free of process/IO concerns so it is
/// fully unit-testable against captured sample output.
/// </summary>
public static partial class DismOutputParser
{
    [GeneratedRegex(@"^\s*([A-Za-z ]+?)\s*:\s*(.*?)\s*$")]
    private static partial Regex KeyValueLine();

    /// <summary>
    /// Parse <c>dism /Get-ImageInfo /ImageFile:install.wim</c> into editions.
    /// Each "Index : N" begins a new block; following Name/Size lines fill it.
    /// </summary>
    public static List<WindowsEdition> ParseImageInfo(string text)
    {
        var editions = new List<WindowsEdition>();
        WindowsEdition? current = null;

        foreach (var raw in SplitLines(text))
        {
            var m = KeyValueLine().Match(raw);
            if (!m.Success) continue;

            var key = m.Groups[1].Value.Trim();
            var value = m.Groups[2].Value.Trim();

            switch (key.ToLowerInvariant())
            {
                case "index":
                    if (int.TryParse(value, out var idx))
                    {
                        current = new WindowsEdition { Index = idx };
                        editions.Add(current);
                    }
                    break;
                case "name":
                    if (current is not null) current.Name = value;
                    break;
                case "edition" or "editionid":
                    if (current is not null) current.Edition = value;
                    break;
                case "architecture":
                    if (current is not null) current.Architecture = value;
                    break;
                case "size":
                    if (current is not null) current.SizeBytes = ParseSize(value);
                    break;
            }
        }
        return editions;
    }

    /// <summary>
    /// Parse <c>dism /Get-ProvisionedAppxPackages</c> into DisplayName/PackageName pairs.
    /// PackageName is what /Remove-ProvisionedAppxPackage needs.
    /// </summary>
    public static List<ProvisionedAppx> ParseProvisionedAppx(string text)
    {
        var list = new List<ProvisionedAppx>();
        string? displayName = null;

        foreach (var raw in SplitLines(text))
        {
            var m = KeyValueLine().Match(raw);
            if (!m.Success) continue;

            var key = m.Groups[1].Value.Trim().ToLowerInvariant();
            var value = m.Groups[2].Value.Trim();

            if (key == "displayname") displayName = value;
            else if (key == "packagename" && !string.IsNullOrEmpty(value))
            {
                list.Add(new ProvisionedAppx(displayName ?? value, value));
                displayName = null;
            }
        }
        return list;
    }

    private static long ParseSize(string value)
    {
        // e.g. "15,895,704,123 bytes"
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n');
}
