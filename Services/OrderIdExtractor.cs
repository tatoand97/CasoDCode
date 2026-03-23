using System.Text.RegularExpressions;

namespace CasoE.Services;

internal sealed class OrderIdExtractor
{
    private static readonly Regex PrefixedOrderIdRegex = new(
        @"\bORD[- ]?\d{3,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NumericOrderIdRegex = new(
        @"\b\d{4,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DigitsRegex = new(
        @"\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string? Extract(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        Match prefixedMatch = PrefixedOrderIdRegex.Match(prompt);
        if (prefixedMatch.Success)
        {
            Match digitsMatch = DigitsRegex.Match(prefixedMatch.Value);
            return digitsMatch.Success
                ? $"ORD-{digitsMatch.Value}"
                : prefixedMatch.Value.ToUpperInvariant().Replace(' ', '-');
        }

        Match numericMatch = NumericOrderIdRegex.Match(prompt);
        return numericMatch.Success
            ? numericMatch.Value
            : null;
    }
}
