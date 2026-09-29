$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& .\.venv\Scripts\python.exe -m unittest discover -s tests -p 'test_*.py' -v
if ($LASTEXITCODE -ne 0) { throw 'Python tests failed.' }
dotnet run --project tests\CoreTests\CoreTests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'C# tests failed.' }
