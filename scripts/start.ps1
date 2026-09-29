$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$appPath = Join-Path $projectRoot 'bin\Release\net8.0-windows\VoiceStudio.exe'
if (Test-Path (Join-Path $projectRoot 'dist\VoiceStudio\VoiceStudio.exe')) { $appPath = Join-Path $projectRoot 'dist\VoiceStudio\VoiceStudio.exe' }
if (-not (Test-Path $appPath)) { throw 'Run scripts/setup.ps1 first.' }
Start-Process -FilePath $appPath
