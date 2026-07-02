$ErrorActionPreference = "Stop"

$projectPath = "D:\PersonalProject\FunStudio.WindowsMaintenance.Agent\FunStudio.WindowsMaintenance.Agent.csproj"
$publishPath = "D:\FunStudio\agent"
$serviceName = "FunStudioMaintenanceAgent"

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne "Stopped") {
    Stop-Service -Name $serviceName -Force
    $service.WaitForStatus("Stopped", "00:00:30")
}

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishPath `
    /p:BaseIntermediateOutputPath=C:\tmp\fsagent-obj\ `
    /p:BaseOutputPath=C:\tmp\fsagent-bin\

if ($null -eq $service) {
    New-Service -Name $serviceName `
        -DisplayName "FunStudio Maintenance Agent" `
        -BinaryPathName "$publishPath\FunStudio.WindowsMaintenance.Agent.exe" `
        -StartupType Automatic
}

Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table Name, Status, StartType -AutoSize
