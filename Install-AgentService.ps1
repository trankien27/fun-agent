$ErrorActionPreference = "Stop"

$serviceName = "FunStudioMaintenanceAgent"
$displayName = "FunStudio Maintenance Agent"
$binaryPath = "D:\FunStudio\agent\FunStudio.WindowsMaintenance.Agent.exe"

if (-not (Test-Path -LiteralPath $binaryPath)) {
    throw "Agent executable not found: $binaryPath"
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    New-Service -Name $serviceName `
        -DisplayName $displayName `
        -BinaryPathName $binaryPath `
        -StartupType Automatic
} else {
    sc.exe config $serviceName binPath= $binaryPath start= auto | Out-Host
}

Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table Name, Status, StartType -AutoSize
