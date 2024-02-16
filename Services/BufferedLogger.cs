namespace KCert.Services;

public class BufferedLogger<TT>(ILogger<TT> inner) : ILogger<TT>
{
    private readonly Queue<string> _buff = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = $"[{logLevel}]: {state}";
        _buff.Enqueue(message);
        inner.Log(logLevel, eventId, state, exception, formatter);
    }

    public void Clear() => _buff.Clear();

    public List<string> Dump() => _buff.ToList();
}
