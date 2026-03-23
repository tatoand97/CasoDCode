using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using CasoE.Agents;
using CasoE.Models;
using CasoE.Services;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Globalization;

namespace CasoE;

internal static class Program
{
    private const string BuiltInDefaultPrompt = "Where is order ORD-000123?";

    private const string OrderAgentPromptTemplate =
        """
        Retrieve only structured order data for the requested order.
        Use your configured MCP tool if applicable.
        Return exactly one JSON object and nothing else.
        No markdown.
        No prose outside JSON.

        Required fields:
        - id
        - status
        - requiresAction

        Optional field:
        - reason

        Allowed status values:
        - Created
        - Confirmed
        - Packed
        - Shipped
        - Delivered
        - Cancelled
        - Unknown
        - NotFound

        If the order is not found, return:
        {{"id":"<requested-id>","status":"NotFound","requiresAction":false,"reason":"Order not found"}}

        User request:
        {0}
        """;

    public static async Task Main(string[] args)
    {
        ConsoleTrace trace = new();
        using CancellationTokenSource cancellationTokenSource = new();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            CasoESettings settings = LoadSettings();
            string endpoint = GetRequiredSetting(settings.ProjectEndpoint, "CasoE:ProjectEndpoint");
            string deployment = GetRequiredSetting(settings.ModelDeploymentName, "CasoE:ModelDeploymentName");
            string orderAgentId = GetRequiredSetting(settings.OrderAgentId, "CasoE:OrderAgentId");
            int timeoutSeconds = GetPositiveSetting(settings.ResponsesTimeoutSeconds, "CasoE:ResponsesTimeoutSeconds");
            int maxBackoffSeconds = GetPositiveSetting(settings.ResponsesMaxBackoffSeconds, "CasoE:ResponsesMaxBackoffSeconds");
            string prompt = ResolvePrompt(args, settings.DefaultPrompt);

            ValidateProjectEndpoint(endpoint);
            trace.Write("CONFIG", "Configuration loaded");

            DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true,
            });

            AIProjectClient projectClient = new(new Uri(endpoint), credential);
            ProjectOpenAIClient openAiClient = projectClient.OpenAI;
            AgentValidationService validationService = new(projectClient, trace);
            AgentReconciler reconciler = new(projectClient, trace);
            AgentRunner runner = new(TimeSpan.FromSeconds(maxBackoffSeconds));
            IntentRouter router = new(new OrderIdExtractor());
            TimeSpan responseTimeout = TimeSpan.FromSeconds(timeoutSeconds);

            await validationService.ValidateProjectAccessAsync(cancellationTokenSource.Token);
            ResolvedAgentIdentity orderAgent = await validationService.ValidateOrderAgentAsync(
                orderAgentId,
                cancellationTokenSource.Token);

            ReconciliationResult refundAgent = await reconciler.ReconcileAsync(
                AgentNames.Refund,
                RefundAgentFactory.Build(deployment),
                cancellationTokenSource.Token);

            ReconciliationResult clarifierAgent = await reconciler.ReconcileAsync(
                AgentNames.Clarifier,
                ClarifierAgentFactory.Build(deployment),
                cancellationTokenSource.Token);

            trace.Write("INPUT", $"Prompt selected: {prompt}");
            RouteDecision decision = RoutePrompt(prompt, router, trace);

            OrderResult? orderResult = null;
            RefundResult? refundResult = null;
            ClarifierResult? clarifierResult = null;
            string? rejectResponse = null;

            switch (decision.Route)
            {
                case RouteKind.Order:
                    orderResult = await RunOrderBranchAsync(
                        runner,
                        openAiClient,
                        orderAgent,
                        prompt,
                        responseTimeout,
                        trace,
                        cancellationTokenSource.Token);
                    break;

                case RouteKind.Refund:
                    refundResult = await RunRefundBranchAsync(
                        runner,
                        openAiClient,
                        refundAgent,
                        decision,
                        responseTimeout,
                        trace,
                        cancellationTokenSource.Token);
                    break;

                case RouteKind.Clarify:
                    clarifierResult = await RunClarifyBranchAsync(
                        runner,
                        openAiClient,
                        clarifierAgent,
                        decision,
                        responseTimeout,
                        trace,
                        cancellationTokenSource.Token);
                    break;

                case RouteKind.Reject:
                    rejectResponse = BuildRejectResponse(decision);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported route '{decision.Route}'.");
            }

            string finalResponse = BuildFinalResponse(
                decision,
                orderResult,
                refundResult,
                clarifierResult,
                rejectResponse);

            trace.Write("FINAL", "Response built successfully");
            Console.WriteLine();
            Console.WriteLine(finalResponse);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            trace.WriteError("RUNTIME", "Operation cancelled by user.");
            Environment.ExitCode = 1;
        }
        catch (ClientResultException ex)
        {
            WriteClientError(ex, trace);
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            trace.WriteError("RUNTIME", ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static RouteDecision RoutePrompt(string prompt, IntentRouter router, ConsoleTrace trace)
    {
        RouteDecision decision = router.Route(prompt);
        trace.Write("ROUTER", $"Route selected: {decision.RouteLabel}");

        if (!string.IsNullOrWhiteSpace(decision.OrderId))
        {
            trace.Write("ROUTER", $"Extracted OrderId: {decision.OrderId}");
        }

        if (!string.IsNullOrWhiteSpace(decision.RefundReason))
        {
            trace.Write("ROUTER", $"Extracted RefundReason: {decision.RefundReason}");
        }

        return decision;
    }

    private static async Task<OrderResult> RunOrderBranchAsync(
        AgentRunner runner,
        ProjectOpenAIClient openAiClient,
        ResolvedAgentIdentity orderAgent,
        string prompt,
        TimeSpan timeout,
        ConsoleTrace trace,
        CancellationToken cancellationToken)
    {
        trace.Write("AGENT", "Invoking OrderAgent");

        string response = await runner.RunPromptAsync(
            openAiClient,
            orderAgent.ResponseClientName,
            string.Format(CultureInfo.InvariantCulture, OrderAgentPromptTemplate, prompt),
            timeout,
            cancellationToken);

        OrderResult result = OutputValidators.ValidateOrderResult(response);
        trace.Write("VALIDATION", "OrderAgent JSON valid");
        return result;
    }

    private static async Task<RefundResult> RunRefundBranchAsync(
        AgentRunner runner,
        ProjectOpenAIClient openAiClient,
        ReconciliationResult refundAgent,
        RouteDecision decision,
        TimeSpan timeout,
        ConsoleTrace trace,
        CancellationToken cancellationToken)
    {
        trace.Write("AGENT", "Invoking RefundAgent");

        string response = await runner.RunPromptAsync(
            openAiClient,
            refundAgent.ResponseClientName,
            BuildRefundPrompt(decision),
            timeout,
            cancellationToken);

        RefundResult result = OutputValidators.ValidateRefundResult(response);
        trace.Write("VALIDATION", "RefundAgent JSON valid");
        return result;
    }

    private static async Task<ClarifierResult> RunClarifyBranchAsync(
        AgentRunner runner,
        ProjectOpenAIClient openAiClient,
        ReconciliationResult clarifierAgent,
        RouteDecision decision,
        TimeSpan timeout,
        ConsoleTrace trace,
        CancellationToken cancellationToken)
    {
        trace.Write("AGENT", "Invoking ClarifierAgent");

        string response = await runner.RunPromptAsync(
            openAiClient,
            clarifierAgent.ResponseClientName,
            BuildClarifierPrompt(decision),
            timeout,
            cancellationToken);

        ClarifierResult result = OutputValidators.ValidateClarifierResult(response);
        trace.Write("VALIDATION", "ClarifierAgent JSON valid");
        return result;
    }

    private static string BuildRejectResponse(RouteDecision decision)
    {
        return decision.Reason.Contains("destructive", StringComparison.OrdinalIgnoreCase)
            ? "I can help with order status and refund questions, but I can't delete orders or perform destructive account actions."
            : "I can help with order status and refund requests only. That request is outside the supported scope for this case.";
    }

    private static string BuildFinalResponse(
        RouteDecision decision,
        OrderResult? orderResult = null,
        RefundResult? refundResult = null,
        ClarifierResult? clarifierResult = null,
        string? rejectResponse = null)
    {
        return decision.Route switch
        {
            RouteKind.Order => BuildOrderResponse(orderResult),
            RouteKind.Refund => BuildRefundResponse(refundResult),
            RouteKind.Clarify => BuildClarifyResponse(clarifierResult),
            RouteKind.Reject => rejectResponse ?? BuildRejectResponse(decision),
            _ => throw new InvalidOperationException($"Unsupported route '{decision.Route}'."),
        };
    }

    private static string BuildOrderResponse(OrderResult? orderResult)
    {
        if (orderResult is null)
        {
            throw new InvalidOperationException("Order route requires a validated order result.");
        }

        if (string.Equals(orderResult.Status, "NotFound", StringComparison.Ordinal))
        {
            return AppendMessage($"I couldn't find order {orderResult.Id}", orderResult.Reason);
        }

        string response = $"Order {orderResult.Id} is currently {ToDisplayStatus(orderResult.Status)}.";
        response = orderResult.RequiresAction
            ? $"{response} Action is required."
            : $"{response} No action is required right now.";

        if (!string.IsNullOrWhiteSpace(orderResult.Reason))
        {
            response = $"{response} {EnsureTerminalPunctuation(orderResult.Reason)}";
        }

        return response.Trim();
    }

    private static string BuildRefundResponse(RefundResult? refundResult)
    {
        if (refundResult is null)
        {
            throw new InvalidOperationException("Refund route requires a validated refund result.");
        }

        string orderFragment = string.IsNullOrWhiteSpace(refundResult.OrderId)
            ? "the refund request"
            : $"the refund request for order {refundResult.OrderId}";

        return refundResult.Status switch
        {
            "accepted" => AppendMessage(
                $"I recorded {orderFragment} as accepted",
                refundResult.Message),
            "needsMoreInfo" => AppendMessage(
                $"I need more information to continue with {orderFragment}",
                refundResult.Message),
            "notAllowed" => AppendMessage(
                $"I can't process {orderFragment}",
                refundResult.Message),
            "pending" => AppendMessage(
                $"{UppercaseFirst(orderFragment)} is pending review",
                refundResult.Message),
            _ => throw new InvalidOperationException($"Unsupported refund status '{refundResult.Status}'."),
        };
    }

    private static string BuildClarifyResponse(ClarifierResult? clarifierResult)
    {
        if (clarifierResult is null)
        {
            throw new InvalidOperationException("Clarify route requires a validated clarification result.");
        }

        return EnsureTerminalPunctuation(clarifierResult.Question);
    }

    private static string BuildRefundPrompt(RouteDecision decision)
    {
        return
            $"""
            User request:
            {decision.Prompt}

            Known context:
            orderId: {decision.OrderId ?? "(missing)"}
            refundReason: {decision.RefundReason ?? "(missing)"}
            routingReason: {decision.Reason}
            """;
    }

    private static string BuildClarifierPrompt(RouteDecision decision)
    {
        return $"Missing information summary: {decision.Reason}";
    }

    private static string AppendMessage(string prefix, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return EnsureTerminalPunctuation(prefix);
        }

        return $"{EnsureTerminalPunctuation(prefix)} {EnsureTerminalPunctuation(message)}";
    }

    private static string EnsureTerminalPunctuation(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed[^1] is '.' or '!' or '?'
            ? trimmed
            : $"{trimmed}.";
    }

    private static string ToDisplayStatus(string status)
    {
        return status switch
        {
            "Created" => "created",
            "Confirmed" => "confirmed",
            "Packed" => "packed",
            "Shipped" => "shipped",
            "Delivered" => "delivered",
            "Cancelled" => "cancelled",
            "Unknown" => "unknown",
            "NotFound" => "not found",
            _ => status.ToLowerInvariant(),
        };
    }

    private static string UppercaseFirst(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length == 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string ResolvePrompt(string[] args, string? defaultPrompt)
    {
        if (args.Length > 0)
        {
            return string.Join(' ', args).Trim();
        }

        if (!string.IsNullOrWhiteSpace(defaultPrompt))
        {
            return defaultPrompt.Trim();
        }

        return BuiltInDefaultPrompt;
    }

    private static CasoESettings LoadSettings()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        CasoESettings? settings = configuration
            .GetSection(CasoESettings.SectionName)
            .Get<CasoESettings>();

        return settings ?? throw new InvalidOperationException(
            $"Missing configuration section '{CasoESettings.SectionName}' in appsettings.json.");
    }

    private static string GetRequiredSetting(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required configuration value '{key}' in appsettings.json.");
        }

        return value.Trim();
    }

    private static int GetPositiveSetting(int value, string key)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be greater than zero.");
        }

        return value;
    }

    private static void ValidateProjectEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CasoE:ProjectEndpoint must be an absolute HTTPS URL.");
        }

        if (!endpoint.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CasoE:ProjectEndpoint must be a Foundry project endpoint containing '/api/projects/'.");
        }

        if (endpoint.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CasoE:ProjectEndpoint must target an Azure AI Foundry project, not an Azure OpenAI resource endpoint.");
        }
    }

    private static void WriteClientError(ClientResultException ex, ConsoleTrace trace)
    {
        trace.WriteError("AZURE", $"Status: {ex.Status}");
        trace.WriteError("AZURE", ex.Message);

        if (ex.Status is 401 or 403)
        {
            trace.WriteError("AZURE", "Authorization failed. Ensure the principal can list and invoke agents in the project.");
        }
        else if (ex.Status == 404)
        {
            trace.WriteError("AZURE", "Resource not found. Validate the project endpoint, model deployment, and agent ids.");
        }

        if (ex.GetRawResponse() is { } rawResponse)
        {
            if (rawResponse.Headers.TryGetValue("x-request-id", out string? requestId))
            {
                trace.WriteError("AZURE", $"RequestId: {requestId ?? "(unavailable)"}");
            }

            if (rawResponse.Headers.TryGetValue("x-ms-client-request-id", out string? clientRequestId))
            {
                trace.WriteError("AZURE", $"ClientRequestId: {clientRequestId ?? "(unavailable)"}");
            }
        }
    }
}
