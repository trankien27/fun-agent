namespace FunStudio.WindowsMaintenance.Agent.Models;

public sealed class ServiceTaskPayload
{
    public string ServiceName { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class FolderInfoPayload
{
    public string FolderKey { get; set; } = "";
}

public sealed class ListFolderFilesPayload
{
    public string FolderKey { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool Recursive { get; set; }
}

public sealed class DownloadToFolderPayload
{
    public string FolderKey { get; set; } = "";
    public string FileUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Overwrite { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class ExtractZipToFolderPayload
{
    public string FolderKey { get; set; } = "";
    public string FileUrl { get; set; } = "";
    public bool Overwrite { get; set; }
    public bool CleanTargetBeforeExtract { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class DeleteFolderFilePayload
{
    public string FolderKey { get; set; } = "";
    public string RelativePath { get; set; } = "";
}

public sealed class CleanFolderPayload
{
    public string FolderKey { get; set; } = "";
}

public sealed class IisTaskPayload
{
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class PowerShellPayload
{
    public string Script { get; set; } = "";
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class PowerShellFilePayload
{
    public string ScriptPath { get; set; } = "";
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class DeploymentTaskPayload
{
    public string FileUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 300;
    public bool CleanTargetBeforeExtract { get; set; }
}
