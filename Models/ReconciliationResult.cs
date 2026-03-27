namespace CasoE.Models;

internal enum ReconciliationStatus
{
    Created,
    Updated,
    Unchanged,
}

internal sealed record ReconciliationResult(
    string AgentName,
    string AgentId,
    string AgentVersion,
    ReconciliationStatus Status,
    string Signature);
