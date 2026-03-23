namespace CasoE.Models;

internal sealed class RefundResult
{
    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? OrderId { get; init; }

    public string? RefundReason { get; init; }
}
