namespace DualView.Shared.Models;

public class OperationStatusUpdate(long operationId, string mainStatusText)
{
    public long OperationId { get; set; } = operationId;

    public string MainStatusText { get; set; } = mainStatusText;

    public string? Message { get; set; }

    public float CompletionFraction { get; set; }

    public bool Completed { get; set; }

    public bool Error { get; set; }

    public bool Paused { get; set; }
}
