using CasoE.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CasoE.Services;

internal static class OutputValidators
{
    private const string SupportedOrderStatuses = "Created, Confirmed, Packed, Shipped, Delivered, Cancelled, Unknown, NotFound";
    private const string SupportedRefundStatuses = "accepted, needsMoreInfo, notAllowed, pending";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<string, string> OrderStatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Created"] = "Created",
        ["Confirmed"] = "Confirmed",
        ["Packed"] = "Packed",
        ["Shipped"] = "Shipped",
        ["Delivered"] = "Delivered",
        ["Cancelled"] = "Cancelled",
        ["Unknown"] = "Unknown",
        ["NotFound"] = "NotFound",
    };

    private static readonly Dictionary<string, string> RefundStatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["accepted"] = "accepted",
        ["needsMoreInfo"] = "needsMoreInfo",
        ["notAllowed"] = "notAllowed",
        ["pending"] = "pending",
    };

    public static OrderResult ValidateOrderResult(string responseText)
    {
        OrderResultDto payload = DeserializeJsonObject<OrderResultDto>(responseText, "OrderAgent");

        if (string.IsNullOrWhiteSpace(payload.Id))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'id' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (string.IsNullOrWhiteSpace(payload.Status))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'status' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (payload.RequiresAction is null)
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'requiresAction'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        string normalizedStatus = NormalizeOrderStatus(payload.Status);

        return new OrderResult
        {
            Id = payload.Id.Trim(),
            Status = normalizedStatus,
            RequiresAction = payload.RequiresAction.Value,
            Reason = CleanOptional(payload.Reason),
        };
    }

    public static RefundResult ValidateRefundResult(string responseText)
    {
        RefundResultDto payload = DeserializeJsonObject<RefundResultDto>(responseText, "RefundAgent");

        if (string.IsNullOrWhiteSpace(payload.Status))
        {
            throw new InvalidOperationException(
                $"RefundAgent JSON is missing required field 'status'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (string.IsNullOrWhiteSpace(payload.Message))
        {
            throw new InvalidOperationException(
                $"RefundAgent JSON is missing required field 'message'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        return new RefundResult
        {
            Status = NormalizeRefundStatus(payload.Status),
            Message = payload.Message.Trim(),
            OrderId = CleanOptional(payload.OrderId),
            RefundReason = CleanOptional(payload.RefundReason),
        };
    }

    public static ClarifierResult ValidateClarifierResult(string responseText)
    {
        ClarifierResultDto payload = DeserializeJsonObject<ClarifierResultDto>(responseText, "ClarifierAgent");

        if (string.IsNullOrWhiteSpace(payload.Question))
        {
            throw new InvalidOperationException(
                $"ClarifierAgent JSON is missing required field 'question'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        return new ClarifierResult
        {
            Question = payload.Question.Trim(),
        };
    }

    private static T DeserializeJsonObject<T>(string responseText, string agentLabel)
        where T : class
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"{agentLabel} returned invalid JSON. Expected a single JSON object. Raw response: {BuildResponseSnippet(responseText)}");
            }

            T? payload = JsonSerializer.Deserialize<T>(document.RootElement.GetRawText(), JsonOptions);
            return payload ?? throw new InvalidOperationException(
                $"{agentLabel} returned an empty JSON payload. Raw response: {BuildResponseSnippet(responseText)}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{agentLabel} returned invalid JSON. Expected a single JSON object. Raw response: {BuildResponseSnippet(responseText)}",
                ex);
        }
    }

    private static string NormalizeOrderStatus(string status)
    {
        string candidate = status.Trim();
        if (!OrderStatusMap.TryGetValue(candidate, out string? normalized))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON field 'status' has unsupported value '{candidate}'. Supported values: {SupportedOrderStatuses}.");
        }

        return normalized;
    }

    private static string NormalizeRefundStatus(string status)
    {
        string candidate = status.Trim();
        if (!RefundStatusMap.TryGetValue(candidate, out string? normalized))
        {
            throw new InvalidOperationException(
                $"RefundAgent JSON field 'status' has unsupported value '{candidate}'. Supported values: {SupportedRefundStatuses}.");
        }

        return normalized;
    }

    private static string? CleanOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string BuildResponseSnippet(string responseText)
    {
        const int MaxLength = 240;
        string condensed = responseText
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (condensed.Length <= MaxLength)
        {
            return condensed;
        }

        return $"{condensed[..MaxLength]}...";
    }

    private sealed class OrderResultDto
    {
        public string? Id { get; init; }

        public string? Status { get; init; }

        public bool? RequiresAction { get; init; }

        public string? Reason { get; init; }
    }

    private sealed class RefundResultDto
    {
        public string? Status { get; init; }

        public string? Message { get; init; }

        public string? OrderId { get; init; }

        public string? RefundReason { get; init; }
    }

    private sealed class ClarifierResultDto
    {
        public string? Question { get; init; }
    }
}
