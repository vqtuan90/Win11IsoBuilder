namespace Win11IsoBuilder.Models;

/// <summary>A single progress update emitted by the build pipeline to the UI.</summary>
/// <param name="Percent">Overall completion 0–100.</param>
/// <param name="Stage">Short stage label, e.g. "Extracting ISO".</param>
/// <param name="Message">Detail line for the log/status.</param>
/// <param name="IsError">True if this update reports a failure.</param>
public sealed record BuildProgress(int Percent, string Stage, string Message, bool IsError = false);
