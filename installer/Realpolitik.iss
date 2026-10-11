; Realpolitik installer: Living Nations for HUMANKIND (Inno Setup 6).
; Do not compile by hand: use _Modding\tools\make-release.ps1 -Version X.Y.Z. It assembles and checks the staging
; (allowlist and secret guard) and passes /DReleaseVersion, /DStagingDir and /DOutputFolder. This script only reads staging files.
;
; Rules (docs\prompt-instalador.md):
; - Installs into the game folder found in Steam or Epic (or picked by hand, validated by Humankind.exe).
; - Official BepInEx 5.4.23.5 only if missing; an existing 5.4.x stays; another one/6.x: warns and never swaps it silently.
; - Never touches BepInEx\config\*.cfg, BepInEx\config\credenciais, BepInEx\DiplomaciaIA or the saves.
; - No administrator rights on the default Steam path; if the folder is not writable, relaunches asking for permission.
; - Uninstalling removes only what it installed; BepInEx only goes if this installer put it there and no other plugin is left.
; - Player-facing text is in [CustomMessages] (5 languages); installed file names (LEIA-ME_*, licencas\, the BepInEx
;   marker) are data on the player's machine and keep their names.

#ifndef ReleaseVersion
  #define ReleaseVersion "0.0.0-dev"
#endif
#ifndef StagingDir
  #define StagingDir "..\dist\staging\" + ReleaseVersion
#endif
#ifndef OutputFolder
  #define OutputFolder "..\dist\" + ReleaseVersion
#endif
; Numbers-only version (Windows does not accept "-beta" in the .exe version resource).
#define Dash Pos("-", ReleaseVersion)
#if Dash > 0
  #define ReleaseVersionNum Copy(ReleaseVersion, 1, Dash - 1)
#else
  #define ReleaseVersionNum ReleaseVersion
#endif

#define AppName "Realpolitik: Living Nations for HUMANKIND"
#define SteamAppId "1124300"
#define GameVersionTested "1.31.4836"

