using Azure.AI.Projects.OpenAI;

namespace CasoE.Agents;

internal static class RefundAgentFactory
{
    private const string InstructionsFileName = "refund-agent-casee.instructions.txt";

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
