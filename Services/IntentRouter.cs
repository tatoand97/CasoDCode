using CasoE.Models;
using System.Text.RegularExpressions;

namespace CasoE.Services;

internal sealed class IntentRouter
{
    private static readonly Regex DestructiveVerbRegex = new(
        @"\b(?:delete|remove|erase|purge|wipe|destroy|drop|cancel)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MassTargetRegex = new(
        @"\b(?:all orders|every order|all the orders|entire order history|all order history|all customer orders)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitOrderReferenceRegex = new(
        @"\borders?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AdministrativeActionRegex = new(
        @"\b(?:update|change|modify|edit|set|reset|close|disable|enable|create|add|register|export|download|list|show)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AdministrativeTargetRegex = new(
        @"\b(?:account|profile|password|email|phone|address|payment(?:\s+method)?|card|customer|user|permissions?|roles?|order history)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DirectRefundRegex = new(
        @"\b(?:refund|reimbursement)\b|money\s+back",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ReturnKeywordRegex = new(
        @"\breturn(?:ed|ing)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ReturnIntentRegex = new(
        @"\b(?:want|need|would\s+like|like|can|could|help)\s+to\s+return\b|\breturn\b\s+(?:(?:my|the|this)\s+)?(?:order|item|package|product|purchase)\b|\bstart\b\s+(?:a\s+)?return\b|^\s*return\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ReturnCommerceContextRegex = new(
        @"\b(?:order|item|package|product|purchase)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ReturnNonRefundRegex = new(
        @"\breturn\b.*\b(?:status|details|tracking|shipment|shipping)\b|\breturn\s+(?:the\s+)?(?:order\s+)?(?:status|details|tracking|shipment|shipping)\b|\bwhen\s+will\b.*\border\b.*\breturn\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        @"^(?:(?:my|the|this)\s+)?(?:order|item|package|product|purchase)\s+(?:ORD[- ]?\d{3,}|\d{4,})\b[\s,:-]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex StandaloneOrderIdRegex = new(
        @"^(?:ORD[- ]?\d{3,}|\d{4,})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex StandaloneOrderReferenceRegex = new(
        @"^(?:(?:my|the|this)\s+)?(?:order|item|package|product|purchase)(?:\s+(?:ORD[- ]?\d{3,}|\d{4,}))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NonRefundReasonContextRegex = new(
        @"\b(?:status|details|tracking|shipment|shipping)\b",
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

        if (IsDestructiveMassOperation(normalized))
        {
            return new RouteDecision(
                RouteKind.Reject,
                normalized,
                orderId,
                refundReason,
                "Destructive mass order operation requested.");
        }

        if (IsDestructiveOrderOperation(normalized, orderId))
        {
            return new RouteDecision(
                RouteKind.Reject,
                normalized,
                orderId,
                refundReason,
                "Destructive order operation requested for explicit order reference.");
        }

        if (IsAdministrativeOutOfScopeRequest(normalized))
        {
            return new RouteDecision(
                RouteKind.Reject,
                normalized,
                orderId,
                refundReason,
                "Administrative request is outside the supported order/refund scope.");
        }

        if (HasRefundIntent(normalized, orderId))
        {
            return new RouteDecision(
                RouteKind.Refund,
                normalized,
                orderId,
                refundReason,
                "Refund keywords detected.");
        }

        bool orderIntent = ContainsAnyPhrase(lowered, OrderKeywords);
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

    private static bool IsDestructiveMassOperation(string prompt)
    {
        return DestructiveVerbRegex.IsMatch(prompt) && MassTargetRegex.IsMatch(prompt);
    }

    private static bool IsDestructiveOrderOperation(string prompt, string? orderId)
    {
        if (!DestructiveVerbRegex.IsMatch(prompt))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(orderId) || ExplicitOrderReferenceRegex.IsMatch(prompt);
    }

    private static bool IsAdministrativeOutOfScopeRequest(string prompt)
    {
        return AdministrativeActionRegex.IsMatch(prompt) && AdministrativeTargetRegex.IsMatch(prompt);
    }

    private static bool HasRefundIntent(string prompt, string? orderId)
    {
        if (DirectRefundRegex.IsMatch(prompt))
        {
            return true;
        }

        if (!ReturnKeywordRegex.IsMatch(prompt) || ReturnNonRefundRegex.IsMatch(prompt))
        {
            return false;
        }

        bool hasCommerceContext = !string.IsNullOrWhiteSpace(orderId) || ReturnCommerceContextRegex.IsMatch(prompt);
        return hasCommerceContext && ReturnIntentRegex.IsMatch(prompt);
    }

    private static string? ExtractRefundReason(string prompt)
    {
        Match directReason = RefundReasonRegex.Match(prompt);
        if (directReason.Success)
        {
            return CleanReasonCandidate(directReason.Groups["reason"].Value);
        }

        Match forReason = RefundReasonForRegex.Match(prompt);
        if (!forReason.Success)
        {
            return null;
        }

        string candidate = LeadingOrderContextRegex.Replace(forReason.Groups["reason"].Value, string.Empty);
        return CleanReasonCandidate(candidate);
    }

    private static bool ContainsAnyPhrase(string input, IEnumerable<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            if (ContainsPhrase(input, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPhrase(string input, string candidate)
    {
        string pattern = $@"(?<![a-z0-9]){Regex.Escape(candidate.ToLowerInvariant())}(?![a-z0-9])";
        return Regex.IsMatch(
            input,
            pattern,
            RegexOptions.CultureInvariant);
    }

    private static string? CleanReasonCandidate(string? reason)
    {
        string? cleaned = CleanReason(reason);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (StandaloneOrderIdRegex.IsMatch(cleaned) ||
            StandaloneOrderReferenceRegex.IsMatch(cleaned) ||
            NonRefundReasonContextRegex.IsMatch(cleaned))
        {
            return null;
        }

        return cleaned;
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
