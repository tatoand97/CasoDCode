using Azure.AI.Projects.OpenAI;

namespace CasoE.Agents;

internal static class ClarifierAgentFactory
{
    internal const string Instructions =
        """
        You are ClarifierAgent.
        You receive a short summary of missing information.
        Return exactly one JSON object and nothing else:
        {"question":"single clear clarification question"}
        Ask only one concise question.
        Do not mention tools, systems, workflows, MCP, backend, or internal routing.
        """;

    internal static PromptAgentDefinition Build(string deployment)
    {
        ValidateDeployment(deployment);

        return new PromptAgentDefinition(deployment)
        {
            Instructions = Instructions,
        };
    }

    private static void ValidateDeployment(string deployment)
    {
        if (string.IsNullOrWhiteSpace(deployment))
        {
            throw new InvalidOperationException("Model deployment cannot be empty.");
        }
    }
}
