; =====================================================================================
;  CaféCostos · Instalador "llave en mano" (Inno Setup 6.3 o superior)
;
;  Qué hace, sin intervención del usuario:
;   1. Copia CafeCostos.exe (autocontenido: NO requiere instalar .NET).
;   2. Si faltan, DESCARGA de Microsoft e instala en silencio:
;        - Visual C++ Redistributable x64 (requisito de LocalDB)
;        - SQL Server 2022 Express LocalDB (si falla, intenta LocalDB 2019)
;      El usuario solo ve una barra de progreso; no tiene que buscar nada.
;   4. Ejecuta "CafeCostos.exe --inicializar" COMO EL USUARIO que instala (no como
;      administrador) para crear la instancia LocalDB, la base de datos comprimida y los
;      datos iniciales en %LOCALAPPDATA%\CafeCostos.
;   5. Crea accesos directos en el Escritorio y en el menú Inicio.
;
;  Compilar:  ISCC.exe CafeCostos.iss   (o ejecutar ..\publicar.ps1, que hace todo)
;  Requiere internet en el equipo del cliente solo si le faltan los componentes de Microsoft.
; =====================================================================================

#define AppNombre      "CaféCostos"
#define AppVersion     "1.0.0"
#define AppEditor      "CaféCostos"
#define AppExe         "CafeCostos.exe"
#define CarpetaPublish "..\publish\app"

