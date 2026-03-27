using Azure.AI.Projects.OpenAI;

namespace CasoDCode.Agents;

internal static class ClarifierAgentFactory
{
    private const string InstructionsFileName = "clarifier-agent-casee.instructions.txt";

    internal static PromptAgentDefinition Build(string deployment)
    {
        ValidateDeployment(deployment);

        return new PromptAgentDefinition(deployment)
        {
            Instructions = AgentInstructionLoader.Load(InstructionsFileName),
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
