namespace CasoE.Services;

internal sealed class ConsoleTrace
{
    private readonly object _lock = new();

    public void Write(string stage, string message) => WriteInternal(Console.Out, stage, message);

    public void WriteError(string stage, string message) => WriteInternal(Console.Error, stage, message);

    private void WriteInternal(TextWriter writer, string stage, string message)
    {
        lock (_lock)
        {
            writer.WriteLine($"[{stage}] {message}");
        }
    }
}
