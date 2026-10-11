# Lance Valheim avec le profil Thunderstore indiqué (même chose que le bouton "Start modded" de TMM).
param([string]$Profile = "luigi")
$game = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim.exe"
$preloader = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\Valheim\profiles\$Profile\BepInEx\core\BepInEx.Preloader.dll"
Start-Process -FilePath $game -WorkingDirectory (Split-Path $game) -ArgumentList @("--doorstop-enabled", "true", "--doorstop-target-assembly", "`"$preloader`"")
