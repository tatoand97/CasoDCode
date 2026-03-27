namespace CasoDCode.Agents;

internal static class AgentInstructionLoader
{
    private static readonly string DefinitionsDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Agents",
        "Definitions");

    public static string Load(string fileName)
    {
        string path = Path.Combine(DefinitionsDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Missing agent instructions asset '{fileName}' at '{path}'.");
        }

        string instructions = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new InvalidOperationException(
                $"Agent instructions asset '{fileName}' is empty.");
        }

        return instructions;
    }
}
