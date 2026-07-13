# Builds Bot Census (Release) and auto-deploys BotCensus.dll to BepInEx\plugins\BotCensus.
# Uses the user-local .NET SDK if present, otherwise whatever `dotnet` is on PATH.
$ErrorActionPreference = "Stop"
$dotnet = if (Test-Path "$env:USERPROFILE\dotnet\dotnet.exe") { "$env:USERPROFILE\dotnet\dotnet.exe" } else { "dotnet" }
& $dotnet build "$PSScriptRoot\BotCensus.csproj" -c Release
