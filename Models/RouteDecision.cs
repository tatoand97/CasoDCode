namespace CasoE.Models;

internal enum RouteKind
{
    Order,
    Refund,
    Clarify,
    Reject,
}

internal sealed record RouteDecision(
    RouteKind Route,
    string Prompt,
    string? OrderId,
    string? RefundReason,
    string Reason)
{
    public string RouteLabel =>
        Route switch
        {
            RouteKind.Order => "order",
            RouteKind.Refund => "refund",
            RouteKind.Clarify => "clarify",
            RouteKind.Reject => "reject",
            _ => Route.ToString().ToLowerInvariant(),
        };
}
