using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using CasoE.Models;
using System.ClientModel;

namespace CasoE.Services;

internal sealed class AgentValidationService
{
    private readonly AIProjectClient _projectClient;
    private readonly ConsoleTrace _trace;

    public AgentValidationService(AIProjectClient projectClient, ConsoleTrace trace)
    {
        _projectClient = projectClient;
        _trace = trace;
    }

    public async Task ValidateProjectAccessAsync(CancellationToken cancellationToken)
    {
        await foreach (AgentRecord _ in _projectClient.Agents.GetAgentsAsync(limit: 1, cancellationToken: cancellationToken))
        {
            break;
        }

        _trace.Write("CONFIG", "Endpoint validated");
    }

    public async Task<ResolvedAgentIdentity> ValidateOrderAgentAsync(string orderAgentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderAgentId))
        {
            throw new InvalidOperationException("CasoE:OrderAgentId cannot be empty.");
        }

        string trimmedAgentId = orderAgentId.Trim();
        ResolvedAgentIdentity? resolved = await TryResolveAgentAsync(trimmedAgentId, cancellationToken);

        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"CasoE:OrderAgentId '{trimmedAgentId}' was not found in the project or is not accessible.");
        }

        _trace.Write(
            "VALIDATION",
            $"OrderAgent validated: Name={resolved.AgentName}, Version={resolved.AgentVersion}, Id={resolved.AgentId}");

        return resolved;
    }

    private async Task<ResolvedAgentIdentity?> TryResolveAgentAsync(string agentIdentifier, CancellationToken cancellationToken)
    {
        foreach ((string AgentName, string RequestedVersion) candidate in GetNameVersionCandidates(agentIdentifier))
        {
            ResolvedAgentIdentity? byVersion = await TryResolveByAgentNameAsync(
                candidate.AgentName,
                version => string.Equals(version.Id, agentIdentifier, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(version.Version, candidate.RequestedVersion, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(version.Name, candidate.RequestedVersion, StringComparison.OrdinalIgnoreCase),
                cancellationToken);

            if (byVersion is not null)
            {
                return byVersion;
            }
        }

        try
        {
            _ = await _projectClient.Agents.GetAgentAsync(agentIdentifier, cancellationToken);

            ResolvedAgentIdentity? latest = await TryResolveByAgentNameAsync(
                agentIdentifier,
                _ => true,
                cancellationToken);

            if (latest is not null)
            {
                return latest;
            }
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
        }

        await foreach (AgentRecord agent in _projectClient.Agents.GetAgentsAsync(
                           limit: 100,
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                               agentName: agent.Name,
                               limit: 100,
                               order: AgentListOrder.Descending,
                               cancellationToken: cancellationToken))
            {
                if (string.Equals(version.Id, agentIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    return new ResolvedAgentIdentity(
                        AgentId: version.Id,
                        AgentName: agent.Name,
                        AgentVersion: version.Version,
                        ResponseClientName: version.Name);
                }
            }
        }

        return null;
    }

    private async Task<ResolvedAgentIdentity?> TryResolveByAgentNameAsync(
        string agentName,
        Func<AgentVersion, bool> match,
        CancellationToken cancellationToken)
    {
        await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           limit: 100,
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            if (match(version))
            {
                return new ResolvedAgentIdentity(
                    AgentId: version.Id,
                    AgentName: agentName,
                    AgentVersion: version.Version,
                    ResponseClientName: version.Name);
            }
        }

        return null;
    }

    private static IEnumerable<(string AgentName, string RequestedVersion)> GetNameVersionCandidates(string agentIdentifier)
    {
        int colonIndex = agentIdentifier.IndexOf(':');
        if (colonIndex > 0 && colonIndex < agentIdentifier.Length - 1)
        {
            yield return (agentIdentifier[..colonIndex], agentIdentifier[(colonIndex + 1)..]);
        }

        int atIndex = agentIdentifier.IndexOf('@');
        if (atIndex > 0 && atIndex < agentIdentifier.Length - 1)
        {
            yield return (agentIdentifier[..atIndex], agentIdentifier[(atIndex + 1)..]);
        }
    }
}