[Setup]
AppId={{8F3C2B71-5D4E-4A9B-B6C1-2E7F0A9D3C58}
AppName={#AppNombre}
AppVersion={#AppVersion}
AppVerName={#AppNombre} {#AppVersion}
AppPublisher={#AppEditor}
DefaultDirName={autopf}\CafeCostos
DefaultGroupName={#AppNombre}
DisableProgramGroupPage=yes
DisableDirPage=auto
; LocalDB y el runtime de C++ se instalan para todo el equipo → requiere administrador.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\publish
OutputBaseFilename=CafeCostos_Instalador_{#AppVersion}
SetupIconFile=..\src\CafeCostos.App\Recursos\cafe.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppNombre}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
spanish.WelcomeLabel2=Este asistente instalará [name/ver] en su computador.%n%nSi a su computador le falta algún componente de Microsoft (motor de base de datos), el instalador lo descargará e instalará automáticamente. Solo necesita estar conectado a internet. No tiene que configurar nada.%n%nSe recomienda cerrar las demás aplicaciones antes de continuar.

[Files]
; Aplicación (un solo ejecutable autocontenido generado por publicar.ps1)
Source: "{#CarpetaPublish}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#CarpetaPublish}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Documentación para el usuario
Source: "..\docs\MANUAL_USUARIO.md"; DestDir: "{app}"; DestName: "Manual de usuario.txt"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\{#AppNombre}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Gestión y costeo para tostadores de café"
Name: "{group}\{#AppNombre}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\Manual de usuario"; Filename: "{app}\Manual de usuario.txt"
Name: "{group}\Desinstalar {#AppNombre}"; Filename: "{uninstallexe}"

[Run]
; 1) Runtime de Visual C++ (silencioso, sin reiniciar)
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Instalando componentes de Windows (Visual C++)..."; Flags: waituntilterminated; Check: FaltaVCRedist

; 2) SQL Server Express LocalDB (silencioso, acepta la licencia)
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\SqlLocalDB.msi"" /qn /norestart IACCEPTSQLLOCALDBLICENSETERMS=YES"; \
    StatusMsg: "Instalando el motor de base de datos (SQL Server LocalDB)..."; Flags: waituntilterminated; Check: FaltaLocalDB

; 3) Preparar la base de datos COMO EL USUARIO (LocalDB es por usuario)
Filename: "{app}\{#AppExe}"; Parameters: "--inicializar"; \
    StatusMsg: "Preparando la base de datos (puede tardar un minuto)..."; Flags: runhidden waituntilterminated runasoriginaluser

; 4) Abrir el programa al terminar
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppNombre} ahora"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
; Solo se borran archivos del programa. La información del negocio (%LOCALAPPDATA%\CafeCostos)
; y las copias de seguridad (Documentos\CaféCostos Respaldos) NO se eliminan nunca.
Type: filesandordirs; Name: "{app}"

[Code]
const
  URL_VCREDIST  = 'https://aka.ms/vs/17/release/vc_redist.x64.exe';
  URL_LOCALDB   = 'https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SqlLocalDB.msi';
  URL_LOCALDB19 = 'https://download.microsoft.com/download/8/4/c/84c6c430-e0f5-476d-bf43-eaaa222a72e0/SqlLocalDB.msi';
  LLAVE_LOCALDB = 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions';
  LLAVE_VCREDIST = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';

{ LocalDB 2019 (15.0) o 2022 (16.0) ya instalado → no se reinstala }
function FaltaLocalDB: Boolean;
begin
  Result := not (RegKeyExists(HKLM64, LLAVE_LOCALDB + '\16.0') or
                 RegKeyExists(HKLM64, LLAVE_LOCALDB + '\15.0'));
end;

{ Visual C++ 2015-2022 x64 }
function FaltaVCRedist: Boolean;
var
  Instalado: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM64, LLAVE_VCREDIST, 'Installed', Instalado) then
    Result := (Instalado <> 1);
end;

var
  PaginaDescarga: TDownloadWizardPage;

procedure InitializeWizard;
begin
  PaginaDescarga := CreateDownloadPage('Descargando componentes',
    'Se están descargando de Microsoft los componentes que necesita CaféCostos. Por favor espere...', nil);
end;

{ Descarga la lista actual de la página. Devuelve True si terminó bien. }
function Descargar: Boolean;
begin
  Result := False;
  PaginaDescarga.Show;
  try
    try
      PaginaDescarga.Download;
      Result := True;
    except
      if not PaginaDescarga.AbortedByUser then
        Log('Error de descarga: ' + GetExceptionMessage);
    end;
  finally
    PaginaDescarga.Hide;
  end;
end;

{ Al pulsar «Instalar»: descarga solo lo que falta, con reintento automático. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or not (FaltaVCRedist or FaltaLocalDB) then Exit;

  PaginaDescarga.Clear;
  if FaltaVCRedist then PaginaDescarga.Add(URL_VCREDIST, 'vc_redist.x64.exe', '');
  if FaltaLocalDB then PaginaDescarga.Add(URL_LOCALDB, 'SqlLocalDB.msi', '');
  if Descargar then Exit;

  { Segundo intento: LocalDB 2019 como alternativa }
  PaginaDescarga.Clear;
  if FaltaVCRedist and not FileExists(ExpandConstant('{tmp}\vc_redist.x64.exe')) then
    PaginaDescarga.Add(URL_VCREDIST, 'vc_redist.x64.exe', '');
  if FaltaLocalDB and not FileExists(ExpandConstant('{tmp}\SqlLocalDB.msi')) then
    PaginaDescarga.Add(URL_LOCALDB19, 'SqlLocalDB.msi', '');
  if Descargar then Exit;

  MsgBox('No fue posible descargar los componentes de Microsoft.' + #13#10#13#10 +
         'Verifique que el computador tenga internet y pulse «Instalar» de nuevo.',
         mbError, MB_OK);
  Result := False;
end;

{ Verifica que el motor quedó instalado; si no, informa con un mensaje claro }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if FaltaLocalDB then
      MsgBox('No fue posible instalar el motor de base de datos (SQL Server LocalDB).' + #13#10 +
             'Reinicie el computador y ejecute de nuevo este instalador.' + #13#10 +
             'Si el problema persiste, comuníquese con soporte.', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox('CaféCostos se desinstaló.' + #13#10#13#10 +
           'Su información y sus copias de seguridad se conservaron. Si vuelve a instalar el programa, ' +
           'todo aparecerá tal como lo dejó.', mbInformation, MB_OK);
end;
