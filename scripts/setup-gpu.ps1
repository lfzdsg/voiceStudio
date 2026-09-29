$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& .\.venv\Scripts\python.exe -m pip install --timeout 120 --retries 5 -r worker\requirements.gpu.txt
if ($LASTEXITCODE -ne 0) { throw 'GPU runtime installation failed.' }
& .\.venv\Scripts\python.exe -c "import sys; sys.path.insert(0, 'worker'); from catalog import select_device; print(select_device('cuda'))"
if ($LASTEXITCODE -ne 0) { throw 'GPU runtime detection failed. Check the NVIDIA driver.' }
Write-Host 'Project GPU runtime is ready. Package again to include the DLLs in a portable build.'
