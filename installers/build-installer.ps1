# Compila iFusion en Release, firma el ejecutable y genera installers\Output\iFusion Setup.exe (firmado).
# Requiere Visual Studio (MSBuild), Inno Setup 6 y el certificado descrito en sign-file.ps1.
$ErrorActionPreference = "Stop"

$here = $PSScriptRoot
$project = Join-Path $here "..\iFusion.csproj"
$msbuild = Get-ChildItem "C:\Program Files\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\MSBuild.exe" | Select-Object -First 1
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "C:\Program Files (x86)\Inno Setup 6\ISCC.exe", "C:\Program Files\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild no encontrado" }
if (-not $iscc) { throw "Inno Setup 6 no encontrado" }

$signScript = Join-Path $here "sign-file.ps1"

& $msbuild.FullName $project -restore -p:Configuration=Release -v:m -nologo
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion" }

& $signScript -Path (Join-Path $here "..\bin\Release\net472\iFusion.exe")

$signTool = "ifusionsign=powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$signScript`" `$f"
& $iscc "/S$signTool" (Join-Path $here "iFusion.iss")
if ($LASTEXITCODE -ne 0) { throw "Fallo Inno Setup" }

Get-ChildItem (Join-Path $here "Output") -Filter "*.exe" | ForEach-Object {
    "{0}  [{1}]" -f $_.Name, (Get-AuthenticodeSignature $_.FullName).Status
}
