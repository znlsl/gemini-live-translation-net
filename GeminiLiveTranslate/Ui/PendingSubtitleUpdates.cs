namespace GeminiLiveTranslate.Ui;

public sealed record PendingSubtitleBatch(
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs)
{
    public bool IsEmpty => Inputs.Count == 0 && Outputs.Count == 0;
}

public sealed class PendingSubtitleUpdates
{
    private readonly object _gate = new();
    private readonly List<string> _inputs = [];
    private readonly List<string> _outputs = [];

    public void Enqueue(string? input, string? output)
    {
        lock (_gate)
        {
            if (input is not null)
            {
                _inputs.Add(input);
            }

            if (output is not null)
            {
                _outputs.Add(output);
            }
        }
    }

    public PendingSubtitleBatch Drain()
    {
        lock (_gate)
        {
            var batch = new PendingSubtitleBatch(
                _inputs.ToArray(),
                _outputs.ToArray());
            ClearCore();
            return batch;
        }
    }

    public void Clear()
    {
        lock (_gate) ClearCore();
    }

    private void ClearCore()
    {
        _inputs.Clear();
        _outputs.Clear();
    }
}
