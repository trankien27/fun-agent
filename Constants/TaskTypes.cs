namespace FunStudio.WindowsMaintenance.Agent.Constants;

public static class TaskTypes
{
    public const string GetServiceStatus = "GET_SERVICE_STATUS";
    public const string StartService = "START_SERVICE";
    public const string StopService = "STOP_SERVICE";
    public const string RestartService = "RESTART_SERVICE";

    public const string GetFolderInfo = "GET_FOLDER_INFO";
    public const string ListFolderFiles = "LIST_FOLDER_FILES";
    public const string DownloadToFolder = "DOWNLOAD_TO_FOLDER";
    public const string ExtractZipToFolder = "EXTRACT_ZIP_TO_FOLDER";
    public const string DeleteFolderFile = "DELETE_FOLDER_FILE";
    public const string CleanFolder = "CLEAN_FOLDER";

    public const string StartIis = "START_IIS";
    public const string StopIis = "STOP_IIS";
    public const string RestartIis = "RESTART_IIS";

    public const string RunPowerShellAdmin = "RUN_POWERSHELL_ADMIN";
    public const string RunPowerShellUser = "RUN_POWERSHELL_USER";
    public const string RunPowerShellFileAdmin = "RUN_POWERSHELL_FILE_ADMIN";
    public const string RunPowerShellFileUser = "RUN_POWERSHELL_FILE_USER";

    public const string GetUltraViewerPreferId = "GET_ULTRAVIEWER_PREFER_ID";

    public const string GetTransactions = "GET_TRANSACTIONS";
    public const string PrintImage = "PRINT_IMAGE";

    public const string UpdateVersion = "UPDATE_VERSION";
    public const string DeployFsAsyncTransaction = "DEPLOY_FS_ASYNC_TRANSACTION";
    public const string DeployFsUpdateSync = "DEPLOY_FS_UPDATE_SYNC";
    public const string DeployAppForm = "DEPLOY_APP_FORM";
}
