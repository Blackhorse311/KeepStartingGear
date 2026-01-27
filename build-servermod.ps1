$env:SPT_PATH = "H:\SPT"
Set-Location "I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\src\servermod"
Write-Host "Building servermod with SPT_PATH=$env:SPT_PATH"
& dotnet build Blackhorse311.KeepStartingGear.Server.csproj -c Release
