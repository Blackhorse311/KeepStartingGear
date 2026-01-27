$env:SPT_PATH = "H:\SPT"
Set-Location "I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\src\server"
Write-Host "Building with SPT_PATH=$env:SPT_PATH"
& dotnet build Blackhorse311.KeepStartingGear.csproj -c Release
