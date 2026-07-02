# FunStudio Windows Maintenance Agent

Local Agent chay tren tung may Windows de nhan task tu Central IT Support API qua SignalR va thuc thi tac vu local. Agent nay la Windows Service doc lap, khong deploy duoi IIS, nen task `RESTART_IIS` khong lam chet agent.

## Stack

- .NET 8 Worker Service (`net8.0-windows`)
- Windows Service hosting
- Microsoft.AspNetCore.SignalR.Client
- System.ServiceProcess.ServiceController
- System.IO.Compression.ZipFile
- ILogger local logging

## SignalR

Agent ket noi den:

```text
/hubs/remote-agent?machineCode=BOOTH-01&agentKey=xxx&agentVersion=1.0.0
```

Agent listen event:

```text
ReceiveTask
```

Agent invoke ve Central API:

```text
TaskStarted
TaskLog
TaskCompleted
```

Khi reconnect, agent se goi fallback endpoint mau:

```text
GET /api/remote-agent/pending-tasks?machineCode=BOOTH-01
Header: X-Agent-Key: secret-key-booth-01
```

Neu Central API cua ban dung route khac, sua method `FetchPendingTasksAsync` trong `Worker.cs`.

## Cau hinh tung may

Sua `appsettings.json` tren moi may:

```json
{
  "Agent": {
    "ServerUrl": "https://your-it-support-api.com",
    "MachineCode": "BOOTH-01",
    "AgentKey": "secret-key-booth-01",
    "AgentVersion": "1.0.0"
  },
  "ManagedServices": [
    "FSAsyncTransaction",
    "FSUpdateSync"
  ],
  "ManagedFolders": {
    "FS_UPDATE_APP_FORM": "D:\\FunStudio\\deploy\\FSUpdateAppForm",
    "BE_PHOTOBOOTH_DEPLOY": "D:\\FunStudio\\deploy\\be-photobooth-deploy",
    "FS_ASYNC_TRANSACTION_INSTALLER": "D:\\FunStudio\\deploy\\FSAsyncTransactionInstaller",
    "FS_UPDATE_SYNC_INSTALLER": "D:\\FunStudio\\deploy\\FSUpdateSyncInstaller"
  },
  "PowerShell": {
    "MaxTimeoutSeconds": 300,
    "MaxOutputLength": 100000,
    "AllowAdminPowerShell": true,
    "AllowUserPowerShell": true
  }
}
```

## Task duoc ho tro

Service task:

```text
GET_SERVICE_STATUS
START_SERVICE
STOP_SERVICE
RESTART_SERVICE
```

Folder task:

```text
GET_FOLDER_INFO
LIST_FOLDER_FILES
DOWNLOAD_TO_FOLDER
EXTRACT_ZIP_TO_FOLDER
DELETE_FOLDER_FILE
CLEAN_FOLDER
```

IIS task:

```text
START_IIS
STOP_IIS
RESTART_IIS
```

PowerShell task:

```text
RUN_POWERSHELL_ADMIN
RUN_POWERSHELL_USER
```

Deployment task:

```text
UPDATE_VERSION
DEPLOY_FS_ASYNC_TRANSACTION
DEPLOY_FS_UPDATE_SYNC
DEPLOY_APP_FORM
```

Payload deployment:

```json
{
  "fileUrl": "https://example.com/package.zip",
  "timeoutSeconds": 300,
  "cleanTargetBeforeExtract": false
}
```

Workflow:

- `UPDATE_VERSION`: stop IIS, extract zip vao `BE_PHOTOBOOTH_DEPLOY`, start IIS, chay `iisreset /restart`.
- `DEPLOY_FS_ASYNC_TRANSACTION`: stop `FSAsyncTransaction`, extract zip vao `FS_ASYNC_TRANSACTION_INSTALLER` voi overwrite, start service.
- `DEPLOY_FS_UPDATE_SYNC`: stop `FSUpdateSync`, extract zip vao `FS_UPDATE_SYNC_INSTALLER` voi overwrite, start service.
- `DEPLOY_APP_FORM`: extract zip vao `FS_UPDATE_APP_FORM` voi overwrite.

## Bao mat folder

- Central API chi gui `folderKey`, khong gui absolute path.
- Agent map `folderKey` sang path local trong `ManagedFolders`.
- Tat ca path con dung `Path.GetFullPath` va phai nam ben trong managed folder.
- Delete file chi nhan `relativePath`.
- Extract zip kiem tra tung entry de chan Zip Slip.

