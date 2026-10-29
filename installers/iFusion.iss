; Instalador iFusion - Inno Setup 6 (mismo esquema que los instaladores de WOW Apps)
; Se genera con build-installer.ps1 (compila Release, firma el exe y empaqueta).
; Salida: Output\iFusion Setup.exe

#define AppName      "iFusion"
#define AppVersion   "1.0.0"
#define AppPublisher "Condor"
#define AppExe       "iFusion.exe"
#define BuildDir     "..\bin\Release\net472"

[Setup]
AppId={{C4F2B8E1-7A31-4E5D-9B62-3D6F8A1C0E04}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; El asistente pregunta si se instala solo para el usuario actual o para todos los usuarios de Windows.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppPublisher}\{#AppName}
DefaultGroupName=WOW Software
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename={#AppName} Setup
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Instalador de {#AppName}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
WizardStyle=modern
SignTool=ifusionsign
SignedUninstaller=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; Flags: checkedonce
Name: "trustcert"; Description: "Confiar en el certificado de firma de iFusion (solo si Windows bloquea la app; Windows pedira confirmacion)"; GroupDescription: "Seguridad:"; Flags: unchecked

[Files]
Source: "{#BuildDir}\{#AppExe}";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#BuildDir}\*.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "..\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "iFusion-Signing.cer"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\WOW Software\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
; Solo agrega este certificado publico a los almacenes del USUARIO ACTUAL (Windows muestra su propia confirmacion).
Filename: "{sys}\certutil.exe"; Parameters: "-user -f -addstore Root ""{app}\iFusion-Signing.cer"""; Flags: runhidden waituntilterminated; Tasks: trustcert
Filename: "{sys}\certutil.exe"; Parameters: "-user -f -addstore TrustedPublisher ""{app}\iFusion-Signing.cer"""; Flags: runhidden waituntilterminated; Tasks: trustcert
Filename: "{app}\{#AppExe}"; Description: "Iniciar {#AppName}"; Flags: nowait postinstall skipifsilent
