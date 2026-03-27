namespace CasoDCode;

internal sealed class CasoDCodeSettings
{
    public const string SectionName = "CasoDCode";

    public string? ProjectEndpoint { get; init; }

    public string? ModelDeploymentName { get; init; }

    public string? OrderAgentId { get; init; }
}
