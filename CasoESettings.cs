namespace CasoE;

internal sealed class CasoESettings
{
    public const string SectionName = "CasoE";

    public string? ProjectEndpoint { get; init; }

    public string? ModelDeploymentName { get; init; }

    public string? OrderAgentId { get; init; }

    public int ResponsesTimeoutSeconds { get; init; } = 60;

    public int ResponsesMaxBackoffSeconds { get; init; } = 8;

    public string? DefaultPrompt { get; init; }
}
