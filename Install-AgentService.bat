@echo off
setlocal

set "SERVICE_NAME=FunStudioMaintenanceAgent"
set "DISPLAY_NAME=FunStudio Maintenance Agent"
set "AGENT_DIR=D:\FunStudio\agent"
set "AGENT_EXE=%AGENT_DIR%\FunStudio.WindowsMaintenance.Agent.exe"
set "DOTNET_RUNTIME_NAME=Microsoft.AspNetCore.App"
set "DOTNET_RUNTIME_MAJOR=8."
set "DOTNET_WINGET_ID=Microsoft.DotNet.AspNetCore.8"
set "DOTNET_DOWNLOAD_URL=https://dotnet.microsoft.com/en-us/download/dotnet/8.0"

net session >nul 2>&1
if not "%ERRORLEVEL%"=="0" (
    echo This script must be run as Administrator.
    echo Right-click Install-AgentService.bat and choose "Run as administrator".
    pause
    exit /b 1
)

if not exist "%AGENT_EXE%" (
    echo Agent executable not found:
    echo %AGENT_EXE%
    echo.
    echo Publish/copy the agent to %AGENT_DIR% first.
    pause
    exit /b 1
)

call :EnsureDotNetRuntime
if errorlevel 1 (
    pause
    exit /b 1
)

sc query "%SERVICE_NAME%" >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    echo Service exists. Reconfiguring %SERVICE_NAME%...
    sc stop "%SERVICE_NAME%" >nul 2>&1
    timeout /t 3 /nobreak >nul
    sc config "%SERVICE_NAME%" binPath= "\"%AGENT_EXE%\"" start= auto DisplayName= "%DISPLAY_NAME%"
) else (
    echo Creating service %SERVICE_NAME%...
    sc create "%SERVICE_NAME%" binPath= "\"%AGENT_EXE%\"" start= auto DisplayName= "%DISPLAY_NAME%"
)

if not "%ERRORLEVEL%"=="0" (
    echo Failed to create/configure service.
    pause
    exit /b 1
)

echo Starting service %SERVICE_NAME%...
sc start "%SERVICE_NAME%"

echo.
sc query "%SERVICE_NAME%"
echo.
echo Done.
pause
exit /b 0

:EnsureDotNetRuntime
echo Checking .NET Runtime...
where dotnet >nul 2>&1
if not errorlevel 1 (
    dotnet --list-runtimes | findstr /I /C:"%DOTNET_RUNTIME_NAME% %DOTNET_RUNTIME_MAJOR%" >nul 2>&1
    if not errorlevel 1 (
        echo .NET %DOTNET_RUNTIME_NAME% %DOTNET_RUNTIME_MAJOR%x is installed.
        echo.
        exit /b 0
    )
)

echo .NET %DOTNET_RUNTIME_NAME% %DOTNET_RUNTIME_MAJOR%x is missing.
echo Installing .NET 8 ASP.NET Core Runtime with winget...
where winget >nul 2>&1
if errorlevel 1 (
    echo winget is not available on this machine.
    echo Please install .NET 8 ASP.NET Core Runtime manually:
    echo %DOTNET_DOWNLOAD_URL%
    echo Then run this script again.
    exit /b 1
)

winget install --id "%DOTNET_WINGET_ID%" -e --silent --accept-package-agreements --accept-source-agreements
if errorlevel 1 (
    echo Failed to install .NET Runtime with winget.
    echo Please install .NET 8 ASP.NET Core Runtime manually:
    echo %DOTNET_DOWNLOAD_URL%
    echo Then run this script again.
    exit /b 1
)

dotnet --list-runtimes | findstr /I /C:"%DOTNET_RUNTIME_NAME% %DOTNET_RUNTIME_MAJOR%" >nul 2>&1
if errorlevel 1 (
    echo .NET Runtime install finished but runtime was not detected.
    echo Please restart Command Prompt or install manually:
    echo %DOTNET_DOWNLOAD_URL%
    exit /b 1
)

echo .NET Runtime installed successfully.
echo.
exit /b 0
