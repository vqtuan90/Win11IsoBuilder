using System.Diagnostics;
using System.Text;

namespace Win11IsoBuilder.Services;

/// <summary>Result of an external process invocation.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Async wrapper around external executables (dism, oscdimg, setup.exe, robocopy...).
/// Captures stdout/stderr + exit code, supports cancellation, timeout, and optional
/// live line streaming for the build log. Single choke-point keeps process handling DRY.
/// </summary>
public sealed class ProcessRunner
{
    private readonly ILogSink? _log;

    public ProcessRunner(ILogSink? log = null) => _log = log;

    /// <param name="onOutputLine">Optional callback per stdout/stderr line (live log).</param>
    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _log?.Info($"RUN: {fileName} {arguments}");

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
            _log?.Trace(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
            _log?.Trace(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process: {fileName}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Combine caller cancellation with an optional timeout.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } t) linkedCts.CancelAfter(t);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            var reason = cancellationToken.IsCancellationRequested ? "cancelled" : "timed out";
            _log?.Error($"Process {reason}: {fileName}");
            throw;
        }

        var result = new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        _log?.Info($"EXIT {result.ExitCode}: {fileName}");
        return result;
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _log?.Error($"Failed to kill process: {ex.Message}");
        }
    }
}
