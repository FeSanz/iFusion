param([Parameter(Mandatory)][string]$Path)

# Firma un archivo con el certificado de firma de codigo de iFusion (PFX fuera del repo).
# Lo usa build-installer.ps1 para el .exe y, mediante el SignTool de Inno Setup, para el Setup y su desinstalador.
# Ruta del PFX y contrasena: variables de entorno IFUSION_PFX / IFUSION_PFX_PASSWORD, o por defecto
# %USERPROFILE%\.ifusion-signing\ifusion.pfx y password.txt (la contrasena nunca se guarda en el repo).
$ErrorActionPreference = "Stop"

$pfx = if ($env:IFUSION_PFX) { $env:IFUSION_PFX } else { Join-Path $env:USERPROFILE ".ifusion-signing\ifusion.pfx" }
$pfxPassword = if ($env:IFUSION_PFX_PASSWORD) { $env:IFUSION_PFX_PASSWORD } else { (Get-Content (Join-Path (Split-Path $pfx) "password.txt")).Trim() }
if (-not (Test-Path $pfx)) { Write-Error "No se encontro el certificado: $pfx"; exit 1 }

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $pfxPassword)
$result = Set-AuthenticodeSignature -FilePath $Path -Certificate $cert -HashAlgorithm SHA256
# Un certificado autofirmado no confiable en este equipo reporta UnknownError/NotTrusted pero la firma queda escrita;
# solo la ausencia de firma es un fallo real.
if (-not (Get-AuthenticodeSignature $Path).SignerCertificate) { Write-Error "Fallo al firmar $Path : $($result.StatusMessage)"; exit 1 }
exit 0

