; Instalador do Realpolitik: Living Nations for HUMANKIND (Inno Setup 6).
; Não compile à mão: use _Modding\tools\gerar-release.ps1 -Versao X.Y.Z. Ele monta e confere o staging (lista branca e
; trava de segredos) e passa /DVersao, /DStaging e /DSaida. Este script só lê arquivos do staging.
;
; Regras (docs\prompt-instalador.md):
; - Instala na pasta do jogo achada na Steam ou na Epic (ou escolhida à mão, validada pelo Humankind.exe).
; - BepInEx 5.4.23.5 oficial só se faltar; 5.4.x existente fica; outro/6.x: avisa e nunca troca em silêncio.
; - Nunca toca em BepInEx\config\*.cfg, BepInEx\config\credenciais, BepInEx\DiplomaciaIA nem nos saves.
; - Sem administrador na Steam padrão; se a pasta não for gravável, reabre pedindo permissão.
; - Desinstalar remove só o que instalou; o BepInEx só sai se foi este instalador que o pôs e não sobrou outro plugin.

#ifndef Versao
  #define Versao "0.0.0-dev"
#endif
#ifndef Staging
  #define Staging "..\dist\staging\" + Versao
#endif
#ifndef Saida
  #define Saida "..\dist\" + Versao
#endif
; Versão só com números (o Windows não aceita "-beta" no recurso de versão do .exe).
#define Dash Pos("-", Versao)
#if Dash > 0
  #define VersaoNum Copy(Versao, 1, Dash - 1)
#else
  #define VersaoNum Versao
#endif

#define AppName "Realpolitik: Living Nations for HUMANKIND"
#define SteamAppId "1124300"
#define GameVersionTested "1.31.4836"