## Test task khi chua co Central API

Co the chay task truc tiep bang file JSON, khong can SignalR/BE admin:

```powershell
dotnet run -- --run-task .\sample-tasks\powershell-whoami.json
```

Do thu muc `obj` co the bi loi quyen tren may dev, co the dung output tam:

```powershell
dotnet run /p:BaseIntermediateOutputPath=C:\tmp\fsagent-obj\ /p:BaseOutputPath=C:\tmp\fsagent-bin\ -- --run-task .\sample-tasks\powershell-whoami.json
```

Cac sample task:

```text
sample-tasks\powershell-whoami.json
sample-tasks\folder-info.json
sample-tasks\list-folder-files.json
sample-tasks\get-service-status.json
```

File JSON co format giong message SignalR:

```json
{
  "taskId": "00000000-0000-0000-0000-000000000001",
  "taskType": "RUN_POWERSHELL_USER",
  "payload": {
    "script": "whoami; Get-Date",
    "timeoutSeconds": 30
  }
}
```

Lenh nay execute executor that, in `TaskLog` va `TaskCompletedDto` ra console, nhung khong connect len Central API.

## Test task qua Local Web API

Windows Service co nhung san local API de test task truc tiep tu may local:

```text
UI   http://localhost:8787/
GET  http://localhost:8787/health
GET  http://localhost:8787/api/local/status
GET  http://localhost:8787/api/local/task-types
GET  http://localhost:8787/api/local/received-tasks
POST http://localhost:8787/api/local/tasks/run
```

Mo `http://localhost:8787/` de test thu cong bang giao dien. UI co san cac tab PowerShell, Service, Folder, IIS, Received va JSON raw.

Vi du chay PowerShell task:

```powershell
$body = @{
  taskType = "RUN_POWERSHELL_USER"
  payload = @{
    script = "whoami; Get-Date"
    timeoutSeconds = 30
  }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:8787/api/local/tasks/run" `
  -ContentType "application/json" `
  -Body $body
```

Vi du xem folder info:

```powershell
$body = @{
  taskType = "GET_FOLDER_INFO"
  payload = @{
    folderKey = "BE_PHOTOBOOTH_DEPLOY"
  }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:8787/api/local/tasks/run" `
  -ContentType "application/json" `
  -Body $body
```

Cau hinh Local API trong `appsettings.json`:

```json
"LocalApi": {
  "Enabled": true,
  "Urls": [
    "http://localhost:8787"
  ],
  "RequireApiKey": false,
  "ApiKey": ""
}
```

Neu bat `RequireApiKey`, gui them header:

```powershell
-Headers @{ "X-Local-Agent-Key" = "your-local-api-key" }
```

## Publish

Chay tren may build Windows:

```powershell
dotnet restore
dotnet publish .\FunStudio.WindowsMaintenance.Agent.csproj -c Release -r win-x64 --self-contained true -o D:\FunStudio\agent
```

Neu may da cai .NET 8 Runtime, co the publish framework-dependent:

```powershell
dotnet publish .\FunStudio.WindowsMaintenance.Agent.csproj -c Release -r win-x64 --self-contained false -o D:\FunStudio\agent
```

## Cai Windows Service

Chay PowerShell bang quyen Administrator.

Dung `sc.exe`:

```powershell
sc.exe create FunStudioMaintenanceAgent binPath= "D:\FunStudio\agent\FunStudio.WindowsMaintenance.Agent.exe" start= auto
sc.exe start FunStudioMaintenanceAgent
```

Hoac dung PowerShell:

```powershell
New-Service -Name "FunStudioMaintenanceAgent" -BinaryPathName "D:\FunStudio\agent\FunStudio.WindowsMaintenance.Agent.exe" -StartupType Automatic
Start-Service FunStudioMaintenanceAgent
```

Khuyen nghi agent chay bang `LocalSystem` hoac local admin user de co quyen:

- Start/stop Windows Service
- Restart IIS
- Ghi file vao `D:\FunStudio\deploy`
- Chay PowerShell admin

## Quan ly service

```powershell
Get-Service FunStudioMaintenanceAgent
Restart-Service FunStudioMaintenanceAgent
Stop-Service FunStudioMaintenanceAgent
```

Xoa service:

```powershell
Stop-Service FunStudioMaintenanceAgent
sc.exe delete FunStudioMaintenanceAgent
```
