namespace CasoE.Models;

internal sealed class OrderResult
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool RequiresAction { get; init; }

    public string? Reason { get; init; }
}
