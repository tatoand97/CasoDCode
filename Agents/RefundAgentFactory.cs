using Azure.AI.Projects.OpenAI;

namespace CasoE.Agents;

internal static class RefundAgentFactory
{
    internal const string Instructions =
        """
        You are RefundAgent.
        You handle refund requests safely.
        Return exactly one JSON object and nothing else.
        No markdown.
        No prose outside JSON.
        Output:
        {"status":"accepted|needsMoreInfo|notAllowed|pending","message":"short explanation","orderId":"optional string","refundReason":"optional string"}

        Rules:
        - Do not invent approvals.
        - If critical information is missing, use status="needsMoreInfo".
        - Use status="notAllowed" for disallowed or unsupported refund requests.
        - Use status="pending" when the request is valid but requires manual review or follow-up.
        - Echo orderId and refundReason when known.
        - Keep message short and user-safe.
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
