using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace FireWill.App.Services.Background;

public interface IBackgroundAssetExtractor
{
    Task<string> ExtractAsync(
        BackgroundDescriptor descriptor,
        CancellationToken cancellationToken = default);
}

public sealed class EmbeddedBackgroundAssetExtractor : IBackgroundAssetExtractor
{
    private readonly Assembly _assembly;
    private readonly string _cacheRoot;
    private readonly string? _developmentAssetDirectory;
    private readonly SemaphoreSlim _extractionLock = new(1, 1);

    public EmbeddedBackgroundAssetExtractor(
        Assembly assembly,
        string? cacheRoot = null,
        string? developmentAssetDirectory = null)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _cacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FireWill",
            "BackgroundCache");
        _developmentAssetDirectory = developmentAssetDirectory;
    }

    public async Task<string> ExtractAsync(
        BackgroundDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateSha256(descriptor.Sha256);

        var targetDirectory = Path.Combine(_cacheRoot, descriptor.Sha256[..16].ToLowerInvariant());
        var targetPath = Path.Combine(targetDirectory, descriptor.FileName);

        await _extractionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(targetPath)
                && await HasExpectedSha256Async(targetPath, descriptor.Sha256, cancellationToken)
                    .ConfigureAwait(false))
            {
                return targetPath;
            }

            Directory.CreateDirectory(targetDirectory);
            var temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                await using var input = OpenAssetStream(descriptor);
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!await HasExpectedSha256Async(temporaryPath, descriptor.Sha256, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new InvalidDataException($"Background asset hash mismatch: {descriptor.FileName}");
                }

                File.Move(temporaryPath, targetPath, true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }

            return targetPath;
        }
        finally
        {
            _extractionLock.Release();
        }
    }

    private Stream OpenAssetStream(BackgroundDescriptor descriptor)
    {
        var embedded = _assembly.GetManifestResourceStream(descriptor.ResourceName);
        if (embedded is not null)
        {
            return embedded;
        }

        if (!string.IsNullOrWhiteSpace(_developmentAssetDirectory))
        {
            var developmentPath = Path.Combine(_developmentAssetDirectory, descriptor.FileName);
            if (File.Exists(developmentPath))
            {
                return File.OpenRead(developmentPath);
            }
        }

        throw new FileNotFoundException(
            $"Embedded background resource '{descriptor.ResourceName}' was not found. " +
            "The application project must embed assets/backgrounds/*.mp4 with the declared logical names.");
    }

    private static async Task<bool> HasExpectedSha256Async(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateSha256(string sha256)
    {
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException($"Invalid SHA-256 value: '{sha256}'.");
        }
    }
}
