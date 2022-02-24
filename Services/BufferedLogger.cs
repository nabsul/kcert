using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KCert.Services;

[Service]
public class BufferedLogger<TT> : ILogger<TT>
{
    private readonly ILogger<TT> _inner;
    private readonly Queue<string> _buff = new();

    public BufferedLogger(ILogger<TT> inner)
    {
        _inner = inner;
    }

    public IDisposable BeginScope<TState>(TState state) => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        var message = $"[{logLevel}]: {state}";
        _buff.Enqueue(message);
        _inner.Log(logLevel, eventId, state, exception, formatter);
    }

    public void Clear() => _buff.Clear();

    public List<string> Dump() => _buff.ToList();
}
