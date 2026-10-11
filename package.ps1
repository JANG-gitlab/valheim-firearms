# Compile en Release et produit dist\ValheimFirearms-<version>.zip, à décompresser dans BepInEx\plugins\.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
& $dotnet build -c Release -p:DeployToProfile=false | Out-Host
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué" }

$version = ([xml](Get-Content ValheimFirearms.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = Join-Path $PSScriptRoot "dist\stage"
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$stage\ValheimFirearms" | Out-Null
Copy-Item bin\Release\ValheimFirearms.dll "$stage\ValheimFirearms\"
Copy-Item README.md, CREDITS.md "$stage\ValheimFirearms\"

$zip = Join-Path $PSScriptRoot "dist\ValheimFirearms-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage\ValheimFirearms" -DestinationPath $zip
Remove-Item -Recurse -Force $stage
Write-Host "-> $zip"
