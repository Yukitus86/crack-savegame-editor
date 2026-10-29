# Builds the self-contained single-file editor into .\dist\CrackSaveEditor.exe
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet publish src\CrackSaveEditor\CrackSaveEditor.csproj -c Release -o dist -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Get-Item dist\CrackSaveEditor.exe
"{0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)
