@echo off
setlocal
cd /d "%~dp0"

echo Recreating project file...
>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo ^<Project Sdk="Microsoft.NET.Sdk"^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo   ^<PropertyGroup^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo     ^<OutputType^>WinExe^</OutputType^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo     ^<TargetFramework^>net8.0-windows^</TargetFramework^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo     ^<UseWindowsForms^>true^</UseWindowsForms^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo     ^<ImplicitUsings^>enable^</ImplicitUsings^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo     ^<Nullable^>enable^</Nullable^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo     ^<LangVersion^>latest^</LangVersion^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo   ^</PropertyGroup^>
>>"NetworkCardMonitor\NetworkCardMonitor.csproj" echo ^</Project^>

for %%F in ("NetworkCardMonitor\Program.cs" "NetworkCardMonitor\MainForm.cs" "NetworkCardMonitor\SpeedOverlayForm.cs" "NetworkCardMonitor\Models\NetworkAdapterInfo.cs" "NetworkCardMonitor\Services\NetworkAdapterService.cs" "NetworkCardMonitor\Services\StartupService.cs") do (
    findstr /C:"END_OF_SOURCE_FILE" "%%~F" >nul
    if errorlevel 1 (
        echo Source file is incomplete: %%~F
        echo Copy or synchronize that file again, then retry.
        pause
        exit /b 1
    )
)

echo Building Windows application (offline-compatible)...
dotnet publish "NetworkCardMonitor\NetworkCardMonitor.csproj" --configuration Release --self-contained false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false --output "publish"

if errorlevel 1 (
    echo.
    echo Build failed. See the compiler error messages above.
    pause
    exit /b 1
)

echo.
echo Build completed: publish\NetworkCardMonitor.exe
echo Keep all files in the publish folder together.
pause
