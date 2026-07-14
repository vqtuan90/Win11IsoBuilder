using System.IO;
using System.Security.Cryptography;

namespace Win11IsoBuilder.Services;

/// <summary>
/// SHA-256 file verification shared by every download path (app catalog, driver catalog).
/// An empty expected hash means "no pin" and always verifies (TLS-only integrity).
/// </summary>
public static class HashVerifier
{
    public static async Task<bool> IsHashOkAsync(string path, string? expectedSha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