[Setup]
AppId={{8C1E5B7A-4D2F-4E19-9A6B-2F7C3D1E0A55}
AppName={#AppName}
AppVersion={#ReleaseVersion}
AppVerName={#AppName} {#ReleaseVersion}
AppPublisher=Realpolitik
AppPublisherURL=https://github.com/lcaresia/realpolitik
AppSupportURL=https://github.com/lcaresia/realpolitik/issues
VersionInfoVersion={#ReleaseVersionNum}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={code:DefaultGameDir}
UsePreviousAppDir=yes
AppendDefaultDirName=no
DirExistsWarning=no
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
UninstallFilesDir={app}\BepInEx\Realpolitik
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\BepInEx\Realpolitik\realpolitik.ico
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
OutputDir={#OutputFolder}
OutputBaseFilename=Realpolitik_Setup_{#ReleaseVersion}
SetupIconFile={#StagingDir}\art\realpolitik.ico
WizardStyle=modern
WizardImageFile={#StagingDir}\art\grande-164.bmp,{#StagingDir}\art\grande-328.bmp
WizardSmallImageFile={#StagingDir}\art\pequena-55.bmp,{#StagingDir}\art\pequena-110.bmp
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
SetupLogging=yes
CloseApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Signing (optional): make-release.ps1 signs the .exe afterwards, if there is a certificate.

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"; LicenseFile: "{#StagingDir}\docs\TERMOS_en.txt"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; LicenseFile: "{#StagingDir}\docs\TERMOS_pt.txt"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"; LicenseFile: "{#StagingDir}\docs\TERMOS_es.txt"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "{#StagingDir}\docs\TERMOS_fr.txt"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "{#StagingDir}\docs\TERMOS_de.txt"

[CustomMessages]
en.TaskMoreEmpires=Up to 16 empires on any map size (MoreEmpires)
pt.TaskMoreEmpires=Até 16 impérios em qualquer tamanho de mapa (MoreEmpires)
es.TaskMoreEmpires=Hasta 16 imperios en cualquier tamaño de mapa (MoreEmpires)
fr.TaskMoreEmpires=Jusqu'à 16 empires sur toutes les tailles de carte (MoreEmpires)
de.TaskMoreEmpires=Bis zu 16 Imperien auf jeder Kartengröße (MoreEmpires)
en.TaskMoreEmpiresNote=Saves with more than 10 empires only open with this option installed.
pt.TaskMoreEmpiresNote=Saves com mais de 10 impérios só abrem com esta opção instalada.
es.TaskMoreEmpiresNote=Las partidas con más de 10 imperios solo se abren con esta opción instalada.
fr.TaskMoreEmpiresNote=Les sauvegardes de plus de 10 empires ne s'ouvrent qu'avec cette option installée.
de.TaskMoreEmpiresNote=Spielstände mit mehr als 10 Imperien öffnen sich nur mit dieser Option.
en.SelectGameDir=Select the HUMANKIND game folder (the one with Humankind.exe).
pt.SelectGameDir=Escolha a pasta do HUMANKIND (a que tem o Humankind.exe).
es.SelectGameDir=Elige la carpeta de HUMANKIND (la que tiene Humankind.exe).
fr.SelectGameDir=Choisissez le dossier de HUMANKIND (celui qui contient Humankind.exe).
de.SelectGameDir=Wähle den HUMANKIND-Ordner (der mit Humankind.exe).
en.NoGameExe=Humankind.exe was not found in this folder. Choose the HUMANKIND game folder.
pt.NoGameExe=O Humankind.exe não está nesta pasta. Escolha a pasta do HUMANKIND.
es.NoGameExe=Humankind.exe no está en esta carpeta. Elige la carpeta de HUMANKIND.
fr.NoGameExe=Humankind.exe est introuvable dans ce dossier. Choisissez le dossier de HUMANKIND.
de.NoGameExe=Humankind.exe wurde in diesem Ordner nicht gefunden. Wähle den HUMANKIND-Ordner.
en.XboxNotSupported=This looks like the Xbox / Microsoft Store version of HUMANKIND, which does not support mods. Realpolitik works with the Steam and Epic versions.
pt.XboxNotSupported=Esta parece ser a versão Xbox / Microsoft Store do HUMANKIND, que não aceita mods. O Realpolitik funciona nas versões da Steam e da Epic.
es.XboxNotSupported=Parece la versión Xbox / Microsoft Store de HUMANKIND, que no admite mods. Realpolitik funciona con las versiones de Steam y Epic.
fr.XboxNotSupported=Il semble s'agir de la version Xbox / Microsoft Store de HUMANKIND, qui n'accepte pas les mods. Realpolitik fonctionne avec les versions Steam et Epic.
de.XboxNotSupported=Das scheint die Xbox-/Microsoft-Store-Version von HUMANKIND zu sein, die keine Mods unterstützt. Realpolitik funktioniert mit der Steam- und der Epic-Version.
en.NeedAdmin=Windows asks for permission to write in this game folder. Setup will reopen and ask for administrator permission.
pt.NeedAdmin=O Windows pede permissão para gravar nesta pasta do jogo. O instalador vai reabrir e pedir permissão de administrador.
es.NeedAdmin=Windows pide permiso para escribir en esta carpeta del juego. El instalador se volverá a abrir y pedirá permiso de administrador.
fr.NeedAdmin=Windows demande une autorisation pour écrire dans ce dossier du jeu. Le programme d'installation va se relancer et demander l'autorisation d'administrateur.
de.NeedAdmin=Windows verlangt eine Berechtigung, um in diesen Spielordner zu schreiben. Das Setup startet neu und fragt nach Administratorrechten.
en.GameRunning=HUMANKIND is running. Close the game and click Retry.
pt.GameRunning=O HUMANKIND está aberto. Feche o jogo e clique em Repetir.
es.GameRunning=HUMANKIND está abierto. Cierra el juego y haz clic en Reintentar.
fr.GameRunning=HUMANKIND est ouvert. Fermez le jeu puis cliquez sur Réessayer.
de.GameRunning=HUMANKIND läuft. Schließe das Spiel und klicke auf Wiederholen.
en.OtherBepInEx=This game folder has a different BepInEx (%1). Realpolitik needs BepInEx 5.4 and Setup will not replace yours.%n%nContinue anyway? (The mod may not load until BepInEx 5.4 is installed.)
pt.OtherBepInEx=Esta pasta do jogo tem outro BepInEx (%1). O Realpolitik precisa do BepInEx 5.4 e o instalador não troca o seu.%n%nContinuar mesmo assim? (O mod pode não carregar até o BepInEx 5.4 ser instalado.)
es.OtherBepInEx=Esta carpeta del juego tiene otro BepInEx (%1). Realpolitik necesita BepInEx 5.4 y el instalador no reemplazará el tuyo.%n%n¿Continuar de todos modos? (El mod puede no cargar hasta instalar BepInEx 5.4.)
fr.OtherBepInEx=Ce dossier du jeu contient un autre BepInEx (%1). Realpolitik a besoin de BepInEx 5.4 et l'installation ne remplacera pas le vôtre.%n%nContinuer quand même ? (Le mod risque de ne pas se charger tant que BepInEx 5.4 n'est pas installé.)
de.OtherBepInEx=Dieser Spielordner enthält ein anderes BepInEx (%1). Realpolitik braucht BepInEx 5.4, und das Setup ersetzt deines nicht.%n%nTrotzdem fortfahren? (Die Mod lädt eventuell erst, wenn BepInEx 5.4 installiert ist.)
en.DoorstopOff=BepInEx is turned off in this game folder (doorstop_config.ini: enabled = false), so no mod loads. Turn it back on?
pt.DoorstopOff=O BepInEx está desligado nesta pasta do jogo (doorstop_config.ini: enabled = false), e nenhum mod carrega. Religar?
es.DoorstopOff=BepInEx está desactivado en esta carpeta del juego (doorstop_config.ini: enabled = false), así que ningún mod carga. ¿Volver a activarlo?
fr.DoorstopOff=BepInEx est désactivé dans ce dossier du jeu (doorstop_config.ini : enabled = false), aucun mod ne se charge. Le réactiver ?
de.DoorstopOff=BepInEx ist in diesem Spielordner ausgeschaltet (doorstop_config.ini: enabled = false), daher lädt keine Mod. Wieder einschalten?
en.ReadyGame=Game folder:
pt.ReadyGame=Pasta do jogo:
es.ReadyGame=Carpeta del juego:
fr.ReadyGame=Dossier du jeu :
de.ReadyGame=Spielordner:
en.ReadyUpdate=Update from %1 to %2
pt.ReadyUpdate=Atualizar de %1 para %2
es.ReadyUpdate=Actualizar de %1 a %2
fr.ReadyUpdate=Mise à jour de %1 vers %2
de.ReadyUpdate=Update von %1 auf %2
en.ReadyBepInExInstall=BepInEx 5.4.23.5 will be installed (needed to load mods).
pt.ReadyBepInExInstall=O BepInEx 5.4.23.5 será instalado (necessário para carregar mods).
es.ReadyBepInExInstall=Se instalará BepInEx 5.4.23.5 (necesario para cargar mods).
fr.ReadyBepInExInstall=BepInEx 5.4.23.5 sera installé (nécessaire pour charger les mods).
de.ReadyBepInExInstall=BepInEx 5.4.23.5 wird installiert (nötig, um Mods zu laden).
en.ReadyBepInExKeep=BepInEx %1 is already installed and will be kept.
pt.ReadyBepInExKeep=O BepInEx %1 já está instalado e será mantido.
es.ReadyBepInExKeep=BepInEx %1 ya está instalado y se mantendrá.
fr.ReadyBepInExKeep=BepInEx %1 est déjà installé et sera conservé.
de.ReadyBepInExKeep=BepInEx %1 ist bereits installiert und bleibt erhalten.
en.ReadyKeep=Your settings, keys and saves are kept.
pt.ReadyKeep=Suas configurações, chaves e saves são mantidos.
es.ReadyKeep=Tu configuración, tus claves y tus partidas se mantienen.
fr.ReadyKeep=Vos réglages, clés et sauvegardes sont conservés.
de.ReadyKeep=Deine Einstellungen, Schlüssel und Spielstände bleiben erhalten.
en.OpenGame=Open HUMANKIND now
pt.OpenGame=Abrir o HUMANKIND agora
es.OpenGame=Abrir HUMANKIND ahora
fr.OpenGame=Lancer HUMANKIND maintenant
de.OpenGame=HUMANKIND jetzt starten
en.OpenGuide=Read the quick guide
pt.OpenGuide=Ler o guia rápido
es.OpenGuide=Leer la guía rápida
fr.OpenGuide=Lire le guide rapide
de.OpenGuide=Kurzanleitung lesen
en.KeepSettings=Keep your Realpolitik settings and keys (AI providers) on this PC?%n%nYes: reinstalling later brings everything back.%nNo: deletes them.%n%nSaves are never deleted.
pt.KeepSettings=Manter as configurações e chaves do Realpolitik (provedores de IA) neste PC?%n%nSim: ao reinstalar, tudo volta.%nNão: apaga tudo.%n%nOs saves nunca são apagados.
es.KeepSettings=¿Conservar la configuración y las claves de Realpolitik (proveedores de IA) en este PC?%n%nSí: al reinstalar, todo vuelve.%nNo: se borran.%n%nLas partidas nunca se borran.
fr.KeepSettings=Conserver les réglages et clés de Realpolitik (fournisseurs d'IA) sur ce PC ?%n%nOui : tout revient si vous réinstallez.%nNon : ils sont supprimés.%n%nLes sauvegardes ne sont jamais supprimées.
de.KeepSettings=Realpolitik-Einstellungen und -Schlüssel (KI-Anbieter) auf diesem PC behalten?%n%nJa: Bei einer Neuinstallation ist alles wieder da.%nNein: Sie werden gelöscht.%n%nSpielstände werden nie gelöscht.

[Tasks]
Name: "moreempires"; Description: "{cm:TaskMoreEmpires}"; GroupDescription: "{cm:TaskMoreEmpiresNote}"

[Files]
; The mod
Source: "{#StagingDir}\payload\BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll"; DestDir: "{app}\BepInEx\plugins\CurrencyMod"; Flags: ignoreversion
Source: "{#StagingDir}\payload\BepInEx\CurrencyModCore\CurrencyMod.dll"; DestDir: "{app}\BepInEx\CurrencyModCore"; Flags: ignoreversion
Source: "{#StagingDir}\payload\BepInEx\plugins\MoreEmpires\MoreEmpires.dll"; DestDir: "{app}\BepInEx\plugins\MoreEmpires"; Tasks: moreempires; Flags: ignoreversion
; Readme, terms, licenses and the uninstaller icon
Source: "{#StagingDir}\docs\*"; DestDir: "{app}\BepInEx\Realpolitik"; Flags: ignoreversion
Source: "{#StagingDir}\licencas\*"; DestDir: "{app}\BepInEx\Realpolitik\licencas"; Flags: ignoreversion
Source: "{#StagingDir}\art\realpolitik.ico"; DestDir: "{app}\BepInEx\Realpolitik"; Flags: ignoreversion
; Official BepInEx, only if missing. Never removed by the automatic uninstaller: the code decides (see RemoveBepInEx).
Source: "{#StagingDir}\bepinex\BepInEx\core\*"; DestDir: "{app}\BepInEx\core"; Check: NeedBepInExCore; Flags: ignoreversion uninsneveruninstall
Source: "{#StagingDir}\bepinex\winhttp.dll"; DestDir: "{app}"; Check: NeedLoader; Flags: ignoreversion uninsneveruninstall
Source: "{#StagingDir}\bepinex\doorstop_config.ini"; DestDir: "{app}"; Check: NeedLoader; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#StagingDir}\bepinex\.doorstop_version"; DestDir: "{app}"; Check: NeedLoader; Flags: ignoreversion uninsneveruninstall

[Dirs]
Name: "{app}\BepInEx\plugins"; Flags: uninsneveruninstall

[Run]
Filename: "steam://rungameid/{#SteamAppId}"; Description: "{cm:OpenGame}"; Flags: postinstall shellexec nowait skipifsilent; Check: IsSteamGame
Filename: "{app}\BepInEx\Realpolitik\LEIA-ME_{code:LangCode}.txt"; Description: "{cm:OpenGuide}"; Flags: postinstall shellexec nowait skipifsilent

[Code]
const
  SteamAppId = '{#SteamAppId}';
  // File name already present on players' machines from earlier versions: data, keep it as is.
  Marker = 'bepinex-instalado-pelo-realpolitik.txt';

var
  GameStore: String;        // steam, epic, manual
  BepInExState: String;     // missing, keep, other
  BepInExFound: String;     // version found
  LoaderMissing: Boolean;
  DoorstopReenable: Boolean;
  PreviousVersion: String;
  Relaunched: Boolean;
  KeepSettings: Boolean;

// ---------------- utilities ----------------

function Unescape(S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\\', '\', True);
  StringChangeEx(Result, '/', '\', True);
end;

// Quoted value after a key: "key" "value" (vdf/acf) or "key": "value" (Epic json).
function QuotedValueAfter(Line, Key: String): String;
var
  P: Integer;
  Rest: String;
begin
  Result := '';
  P := Pos('"' + Key + '"', Line);
  if P = 0 then Exit;
  Rest := Copy(Line, P + Length(Key) + 2, MaxInt);
  P := Pos('"', Rest);
  if P = 0 then Exit;
  Rest := Copy(Rest, P + 1, MaxInt);
  P := Pos('"', Rest);
  if P = 0 then Exit;
  Result := Copy(Rest, 1, P - 1);
end;

function IsGameDir(Dir: String): Boolean;
begin
  Result := (Dir <> '') and FileExists(AddBackslash(Dir) + 'Humankind.exe');
end;

function IsGameRunning: Boolean;
var
  Locator, Service, Items: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Items := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Humankind.exe''');
    Result := Items.Count > 0;
  except
    Log('Could not check whether the game is running: ' + GetExceptionMessage);
  end;
end;

// Asks to close the game until it is closed (or cancelled). Silent mode: fails right away.
function WaitGameClosed: Boolean;
begin
  Result := True;
  while IsGameRunning do
  begin
    Log('HUMANKIND is running.');
    if UninstallSilent or WizardSilent then
    begin
      Result := False;
      Exit;
    end;
    if MsgBox(CustomMessage('GameRunning'), mbError, MB_RETRYCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

// ---------------- finding the game ----------------

function SteamGameDir: String;
var
  SteamRoot, Lib, Acf, InstallDir, Dir: String;
  Libs, Lines: TArrayOfString;
  I, J, N: Integer;
  V: String;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', SteamRoot) then
    if not RegQueryStringValue(HKLM32, 'SOFTWARE\Valve\Steam', 'InstallPath', SteamRoot) then
      RegQueryStringValue(HKLM64, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', SteamRoot);
  SteamRoot := Unescape(SteamRoot);
  if SteamRoot = '' then Exit;
  N := 1;
  SetArrayLength(Libs, 1);
  Libs[0] := SteamRoot;
  if LoadStringsFromFile(AddBackslash(SteamRoot) + 'steamapps\libraryfolders.vdf', Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      V := Unescape(QuotedValueAfter(Lines[I], 'path'));
      if V <> '' then
      begin
        SetArrayLength(Libs, N + 1);
        Libs[N] := V;
        N := N + 1;
      end;
    end;
  for I := 0 to N - 1 do
  begin
    Lib := AddBackslash(Libs[I]);
    Acf := Lib + 'steamapps\appmanifest_' + SteamAppId + '.acf';
    if FileExists(Acf) and LoadStringsFromFile(Acf, Lines) then
    begin
      InstallDir := 'Humankind';
      for J := 0 to GetArrayLength(Lines) - 1 do
      begin
        V := QuotedValueAfter(Lines[J], 'installdir');
        if V <> '' then InstallDir := V;
      end;
      Dir := Lib + 'steamapps\common\' + InstallDir;
      if IsGameDir(Dir) then
      begin
        Log('Steam: game in ' + Dir);
        Result := Dir;
        Exit;
      end;
    end;
  end;
end;

function EpicGameDir: String;
var
  Manifests, Dir, Line, Location: String;
  FindRec: TFindRec;
  Lines: TArrayOfString;
  I: Integer;
  IsHumankind: Boolean;
begin
  Result := '';
  Manifests := ExpandConstant('{commonappdata}\Epic\EpicGamesLauncher\Data\Manifests\');
  if not FindFirst(Manifests + '*.item', FindRec) then Exit;
  try
    repeat
      if LoadStringsFromFile(Manifests + FindRec.Name, Lines) then
      begin
        IsHumankind := False;
        Location := '';
        for I := 0 to GetArrayLength(Lines) - 1 do
        begin
          Line := Lines[I];
          if Pos('HUMANKIND', Uppercase(QuotedValueAfter(Line, 'DisplayName'))) > 0 then IsHumankind := True;
          if QuotedValueAfter(Line, 'InstallLocation') <> '' then Location := Unescape(QuotedValueAfter(Line, 'InstallLocation'));
        end;
        Dir := Location;
        if IsHumankind and IsGameDir(Dir) then
        begin
          Log('Epic: game in ' + Dir);
          Result := Dir;
          Exit;
        end;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function DefaultGameDir(Param: String): String;
begin
  Result := SteamGameDir;
  if Result = '' then Result := EpicGameDir;
  if Result = '' then Result := ExpandConstant('{autopf32}\Steam\steamapps\common\Humankind');
end;

function IsXboxDir(Dir: String): Boolean;
var
  U: String;
begin
  U := Uppercase(Dir);
  Result := (Pos('\WINDOWSAPPS\', U) > 0) or (Pos('\XBOXGAMES\', U) > 0) or (Pos('\MODIFIABLEWINDOWSAPPS\', U) > 0);
end;

function DirWritable(Dir: String): Boolean;
var
  Probe: String;
begin
  Probe := AddBackslash(Dir) + 'realpolitik-write-test.tmp';
  Result := SaveStringToFile(Probe, 'x', False);
  if Result then DeleteFile(Probe);
end;

function IsSteamGame: Boolean;
begin
  Result := GameStore = 'steam';
end;

function LangCode(Param: String): String;
begin
  Result := ActiveLanguage;
end;

// ---------------- BepInEx ----------------

procedure InspectBepInEx(Dir: String);
var
  Core, Ini: String;
  Lines: TArrayOfString;
  I: Integer;
  L: String;
begin
  Dir := AddBackslash(Dir);
  Core := Dir + 'BepInEx\core\';
  BepInExFound := '';
  if FileExists(Core + 'BepInEx.dll') then
  begin
    GetVersionNumbersString(Core + 'BepInEx.dll', BepInExFound);
    if Pos('5.4.', BepInExFound) = 1 then BepInExState := 'keep' else BepInExState := 'other';
  end
  else if FileExists(Core + 'BepInEx.Core.dll') or FileExists(Core + 'BepInEx.Unity.Mono.dll') then
  begin
    BepInExState := 'other';
    GetVersionNumbersString(Core + 'BepInEx.Core.dll', BepInExFound);
    if BepInExFound = '' then BepInExFound := '6.x';
  end
  else
    BepInExState := 'missing';
  LoaderMissing := not FileExists(Dir + 'winhttp.dll');
  DoorstopReenable := False;
  Ini := Dir + 'doorstop_config.ini';
  if (BepInExState <> 'missing') and FileExists(Ini) and LoadStringsFromFile(Ini, Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      L := Lowercase(Lines[I]);
      StringChangeEx(L, ' ', '', True);
      if (Pos('enabled=false', L) = 1) then DoorstopReenable := True;
    end;
  Log(Format('BepInEx: state=%s version=%s winhttp missing=%d doorstop off=%d', [BepInExState, BepInExFound, Ord(LoaderMissing), Ord(DoorstopReenable)]));
end;

function NeedBepInExCore: Boolean;
begin
  Result := BepInExState = 'missing';
end;

function NeedLoader: Boolean;
begin
  Result := (BepInExState = 'missing') or ((BepInExState = 'keep') and LoaderMissing);
end;

procedure ReenableDoorstop(Dir: String);
var
  Ini, L: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  Ini := AddBackslash(Dir) + 'doorstop_config.ini';
  if not LoadStringsFromFile(Ini, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    L := Lowercase(Lines[I]);
    StringChangeEx(L, ' ', '', True);
    if Pos('enabled=false', L) = 1 then Lines[I] := 'enabled = true';
  end;
  if SaveStringsToFile(Ini, Lines, False) then Log('doorstop_config.ini: BepInEx turned back on.');
end;

// ---------------- wizard ----------------

function InitializeSetup: Boolean;
begin
  Result := True;
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C1E5B7A-4D2F-4E19-9A6B-2F7C3D1E0A55}_is1', 'DisplayVersion', PreviousVersion) then
    RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C1E5B7A-4D2F-4E19-9A6B-2F7C3D1E0A55}_is1', 'DisplayVersion', PreviousVersion);
  Log('Previous version: ' + PreviousVersion + ' - new: {#ReleaseVersion} - admin: ' + IntToStr(Ord(IsAdminInstallMode)));
end;

procedure InitializeWizard;
begin
  WizardForm.SelectDirLabel.Caption := CustomMessage('SelectGameDir');
end;

function RelaunchElevated(Dir: String): Boolean;
var
  Code: Integer;
  Params: String;
begin
  Params := '/ALLUSERS /LANG=' + ActiveLanguage + ' /DIR="' + Dir + '"';
  Result := ShellExec('runas', ExpandConstant('{srcexe}'), Params, '', SW_SHOW, ewNoWait, Code);
  Log('Relaunching as administrator: ' + IntToStr(Ord(Result)));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir, GameVer: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then Exit;
  Dir := RemoveBackslashUnlessRoot(WizardDirValue);
  if IsXboxDir(Dir) then
  begin
    MsgBox(CustomMessage('XboxNotSupported'), mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if not IsGameDir(Dir) then
  begin
    MsgBox(CustomMessage('NoGameExe'), mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if not IsAdminInstallMode and not DirWritable(Dir) then
  begin
    if WizardSilent then
    begin
      Log('Folder not writable and silent install without /ALLUSERS.');
      Result := False;
      Exit;
    end;
    MsgBox(CustomMessage('NeedAdmin'), mbInformation, MB_OK);
    if RelaunchElevated(Dir) then
    begin
      Relaunched := True;
      WizardForm.Close;
    end;
    Result := False;
    Exit;
  end;
  if CompareText(SteamGameDir, Dir) = 0 then GameStore := 'steam'
  else if CompareText(EpicGameDir, Dir) = 0 then GameStore := 'epic'
  else GameStore := 'manual';
  Log('Game folder: ' + Dir + ' - store: ' + GameStore);
  Dir := AddBackslash(Dir);
  // Game version only in the log (no warning to the player).
  GameVer := '';
  GetVersionNumbersString(Dir + 'Humankind.exe', GameVer);
  Log('Humankind.exe: ' + GameVer + ' (reference version: {#GameVersionTested})');
  InspectBepInEx(Dir);
  if BepInExState = 'other' then
    if WizardSilent or (MsgBox(FmtMessage(CustomMessage('OtherBepInEx'), [BepInExFound]), mbConfirmation, MB_YESNO) <> IDYES) then
    begin
      Result := WizardSilent; // silent mode carries on without swapping BepInEx (recorded in the log)
      Exit;
    end;
  if DoorstopReenable and not WizardSilent then
    DoorstopReenable := MsgBox(CustomMessage('DoorstopOff'), mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if Relaunched then Confirm := False;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := CustomMessage('ReadyGame') + NewLine + Space + WizardDirValue + NewLine + NewLine;
  if (PreviousVersion <> '') then
    Result := Result + FmtMessage(CustomMessage('ReadyUpdate'), [PreviousVersion, '{#ReleaseVersion}']) + NewLine;
  if BepInExState = 'missing' then
    Result := Result + CustomMessage('ReadyBepInExInstall') + NewLine
  else
    Result := Result + FmtMessage(CustomMessage('ReadyBepInExKeep'), [BepInExFound]) + NewLine;
  if MemoTasksInfo <> '' then Result := Result + NewLine + MemoTasksInfo + NewLine;
  Result := Result + NewLine + CustomMessage('ReadyKeep');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not WaitGameClosed then Result := CustomMessage('GameRunning');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Dir, Old: String;
begin
  Dir := AddBackslash(WizardDirValue);
  if CurStep = ssInstall then
  begin
    // MoreEmpires unticked: the DLL goes (its configuration stays).
    Old := Dir + 'BepInEx\plugins\MoreEmpires\MoreEmpires.dll';
    if not WizardIsTaskSelected('moreempires') and FileExists(Old) then
      if DeleteFile(Old) then Log('MoreEmpires unticked: DLL removed.');
  end;
  if CurStep = ssPostInstall then
  begin
    if BepInExState = 'missing' then
    begin
      ForceDirectories(Dir + 'BepInEx\Realpolitik');
      SaveStringToFile(Dir + 'BepInEx\Realpolitik\' + Marker, 'BepInEx 5.4.23.5 installed by Realpolitik {#ReleaseVersion} on ' + GetDateTimeString('yyyy-mm-dd hh:nn', '-', ':') + #13#10, False);
      Log('BepInEx installed (marked to be removed on uninstall, if no other plugin is left).');
    end;
    if DoorstopReenable then ReenableDoorstop(Dir);
  end;
end;

// ---------------- uninstall ----------------

function InitializeUninstall: Boolean;
begin
  Result := WaitGameClosed;
  // Silent mode keeps everything, unless /DELETEALL is passed (support and tests); /APAGARTUDO is the old spelling, still accepted.
  KeepSettings := (Pos('/DELETEALL', Uppercase(GetCmdTail)) = 0) and (Pos('/APAGARTUDO', Uppercase(GetCmdTail)) = 0);
  if Result and not UninstallSilent then
    KeepSettings := MsgBox(CustomMessage('KeepSettings'), mbConfirmation, MB_YESNO) = IDYES;
  Log('Uninstall - keep settings: ' + IntToStr(Ord(KeepSettings)));
end;

function OtherPluginsLeft(Dir: String): Boolean;
var
  FindRec: TFindRec;
  Plugins: String;
begin
  Result := False;
  Plugins := Dir + 'BepInEx\plugins\';
  if FindFirst(Plugins + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Log('Left in plugins: ' + FindRec.Name);
        Result := True;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure RemoveBepInEx(Dir: String);
begin
  DelTree(Dir + 'BepInEx\core', True, True, True);
  DelTree(Dir + 'BepInEx\cache', True, True, True);
  DelTree(Dir + 'BepInEx\patchers', True, True, True);
  DeleteFile(Dir + 'BepInEx\LogOutput.log');
  DeleteFile(Dir + 'BepInEx\config\BepInEx.cfg');
  DeleteFile(Dir + 'winhttp.dll');
  DeleteFile(Dir + 'doorstop_config.ini');
  DeleteFile(Dir + '.doorstop_version');
  RemoveDir(Dir + 'BepInEx\plugins');
  RemoveDir(Dir + 'BepInEx\config');
  RemoveDir(Dir + 'BepInEx');
  Log('BepInEx removed (it was installed by Realpolitik and no other plugin is left).');
end;

procedure CleanupAfterExit(Dir: String);
var
  Code: Integer;
  Cmd: String;
begin
  Cmd := '/C ping 127.0.0.1 -n 5 > nul';
  Cmd := Cmd + ' & rd "' + Dir + 'BepInEx\Realpolitik\licencas"';
  Cmd := Cmd + ' & rd "' + Dir + 'BepInEx\Realpolitik"';
  Cmd := Cmd + ' & rd "' + Dir + 'BepInEx\plugins"';
  Cmd := Cmd + ' & rd "' + Dir + 'BepInEx\config"';
  Cmd := Cmd + ' & rd "' + Dir + 'BepInEx"';
  if Exec(ExpandConstant('{cmd}'), Cmd, '', SW_HIDE, ewNoWait, Code) then
    Log('Cleanup of the empty folders scheduled.');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Dir := AddBackslash(ExpandConstant('{app}'));
  // Folders that belonged only to the mod (the DLLs were already removed by the uninstaller).
  RemoveDir(Dir + 'BepInEx\plugins\CurrencyMod');
  RemoveDir(Dir + 'BepInEx\plugins\MoreEmpires');
  DeleteFile(Dir + 'BepInEx\CurrencyModCore\result.txt');
  if not KeepSettings then
  begin
    DeleteFile(Dir + 'BepInEx\config\lucas.humankind.currency.cfg');
    DeleteFile(Dir + 'BepInEx\config\lucas.humankind.moreempires.cfg');
    DeleteFile(Dir + 'BepInEx\config\deepseek.key');
    DelTree(Dir + 'BepInEx\config\credenciais', True, True, True);
    DelTree(Dir + 'BepInEx\DiplomaciaIA', True, True, True);
    DelTree(Dir + 'BepInEx\MoreEmpires', True, True, True);
    DelTree(Dir + 'BepInEx\CurrencyModCore', True, True, True);
    Log('Realpolitik settings and keys deleted.');
  end;
  RemoveDir(Dir + 'BepInEx\CurrencyModCore');
  if FileExists(Dir + 'BepInEx\Realpolitik\' + Marker) then
  begin
    DeleteFile(Dir + 'BepInEx\Realpolitik\' + Marker);
    // Saved settings (BepInEx\config) stay even without BepInEx: the folder only goes if it is empty.
    if not OtherPluginsLeft(Dir) then
      RemoveBepInEx(Dir)
    else
      Log('BepInEx kept (another plugin is left in the folder).');
  end
  else
    Log('BepInEx kept (it was not installed by Realpolitik).');
  RemoveDir(Dir + 'BepInEx\Realpolitik\licencas');
  RemoveDir(Dir + 'BepInEx\Realpolitik');
  // The uninstaller's own files (BepInEx\Realpolitik) only go after this step, so the folders above
  // are not empty yet. Schedules the removal right after: "rd" without /s only deletes an EMPTY folder (never content).
  CleanupAfterExit(Dir);
end;
