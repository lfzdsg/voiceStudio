param([string]$OutputDirectory = 'dist/VoiceStudio-v4')
$ErrorActionPreference = 'Stop'
if ($args -contains '--help') { Write-Host 'Run this script to build a portable Windows x64 app; existing model files are copied when available.'; exit }
Set-Location (Split-Path $PSScriptRoot -Parent)
& .\.venv\Scripts\python.exe -m pip install 'pyinstaller>=6,<7'
if ($LASTEXITCODE -ne 0) { throw 'PyInstaller installation failed.' }
& .\.venv\Scripts\python.exe -m PyInstaller --noconfirm --name voice-worker --onedir --distpath (Join-Path $OutputDirectory 'engine') --workpath .runtime\pyinstaller --specpath .runtime --collect-all faster_whisper --collect-all ctranslate2 --collect-all onnxruntime --collect-all tokenizers --collect-all av --collect-all sentencepiece --collect-all sherpa_onnx worker\main.py
if ($LASTEXITCODE -ne 0) { throw 'Worker packaging failed.' }
dotnet publish VoiceStudio.csproj -c Release -r win-x64 --self-contained true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Desktop packaging failed.' }
foreach ($modelName in @('tiny', 'base', 'small', 'medium', 'large-v3-turbo', 'large-v3', 'sensevoice-small', 'qwen3-4b-instruct')) {
    $sourceModel = Join-Path 'models' $modelName
    if (Test-Path (Join-Path $sourceModel '.ready')) {
        $targetModel = Join-Path (Join-Path $OutputDirectory 'models') $modelName
        New-Item -ItemType Directory -Force -Path $targetModel | Out-Null
        Get-ChildItem -LiteralPath $sourceModel -File -Force | Copy-Item -Destination $targetModel -Force
    }
}
foreach ($pair in @('en_zh', 'zh_en', 'ja_en', 'en_ja')) {
    $sourceModel = Join-Path 'models/translation' $pair
    if (Test-Path (Join-Path $sourceModel '.ready')) {
        $targetModel = Join-Path (Join-Path $OutputDirectory 'models/translation') $pair
        New-Item -ItemType Directory -Force -Path $targetModel | Out-Null
        Get-ChildItem -LiteralPath $sourceModel -Force | Copy-Item -Destination $targetModel -Recurse -Force
    }
}
if (Test-Path 'engine/llama/.ready') {
    $llamaTarget = Join-Path $OutputDirectory 'engine/llama'
    New-Item -ItemType Directory -Force -Path $llamaTarget | Out-Null
    Get-ChildItem 'engine/llama' -File -Force | Copy-Item -Destination $llamaTarget -Force
}
$nvidiaRoot = '.venv/Lib/site-packages/nvidia'
if (Test-Path $nvidiaRoot) {
    $cudaTarget = Join-Path $OutputDirectory 'cuda'
    New-Item -ItemType Directory -Force -Path $cudaTarget | Out-Null
    Get-ChildItem -LiteralPath $nvidiaRoot -Recurse -File -Filter '*.dll' | Copy-Item -Destination $cudaTarget -Force
    Get-ChildItem '.venv/Lib/site-packages' -Directory -Filter 'nvidia*dist-info' | ForEach-Object {
        $licenseTarget = Join-Path (Join-Path $OutputDirectory 'licenses') $_.Name
        New-Item -ItemType Directory -Force -Path $licenseTarget | Out-Null
        Get-ChildItem -LiteralPath $_.FullName -Recurse -File | Where-Object { $_.Name -match 'LICENSE|EULA' } | Copy-Item -Destination $licenseTarget -Force
    }
}
Write-Host "Portable app is in $OutputDirectory. Keep the whole folder in a writable location."
