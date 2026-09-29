$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
python -c "import sys,struct; assert sys.version_info[:2] == (3,12) and struct.calcsize('P') == 8, 'Use Python 3.12 x64 with the pinned dependencies.'"
if ($LASTEXITCODE -ne 0) { throw 'Python 3.12 x64 is required for this lockfile.' }
python -m venv .venv
if ($LASTEXITCODE -ne 0) { throw 'Could not create Python environment. Install Python 3.12 x64 first.' }
& .\.venv\Scripts\python.exe -m pip install -r worker\requirements.lock.txt
if ($LASTEXITCODE -ne 0) { throw 'Python dependencies failed to install.' }
dotnet restore VoiceStudio.csproj
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
dotnet build VoiceStudio.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'Setup complete. Run scripts/start.ps1, then download a model from the app.'
