using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using CasoE.Models;
using System.ClientModel;
using System.Text.Json;

namespace CasoE.Services;

internal sealed class AgentReconciler
{
    private readonly AIProjectClient _projectClient;
    private readonly ConsoleTrace _trace;

    public AgentReconciler(AIProjectClient projectClient, ConsoleTrace trace)
    {
        _projectClient = projectClient;
        _trace = trace;
    }

    public async Task<ReconciliationResult> ReconcileAsync(
        string agentName,
        PromptAgentDefinition desiredDefinition,
        CancellationToken cancellationToken)
    {
        string desiredSignature = BuildDefinitionSignature(desiredDefinition);
        AgentVersion? latest = await TryGetLatestVersionAsync(agentName, cancellationToken);

        if (latest is null)
        {
            ClientResult<AgentVersion> created = await _projectClient.Agents.CreateAgentVersionAsync(
                agentName,
                new AgentVersionCreationOptions(desiredDefinition),
                cancellationToken);

            _trace.Write("RECONCILE", $"{agentName} => {ReconciliationStatus.Created}");
            return new ReconciliationResult(
                AgentName: created.Value.Name,
                AgentId: created.Value.Id,
                AgentVersion: created.Value.Version,
                ResponseClientName: created.Value.Name,
                Status: ReconciliationStatus.Created,
                Signature: desiredSignature);
        }

        string currentSignature = BuildDefinitionSignature(latest.Definition);
        if (string.Equals(currentSignature, desiredSignature, StringComparison.Ordinal))
        {
            _trace.Write("RECONCILE", $"{agentName} => {ReconciliationStatus.Unchanged}");
            return new ReconciliationResult(
                AgentName: latest.Name,
                AgentId: latest.Id,
                AgentVersion: latest.Version,
                ResponseClientName: latest.Name,
                Status: ReconciliationStatus.Unchanged,
                Signature: currentSignature);
        }

        ClientResult<AgentVersion> updated = await _projectClient.Agents.CreateAgentVersionAsync(
            agentName,
            new AgentVersionCreationOptions(desiredDefinition),
            cancellationToken);

        _trace.Write("RECONCILE", $"{agentName} => {ReconciliationStatus.Updated}");
        return new ReconciliationResult(
            AgentName: updated.Value.Name,
            AgentId: updated.Value.Id,
            AgentVersion: updated.Value.Version,
            ResponseClientName: updated.Value.Name,
            Status: ReconciliationStatus.Updated,
            Signature: desiredSignature);
    }

    private async Task<AgentVersion?> TryGetLatestVersionAsync(string agentName, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _projectClient.Agents.GetAgentAsync(agentName, cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            return null;
        }

        await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           limit: 1,
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            return version;
        }

        return null;
    }

    private static string BuildDefinitionSignature(AgentDefinition definition)
    {
        using JsonDocument document = JsonDocument.Parse(BinaryData.FromObjectAsJson(definition).ToString());

        string model = ReadString(document.RootElement, "model", "Model");
        string instructions = ReadString(document.RootElement, "instructions", "Instructions");
        string tools = ReadToolsSignature(document.RootElement);

        return JsonSerializer.Serialize(new
        {
            model,
            instructions,
            tools,
        });
    }

    private static string ReadToolsSignature(JsonElement root)
    {
        JsonElement toolsElement = root.TryGetProperty("tools", out JsonElement lowerCase)
            ? lowerCase
            : root.TryGetProperty("Tools", out JsonElement pascalCase)
                ? pascalCase
                : default;

        return toolsElement.ValueKind == JsonValueKind.Array
            ? toolsElement.GetRawText()
            : "[]";
    }

    private static string ReadString(JsonElement root, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (root.TryGetProperty(candidate, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
