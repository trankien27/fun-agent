@echo off
setlocal EnableExtensions

set "PROJECT_PATH=%~dp0FunStudio.WindowsMaintenance.Agent.csproj"
set "SERVICE_NAME=FunStudioMaintenanceAgent"
set "PUBLISH_DIR=D:\FunStudio\agent"
set "RUNTIME=win-x64"
set "CONFIGURATION=Release"
set "SELF_CONTAINED=false"
set "OBJ_DIR=C:\tmp\fsagent-obj"
set "BIN_DIR=C:\tmp\fsagent-bin"
set "APPSETTINGS=%PUBLISH_DIR%\appsettings.json"
set "APPSETTINGS_BAK=%TEMP%\FunStudio.WindowsMaintenance.Agent.appsettings.%RANDOM%.bak"

echo ============================================================
echo Publish FunStudio Windows Maintenance Agent
echo Project: %PROJECT_PATH%
echo Output : %PUBLISH_DIR%
echo Service: %SERVICE_NAME%
echo Runtime: %RUNTIME%
echo Self-contained: %SELF_CONTAINED%
echo ============================================================
echo.

if not exist "%PROJECT_PATH%" (
    echo Project file not found:
    echo %PROJECT_PATH%
    exit /b 1
)

if not exist "%PUBLISH_DIR%" (
    echo Creating publish folder: %PUBLISH_DIR%
    mkdir "%PUBLISH_DIR%"
    if errorlevel 1 exit /b 1
)

if exist "%APPSETTINGS%" (
    echo Backup current appsettings.json...
    copy /Y "%APPSETTINGS%" "%APPSETTINGS_BAK%" >nul
    if errorlevel 1 exit /b 1
)

sc query "%SERVICE_NAME%" >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    echo Stopping service %SERVICE_NAME% before publish...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$service = Get-Service -Name '%SERVICE_NAME%' -ErrorAction SilentlyContinue; if ($service -and $service.Status -ne 'Stopped') { Stop-Service -Name '%SERVICE_NAME%' -Force; $service.WaitForStatus('Stopped', '00:00:30') }"
    if errorlevel 1 (
        echo Failed to stop service %SERVICE_NAME%.
        if exist "%APPSETTINGS_BAK%" (
            echo Restoring appsettings.json backup...
            copy /Y "%APPSETTINGS_BAK%" "%APPSETTINGS%" >nul
        )
        exit /b 1
    )
)

dotnet publish "%PROJECT_PATH%" ^
    -c "%CONFIGURATION%" ^
    -r "%RUNTIME%" ^
    --self-contained "%SELF_CONTAINED%" ^
    -o "%PUBLISH_DIR%" ^
    /p:BaseIntermediateOutputPath=%OBJ_DIR%\ ^
    /p:BaseOutputPath=%BIN_DIR%\ ^
    /p:GenerateAssemblyInfo=false ^
    /p:GenerateTargetFrameworkAttribute=false

if errorlevel 1 (
    echo.
    echo Publish failed.
    if exist "%APPSETTINGS_BAK%" (
        echo Restoring appsettings.json backup...
        copy /Y "%APPSETTINGS_BAK%" "%APPSETTINGS%" >nul
    )
    exit /b 1
)

if exist "%APPSETTINGS_BAK%" (
    echo Restoring booth appsettings.json...
    copy /Y "%APPSETTINGS_BAK%" "%APPSETTINGS%" >nul
    del /Q "%APPSETTINGS_BAK%" >nul 2>&1
)

echo.
echo Publish completed successfully.
echo Output: %PUBLISH_DIR%
echo.
exit /b 0
