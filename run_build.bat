@echo off
echo Starting build...
set SPT_PATH=H:\SPT
echo SPT_PATH is set to: %SPT_PATH%
cd /d I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\src\server
echo Current directory: %CD%
dotnet build Blackhorse311.KeepStartingGear.csproj -c Release
echo Build completed with exit code: %ERRORLEVEL%