[Setup]
AppId={{8C1E5B7A-4D2F-4E19-9A6B-2F7C3D1E0A55}
AppName={#AppName}
AppVersion={#Versao}
AppVerName={#AppName} {#Versao}
AppPublisher=Realpolitik
AppPublisherURL=https://realpolitik-living-nations.pages.dev/
AppSupportURL=https://realpolitik-living-nations.pages.dev/
VersionInfoVersion={#VersaoNum}
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
OutputDir={#Saida}
OutputBaseFilename=Realpolitik_Setup_{#Versao}
SetupIconFile={#Staging}\art\realpolitik.ico
WizardStyle=modern
WizardImageFile={#Staging}\art\grande-164.bmp,{#Staging}\art\grande-328.bmp
WizardSmallImageFile={#Staging}\art\pequena-55.bmp,{#Staging}\art\pequena-110.bmp
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
SetupLogging=yes
CloseApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Assinatura (opcional): o gerar-release.ps1 assina o .exe depois, se houver certificado.

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"; LicenseFile: "{#Staging}\docs\TERMOS_en.txt"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; LicenseFile: "{#Staging}\docs\TERMOS_pt.txt"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"; LicenseFile: "{#Staging}\docs\TERMOS_es.txt"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "{#Staging}\docs\TERMOS_fr.txt"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "{#Staging}\docs\TERMOS_de.txt"

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
en.OpenGuide=Read the quick guide (activate the license in Realpolitik -> License)
pt.OpenGuide=Ler o guia rápido (ative a licença em Realpolitik -> Licença)
es.OpenGuide=Leer la guía rápida (activa la licencia en Realpolitik -> Licencia)
fr.OpenGuide=Lire le guide rapide (activez la licence dans Realpolitik -> Licence)
de.OpenGuide=Kurzanleitung lesen (Lizenz unter Realpolitik -> Lizenz aktivieren)
en.KeepSettings=Keep your Realpolitik settings and keys (AI providers and license) on this PC?%n%nYes: reinstalling later brings everything back.%nNo: deletes them (if you are moving to another PC, release this PC in the game first).%n%nSaves are never deleted.
pt.KeepSettings=Manter as configurações e chaves do Realpolitik (provedores de IA e licença) neste PC?%n%nSim: ao reinstalar, tudo volta.%nNão: apaga tudo (se for trocar de PC, libere este PC no jogo antes).%n%nOs saves nunca são apagados.
es.KeepSettings=¿Conservar la configuración y las claves de Realpolitik (proveedores de IA y licencia) en este PC?%n%nSí: al reinstalar, todo vuelve.%nNo: se borran (si vas a cambiar de PC, libera este PC en el juego antes).%n%nLas partidas nunca se borran.
fr.KeepSettings=Conserver les réglages et clés de Realpolitik (fournisseurs d'IA et licence) sur ce PC ?%n%nOui : tout revient si vous réinstallez.%nNon : ils sont supprimés (si vous changez de PC, libérez d'abord ce PC dans le jeu).%n%nLes sauvegardes ne sont jamais supprimées.
de.KeepSettings=Realpolitik-Einstellungen und -Schlüssel (KI-Anbieter und Lizenz) auf diesem PC behalten?%n%nJa: Bei einer Neuinstallation ist alles wieder da.%nNein: Sie werden gelöscht (beim PC-Wechsel zuerst diesen PC im Spiel freigeben).%n%nSpielstände werden nie gelöscht.

[Tasks]
Name: "moreempires"; Description: "{cm:TaskMoreEmpires}"; GroupDescription: "{cm:TaskMoreEmpiresNote}"

[Files]
; O mod
Source: "{#Staging}\payload\BepInEx\plugins\CurrencyMod\CurrencyMod.Loader.dll"; DestDir: "{app}\BepInEx\plugins\CurrencyMod"; Flags: ignoreversion
Source: "{#Staging}\payload\BepInEx\CurrencyModCore\CurrencyMod.dll"; DestDir: "{app}\BepInEx\CurrencyModCore"; Flags: ignoreversion
Source: "{#Staging}\payload\BepInEx\plugins\MoreEmpires\MoreEmpires.dll"; DestDir: "{app}\BepInEx\plugins\MoreEmpires"; Tasks: moreempires; Flags: ignoreversion
; Leia-me, termos, licenças e ícone do desinstalador
Source: "{#Staging}\docs\*"; DestDir: "{app}\BepInEx\Realpolitik"; Flags: ignoreversion
Source: "{#Staging}\licencas\*"; DestDir: "{app}\BepInEx\Realpolitik\licencas"; Flags: ignoreversion
Source: "{#Staging}\art\realpolitik.ico"; DestDir: "{app}\BepInEx\Realpolitik"; Flags: ignoreversion
; BepInEx oficial, só se faltar. Nunca é removido pelo desinstalador automático: o código decide (ver RemoveBepInEx).
Source: "{#Staging}\bepinex\BepInEx\core\*"; DestDir: "{app}\BepInEx\core"; Check: NeedBepInExCore; Flags: ignoreversion uninsneveruninstall
Source: "{#Staging}\bepinex\winhttp.dll"; DestDir: "{app}"; Check: NeedLoader; Flags: ignoreversion uninsneveruninstall
Source: "{#Staging}\bepinex\doorstop_config.ini"; DestDir: "{app}"; Check: NeedLoader; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#Staging}\bepinex\.doorstop_version"; DestDir: "{app}"; Check: NeedLoader; Flags: ignoreversion uninsneveruninstall

[Dirs]
Name: "{app}\BepInEx\plugins"; Flags: uninsneveruninstall

[Run]
Filename: "steam://rungameid/{#SteamAppId}"; Description: "{cm:OpenGame}"; Flags: postinstall shellexec nowait skipifsilent; Check: IsSteamGame
Filename: "{app}\BepInEx\Realpolitik\LEIA-ME_{code:LangCode}.txt"; Description: "{cm:OpenGuide}"; Flags: postinstall shellexec nowait skipifsilent

[Code]
const
  SteamAppId = '{#SteamAppId}';
  Marker = 'bepinex-instalado-pelo-realpolitik.txt';

var
  GameStore: String;        // steam, epic, manual
  BepInExState: String;     // missing, keep, other
  BepInExFound: String;     // versão encontrada
  LoaderMissing: Boolean;
  DoorstopReenable: Boolean;
  PreviousVersion: String;
  Relaunched: Boolean;
  KeepSettings: Boolean;

// ---------------- utilidades ----------------

function Unescape(S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\\', '\', True);
  StringChangeEx(Result, '/', '\', True);
end;

// Valor entre aspas depois de uma chave: "chave" "valor" (vdf/acf) ou "chave": "valor" (json da Epic).
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
    Log('Não consegui conferir se o jogo está aberto: ' + GetExceptionMessage);
  end;
end;

// Pede para fechar o jogo até fechar (ou cancelar). Silencioso: falha direto.
function WaitGameClosed: Boolean;
begin
  Result := True;
  while IsGameRunning do
  begin
    Log('HUMANKIND está aberto.');
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

// ---------------- achar o jogo ----------------

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
        Log('Steam: jogo em ' + Dir);
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
          Log('Epic: jogo em ' + Dir);
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
  Probe := AddBackslash(Dir) + 'realpolitik-teste-escrita.tmp';
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
  Log(Format('BepInEx: estado=%s versão=%s winhttp ausente=%d doorstop desligado=%d', [BepInExState, BepInExFound, Ord(LoaderMissing), Ord(DoorstopReenable)]));
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
  if SaveStringsToFile(Ini, Lines, False) then Log('doorstop_config.ini: BepInEx religado.');
end;

// ---------------- assistente ----------------

function InitializeSetup: Boolean;
begin
  Result := True;
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C1E5B7A-4D2F-4E19-9A6B-2F7C3D1E0A55}_is1', 'DisplayVersion', PreviousVersion) then
    RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C1E5B7A-4D2F-4E19-9A6B-2F7C3D1E0A55}_is1', 'DisplayVersion', PreviousVersion);
  Log('Versão anterior: ' + PreviousVersion + ' · nova: {#Versao} · admin: ' + IntToStr(Ord(IsAdminInstallMode)));
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
  Log('Reabrindo como administrador: ' + IntToStr(Ord(Result)));
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
      Log('Pasta sem permissão de escrita e instalação silenciosa sem /ALLUSERS.');
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
  Log('Pasta do jogo: ' + Dir + ' · loja: ' + GameStore);
  Dir := AddBackslash(Dir);
  // Versão do jogo só no log (sem aviso ao comprador).
  GameVer := '';
  GetVersionNumbersString(Dir + 'Humankind.exe', GameVer);
  Log('Humankind.exe: ' + GameVer + ' (versão de referência: {#GameVersionTested})');
  InspectBepInEx(Dir);
  if BepInExState = 'other' then
    if WizardSilent or (MsgBox(FmtMessage(CustomMessage('OtherBepInEx'), [BepInExFound]), mbConfirmation, MB_YESNO) <> IDYES) then
    begin
      Result := WizardSilent; // silencioso segue sem trocar o BepInEx (registrado no log)
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
    Result := Result + FmtMessage(CustomMessage('ReadyUpdate'), [PreviousVersion, '{#Versao}']) + NewLine;
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
    // MoreEmpires desmarcado: sai a DLL (a configuração dele fica).
    Old := Dir + 'BepInEx\plugins\MoreEmpires\MoreEmpires.dll';
    if not WizardIsTaskSelected('moreempires') and FileExists(Old) then
      if DeleteFile(Old) then Log('MoreEmpires desmarcado: DLL removida.');
  end;
  if CurStep = ssPostInstall then
  begin
    if BepInExState = 'missing' then
    begin
      ForceDirectories(Dir + 'BepInEx\Realpolitik');
      SaveStringToFile(Dir + 'BepInEx\Realpolitik\' + Marker, 'BepInEx 5.4.23.5 instalado pelo Realpolitik {#Versao} em ' + GetDateTimeString('yyyy-mm-dd hh:nn', '-', ':') + #13#10, False);
      Log('BepInEx instalado (marcado para sair junto na desinstalação, se não sobrar outro plugin).');
    end;
    if DoorstopReenable then ReenableDoorstop(Dir);
  end;
end;

// ---------------- desinstalar ----------------

function InitializeUninstall: Boolean;
begin
  Result := WaitGameClosed;
  // Silencioso mantém tudo, a não ser com /APAGARTUDO (suporte e testes).
  KeepSettings := Pos('/APAGARTUDO', Uppercase(GetCmdTail)) = 0;
  if Result and not UninstallSilent then
    KeepSettings := MsgBox(CustomMessage('KeepSettings'), mbConfirmation, MB_YESNO) = IDYES;
  Log('Desinstalar · manter configurações: ' + IntToStr(Ord(KeepSettings)));
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
        Log('Sobrou em plugins: ' + FindRec.Name);
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
  Log('BepInEx removido (tinha sido instalado pelo Realpolitik e não sobrou outro plugin).');
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
    Log('Limpeza das pastas vazias agendada.');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Dir := AddBackslash(ExpandConstant('{app}'));
  // Pastas que eram só do mod (as DLLs já saíram pelo desinstalador).
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
    Log('Configurações e chaves do Realpolitik apagadas.');
  end;
  RemoveDir(Dir + 'BepInEx\CurrencyModCore');
  if FileExists(Dir + 'BepInEx\Realpolitik\' + Marker) then
  begin
    DeleteFile(Dir + 'BepInEx\Realpolitik\' + Marker);
    // As configurações guardadas (BepInEx\config) ficam mesmo sem o BepInEx: a pasta só some se estiver vazia.
    if not OtherPluginsLeft(Dir) then
      RemoveBepInEx(Dir)
    else
      Log('BepInEx mantido (sobrou outro plugin na pasta).');
  end
  else
    Log('BepInEx mantido (não foi instalado pelo Realpolitik).');
  RemoveDir(Dir + 'BepInEx\Realpolitik\licencas');
  RemoveDir(Dir + 'BepInEx\Realpolitik');
  // Os arquivos do próprio desinstalador (BepInEx\Realpolitik) só saem depois deste passo, então as pastas de cima
  // ainda não estão vazias. Agenda a remoção para logo depois: "rd" sem /s só apaga pasta VAZIA (nunca conteúdo).
  CleanupAfterExit(Dir);
end;
