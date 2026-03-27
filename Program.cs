using Azure.AI.Projects;
using Azure.Identity;
using CasoE.Agents;
using CasoE.Models;
using CasoE.Services;
using Microsoft.Extensions.Configuration;
using System.ClientModel;

namespace CasoE;

internal static class Program
{
    public static async Task Main()
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
            await RunBootstrapAsync(trace, cancellationTokenSource.Token);
            Environment.ExitCode = 0;
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            trace.WriteError("BOOTSTRAP", "Operation cancelled by user.");
            Environment.ExitCode = 1;
        }
        catch (ClientResultException ex)
        {
            WriteClientError(ex, trace);
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            trace.WriteError("BOOTSTRAP", ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static async Task RunBootstrapAsync(ConsoleTrace trace, CancellationToken cancellationToken)
    {
        BootstrapConfiguration configuration = LoadBootstrapConfiguration();
        trace.Write("CONFIG", "Endpoint validated");

        AIProjectClient projectClient = CreateProjectClient(configuration.Endpoint);
        _ = projectClient.OpenAI;

        AgentValidationService validationService = new(projectClient, trace);
        AgentReconciler reconciler = new(projectClient, trace);

        await validationService.ValidateProjectAccessAsync(cancellationToken);
        await validationService.ValidateModelDeploymentAsync(configuration.Deployment, cancellationToken);

        ResolvedAgentIdentity orderAgent = await validationService.ValidateOrderAgentAsync(
            configuration.OrderAgentId,
            cancellationToken);

        ReconciliationResult refundAgent = await reconciler.ReconcileAsync(
            AgentNames.Refund,
            RefundAgentFactory.Build(configuration.Deployment),
            cancellationToken);

        ReconciliationResult clarifierAgent = await reconciler.ReconcileAsync(
            AgentNames.Clarifier,
            ClarifierAgentFactory.Build(configuration.Deployment),
            cancellationToken);

        WriteBootstrapSummary(
            trace,
            configuration.Endpoint,
            configuration.Deployment,
            orderAgent,
            refundAgent,
            clarifierAgent);
    }

    private static void WriteBootstrapSummary(
        ConsoleTrace trace,
        string endpoint,
        string deployment,
        ResolvedAgentIdentity orderAgent,
        ReconciliationResult refundAgent,
        ReconciliationResult clarifierAgent)
    {
        trace.Write("SUMMARY", $"Endpoint: {endpoint}");
        trace.Write("SUMMARY", $"Deployment: {deployment}");
        trace.Write(
            "SUMMARY",
            $"OrderAgent => validated | Name={orderAgent.AgentName} | Version={orderAgent.AgentVersion} | Id={orderAgent.AgentId}");
        trace.Write(
            "SUMMARY",
            $"RefundAgent => {ToStatusLabel(refundAgent.Status)} | Name={refundAgent.AgentName} | Version={refundAgent.AgentVersion} | Id={refundAgent.AgentId}");
        trace.Write(
            "SUMMARY",
            $"ClarifierAgent => {ToStatusLabel(clarifierAgent.Status)} | Name={clarifierAgent.AgentName} | Version={clarifierAgent.AgentVersion} | Id={clarifierAgent.AgentId}");
        trace.Write(
            "SUMMARY",
            $"Bindings => OrderAgent={orderAgent.AgentId}; RefundAgent={refundAgent.AgentId}; ClarifierAgent={clarifierAgent.AgentId}");
        trace.Write("SUMMARY", "Foundry bootstrap completed");
    }

    private static BootstrapConfiguration LoadBootstrapConfiguration()
    {
        CasoESettings settings = LoadSettings();
        string endpoint = GetRequiredSetting(settings.ProjectEndpoint, "CasoE:ProjectEndpoint");
        string deployment = GetRequiredSetting(settings.ModelDeploymentName, "CasoE:ModelDeploymentName");
        string orderAgentId = GetRequiredSetting(settings.OrderAgentId, "CasoE:OrderAgentId");

        ValidateProjectEndpoint(endpoint);

        return new BootstrapConfiguration(
            Endpoint: endpoint,
            Deployment: deployment,
            OrderAgentId: orderAgentId);
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

    private static AIProjectClient CreateProjectClient(string endpoint)
    {
        DefaultAzureCredential credential = new(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true,
        });

        AIProjectClient projectClient = new(new Uri(endpoint), credential);
        return projectClient;
    }

    private static string GetRequiredSetting(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required configuration value '{key}' in appsettings.json.");
        }

        return value.Trim();
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
            trace.WriteError("AZURE", "Authorization failed. Ensure the principal can read deployments and agents in the project.");
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

    private static string ToStatusLabel(ReconciliationStatus status) => status.ToString().ToLowerInvariant();

    private sealed record BootstrapConfiguration(
        string Endpoint,
        string Deployment,
        string OrderAgentId);
}
