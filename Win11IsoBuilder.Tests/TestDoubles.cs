using Win11IsoBuilder.Services;

namespace Win11IsoBuilder.Tests;

/// <summary>No-op log sink so services can be constructed in tests without IO.</summary>
internal sealed class NoOpLog : ILogSink
{
    public void Trace(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}
