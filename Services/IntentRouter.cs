using CasoE.Models;
using System.Text.RegularExpressions;

namespace CasoE.Services;

internal sealed class IntentRouter
{
    private static readonly string[] RejectActionKeywords =
    [
        "delete",
        "remove",
        "erase",
        "purge",
        "wipe",
        "destroy",
        "drop",
    ];

    private static readonly string[] RejectTargetKeywords =
    [
        "all orders",
        "every order",
        "all the orders",
        "entire order history",
        "all order history",
        "all customer orders",
    ];

    private static readonly string[] RefundKeywords =
    [
        "refund",
        "return",
        "reimbursement",
        "money back",
    ];

    private static readonly string[] OrderKeywords =
    [
        "order",
        "shipment",
        "shipping",
        "tracking",
        "track",
        "status",
        "details",
        "where is",
        "where's",
        "where my",
        "arrive",
        "delivery",
    ];

    private static readonly Regex RefundReasonRegex = new(
        @"\b(?:because|since|due to)\b\s+(?<reason>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RefundReasonForRegex = new(
        @"\bfor\b\s+(?<reason>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingOrderContextRegex = new(
        @"^(?:order\s+[A-Z0-9-]+\s+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly OrderIdExtractor _orderIdExtractor;

    public IntentRouter(OrderIdExtractor orderIdExtractor)
    {
        _orderIdExtractor = orderIdExtractor;
    }

    public RouteDecision Route(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new RouteDecision(
                RouteKind.Clarify,
                prompt ?? string.Empty,
                null,
                null,
                "The request is empty. Ask for the order or refund question in one sentence.");
        }

        string normalized = prompt.Trim();
        string lowered = normalized.ToLowerInvariant();
        string? orderId = _orderIdExtractor.Extract(normalized);
        string? refundReason = ExtractRefundReason(normalized);

        if (ContainsAny(lowered, RejectActionKeywords) && ContainsAny(lowered, RejectTargetKeywords))
        {
            return new RouteDecision(
                RouteKind.Reject,
                normalized,
                orderId,
                refundReason,
                "Destructive mass order operation requested.");
        }

        if (ContainsAny(lowered, RefundKeywords))
        {
            return new RouteDecision(
                RouteKind.Refund,
                normalized,
                orderId,
                refundReason,
                "Refund keywords detected.");
        }

        bool orderIntent = ContainsAny(lowered, OrderKeywords);
        if (orderIntent && !string.IsNullOrWhiteSpace(orderId))
        {
            return new RouteDecision(
                RouteKind.Order,
                normalized,
                orderId,
                refundReason,
                "Order-status intent with order id detected.");
        }

        if (orderIntent)
        {
            return new RouteDecision(
                RouteKind.Clarify,
                normalized,
                orderId,
                refundReason,
                "Missing orderId for an order-related request.");
        }

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            return new RouteDecision(
                RouteKind.Clarify,
                normalized,
                orderId,
                refundReason,
                "The request includes an orderId but does not clearly ask for order status or refund help.");
        }

        return new RouteDecision(
            RouteKind.Reject,
            normalized,
            null,
            null,
            "Prompt is outside the supported order and refund scope.");
    }

    private static string? ExtractRefundReason(string prompt)
    {
        Match directReason = RefundReasonRegex.Match(prompt);
        if (directReason.Success)
        {
            return CleanReason(directReason.Groups["reason"].Value);
        }

        Match forReason = RefundReasonForRegex.Match(prompt);
        if (!forReason.Success)
        {
            return null;
        }

        string candidate = LeadingOrderContextRegex.Replace(forReason.Groups["reason"].Value, string.Empty);
        return CleanReason(candidate);
    }

    private static bool ContainsAny(string input, IEnumerable<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            if (input.Contains(candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? CleanReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        string cleaned = reason.Trim().TrimEnd('.', '!', '?', ';', ':');
        return cleaned.Length == 0
            ? null
            : cleaned;
    }
}
