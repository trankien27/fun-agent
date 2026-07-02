using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent.Executors;

public sealed class ManagedFolderExecutor
{
    private readonly ManagedFolderOptions _folders;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ManagedFolderExecutor> _logger;

    public ManagedFolderExecutor(
        IOptions<ManagedFolderOptions> folders,
        IHttpClientFactory httpClientFactory,
        ILogger<ManagedFolderExecutor> logger)
    {
        _folders = folders.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<TaskExecutionResult> GetFolderInfoAsync(FolderInfoPayload payload, CancellationToken cancellationToken)
    {
        return SafeExecute(() =>
        {
            var root = ResolveRoot(payload.FolderKey);
            Directory.CreateDirectory(root);

            var directory = new DirectoryInfo(root);
            var result = new
            {
                payload.FolderKey,
                Path = root,
                Exists = directory.Exists,
                FileCount = directory.EnumerateFiles("*", SearchOption.AllDirectories).Count(),
                TotalBytes = directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length),
                LastWriteTimeUtc = directory.LastWriteTimeUtc
            };

            return TaskExecutionResult.Success(JsonSerializer.Serialize(result, JsonOptions()));
        });
    }

    public Task<TaskExecutionResult> ListFolderFilesAsync(ListFolderFilesPayload payload, CancellationToken cancellationToken)
    {
        return SafeExecute(() =>
        {
            var root = ResolveRoot(payload.FolderKey);
            var target = ResolveChildPath(root, payload.RelativePath);
            Directory.CreateDirectory(target);

            var files = Directory.EnumerateFileSystemEntries(
                    target,
                    "*",
                    payload.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var attributes = File.GetAttributes(path);
                    var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                    var info = new FileInfo(path);
                    return new
                    {
                        RelativePath = Path.GetRelativePath(root, path),
                        IsDirectory = isDirectory,
                        SizeBytes = isDirectory ? 0 : info.Length,
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(path)
                    };
                });

            return TaskExecutionResult.Success(JsonSerializer.Serialize(files, JsonOptions()));
        });
    }

    public async Task<TaskExecutionResult> DownloadToFolderAsync(DownloadToFolderPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var root = ResolveRoot(payload.FolderKey);
            Directory.CreateDirectory(root);

            if (string.IsNullOrWhiteSpace(payload.FileName) || payload.FileName != Path.GetFileName(payload.FileName))
            {
                return TaskExecutionResult.Failure("fileName must be a simple file name.");
            }

            var target = ResolveChildPath(root, payload.FileName);
            if (File.Exists(target) && !payload.Overwrite)
            {
                return TaskExecutionResult.Failure($"File '{payload.FileName}' already exists and overwrite=false.");
            }

            await DownloadFileAsync(payload.FileUrl, target, payload.TimeoutSeconds, cancellationToken);
            return TaskExecutionResult.Success($"Downloaded '{payload.FileName}' to '{payload.FolderKey}'.");
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Failure(ex.Message);
        }
    }

    public async Task<TaskExecutionResult> ExtractZipToFolderAsync(ExtractZipToFolderPayload payload, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.zip");

        try
        {
            var root = ResolveRoot(payload.FolderKey);
            Directory.CreateDirectory(root);

            await DownloadFileAsync(payload.FileUrl, tempFile, payload.TimeoutSeconds, cancellationToken);

            if (payload.CleanTargetBeforeExtract)
            {
                CleanDirectory(root);
            }

            using var archive = ZipFile.OpenRead(tempFile);
            foreach (var entry in archive.Entries)
            {
                var destinationPath = ResolveChildPath(root, entry.FullName);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                if (File.Exists(destinationPath) && !payload.Overwrite)
                {
                    return TaskExecutionResult.Failure($"File '{entry.FullName}' already exists and overwrite=false.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, payload.Overwrite);
            }

            return TaskExecutionResult.Success($"Extracted zip into '{payload.FolderKey}'.");
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Failure(ex.Message);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    public Task<TaskExecutionResult> DeleteFolderFileAsync(DeleteFolderFilePayload payload, CancellationToken cancellationToken)
    {
        return SafeExecute(() =>
        {
            var root = ResolveRoot(payload.FolderKey);
            var target = ResolveChildPath(root, payload.RelativePath);

            if (Directory.Exists(target))
            {
                return TaskExecutionResult.Failure("DELETE_FOLDER_FILE only deletes files. Use CLEAN_FOLDER for folders.");
            }

            if (!File.Exists(target))
            {
                return TaskExecutionResult.Success($"File '{payload.RelativePath}' does not exist.");
            }

            File.Delete(target);
            return TaskExecutionResult.Success($"Deleted '{payload.RelativePath}'.");
        });
    }

    public Task<TaskExecutionResult> CleanFolderAsync(CleanFolderPayload payload, CancellationToken cancellationToken)
    {
        return SafeExecute(() =>
        {
            var root = ResolveRoot(payload.FolderKey);
            Directory.CreateDirectory(root);
            CleanDirectory(root);
            return TaskExecutionResult.Success($"Cleaned folder '{payload.FolderKey}'.");
        });
    }

    private Task<TaskExecutionResult> SafeExecute(Func<TaskExecutionResult> action)
    {
        try
        {
            return Task.FromResult(action());
        }
        catch (Exception ex)
        {
            return Task.FromResult(TaskExecutionResult.Failure(ex.Message));
        }
    }

    private string ResolveRoot(string folderKey)
    {
        if (string.IsNullOrWhiteSpace(folderKey) || !_folders.TryGetValue(folderKey, out var configuredPath))
        {
            throw new InvalidOperationException($"Folder key '{folderKey}' is not configured in ManagedFolders.");
        }

        return EnsureTrailingSeparator(Path.GetFullPath(configuredPath));
    }

    private string ResolveChildPath(string root, string? relativePath)
    {
        relativePath ??= "";
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Absolute paths are not allowed.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path traversal detected.");
        }

        return fullPath;
    }

    private async Task DownloadFileAsync(string fileUrl, string targetPath, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("fileUrl must be an absolute http or https URL.");
        }

        timeoutSeconds = timeoutSeconds <= 0 ? 300 : timeoutSeconds;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var client = _httpClientFactory.CreateClient("agent-download");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var tempTarget = targetPath + ".download";
        await using (var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token))
        await using (var destination = File.Create(tempTarget))
        {
            await source.CopyToAsync(destination, timeoutCts.Token);
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempTarget, targetPath);

        var length = response.Content.Headers.ContentLength;
        _logger.LogInformation(
            "Downloaded {FileUrl} to {TargetPath}. ContentLength={ContentLength}",
            fileUrl,
            targetPath,
            length.HasValue ? length.Value.ToString() : "unknown");
    }

    private static void CleanDirectory(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root))
        {
            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true
    };
}
