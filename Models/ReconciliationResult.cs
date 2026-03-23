namespace CasoE.Models;

internal enum ReconciliationStatus
{
    Created,
    Updated,
    Unchanged,
    ExternalValidated,
}

internal sealed record ReconciliationResult(
    string AgentName,
    string AgentId,
    string AgentVersion,
    string ResponseClientName,
    ReconciliationStatus Status,
    string Signature);
