namespace CasoE;

internal sealed class CasoESettings
{
    public const string SectionName = "CasoE";

    public string? ProjectEndpoint { get; init; }

    public string? ModelDeploymentName { get; init; }

    public string? OrderAgentId { get; init; }
}
