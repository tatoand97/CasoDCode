namespace CasoDCode.Models;

internal sealed record ResolvedAgentIdentity(
    string AgentId,
    string AgentName,
    string AgentVersion);
