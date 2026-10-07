# Confere a DLL ofuscada contra a original (sem abrir o jogo). Falha se algo que precisa manter o nome mudou:
# patches do Harmony (nome e parâmetros), ModEntry, mensagens da Unity, campos dos tipos salvos em JSON, P/Invoke.
# Também confere que os textos (prompts) não aparecem em claro.
# Uso: powershell -File conferir-ofuscacao.ps1 -Original <dll> -Ofuscada <dll> -Cecil <Mono.Cecil.dll>
param([Parameter(Mandatory = $true)][string]$Original, [Parameter(Mandatory = $true)][string]$Ofuscada, [Parameter(Mandatory = $true)][string]$Cecil)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Cecil
$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Original)
$b = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Ofuscada)
$problems = New-Object System.Collections.Generic.List[string]
function AllTypes($asm) { $asm.MainModule.GetTypes() }
$bTypes = @{}; foreach ($t in AllTypes $b) { $bTypes[$t.FullName] = $t }
$bAll = @(AllTypes $b)

function Sig($m) { ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', ' }

# 1. Patches do Harmony: cada método de convenção tem que existir na DLL ofuscada com os mesmos parâmetros.
$conv = '^(Prefix|Postfix|Finalizer|Transpiler|TargetMethod|TargetMethods|Prepare)$'
$patchCount = 0
foreach ($t in AllTypes $a) {
    foreach ($m in $t.Methods | Where-Object { $_.Name -match $conv }) {
        $patchCount++
        $sig = Sig $m
        $found = $bAll | ForEach-Object { $_.Methods } | Where-Object { $_.Name -eq $m.Name -and (Sig $_) -eq $sig -and $_.Body.Instructions.Count -gt 0 -and $_.IsStatic -eq $m.IsStatic }
        if (-not $found) { $problems.Add("patch $($t.FullName).$($m.Name)($sig) sumiu ou mudou os parâmetros") }
    }
}
# Cada tipo com [HarmonyPatch] continua com o atributo e com os mesmos métodos de convenção.
$harmonyA = @(AllTypes $a | Where-Object { $_.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'HarmonyPatch' } })
$harmonyB = @($bAll | Where-Object { $_.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'HarmonyPatch' } })
if ($harmonyA.Count -ne $harmonyB.Count) { $problems.Add("classes com [HarmonyPatch]: $($harmonyA.Count) antes, $($harmonyB.Count) depois") }

# 2. Carregador
$me = $bTypes['CurrencyMod.ModEntry']
if (-not $me) { $problems.Add('CurrencyMod.ModEntry sumiu') }
else { foreach ($n in 'Start', 'Stop', 'Command') { if (-not ($me.Methods | Where-Object { $_.Name -eq $n -and $_.IsPublic })) { $problems.Add("ModEntry.$n sumiu") } } }

# 3. Transpiler por nome
if (-not ($bAll | ForEach-Object { $_.Methods } | Where-Object { $_.Name -eq 'DisplayedMoneyNet' })) { $problems.Add('NativeEffects.DisplayedMoneyNet foi renomeado') }

# 4. Mensagens da Unity: os mesmos nomes nas classes que derivam de MonoBehaviour/UIWindow.
$unity = '^(Awake|Start|Update|LateUpdate|FixedUpdate|OnGUI|OnEnable|OnDisable|OnDestroy|OnApplicationQuit)$'
$components = 'CurrencyMod.CentralBankWindow', 'CurrencyMod.DevTools', 'CurrencyMod.Diplomacia.IaModule', 'CurrencyMod.Diplomacia.UI.CorrespondenceTab',
    'CurrencyMod.Diplomacia.UI.CouncilButton', 'CurrencyMod.Diplomacia.UI.CouncilScreen', 'CurrencyMod.Diplomacia.UI.InterceptedLettersTab',
    'CurrencyMod.Diplomacia.UI.MailButton', 'CurrencyMod.Diplomacia.UI.MailScreen', 'CurrencyMod.Diplomacia.UI.ProvidersButtons',
    'CurrencyMod.Diplomacia.UI.ProvidersScreen', 'CurrencyMod.NativeUI.DiplomacyEconomyPanel', 'CurrencyMod.NativeUI.DiplomacyTradePolicyPanel',
    'CurrencyMod.NativeUI.NativeBankButton', 'CurrencyMod.NativeUI.NativeBankWindow', 'CurrencyMod.NativeUI.TradePostWindow'
# Componente novo (AddComponent) que não está nesta lista nem no obfuscar.xml: a checagem abaixo avisa.
foreach ($t in AllTypes $a | Where-Object { $_.BaseType -and $_.BaseType.Name -match '^(MonoBehaviour|UIWindow)$' -and $components -notcontains $_.FullName }) { $problems.Add("componente $($t.FullName) fora da lista (acrescente no obfuscar.xml e aqui)") }
foreach ($t in AllTypes $a | Where-Object { $components -contains $_.FullName }) {
    $msgs = @($t.Methods | Where-Object { $_.Name -match $unity -and -not $_.IsStatic } | ForEach-Object Name)
    $tb = $bTypes[$t.FullName]
    if (-not $tb) { $problems.Add("componente $($t.FullName) mudou de nome"); continue }
    foreach ($n in $msgs) { if (-not ($tb.Methods | Where-Object { $_.Name -eq $n })) { $problems.Add("$($t.FullName).$n sumiu") } }
}

# 5. Tipos salvos em JSON: mesmos campos e propriedades públicos.
$json = 'CurrencyMod.CurrencyWorld', 'CurrencyMod.EmpireCurrency', 'CurrencyMod.HistoryPoint', 'CurrencyMod.TradeRule', 'CurrencyMod.TollPrice',
    'CurrencyMod.TradeIncident', 'CurrencyMod.TollRecord', 'CurrencyMod.Diplomacia.IaWorld', 'CurrencyMod.Diplomacia.IaNation', 'CurrencyMod.Diplomacia.Persona',
    'CurrencyMod.Diplomacia.Feelings', 'CurrencyMod.Diplomacia.Stance', 'CurrencyMod.Diplomacia.DiaryEntry', 'CurrencyMod.Diplomacia.MemoryNote',
    'CurrencyMod.Diplomacia.ActionRecord', 'CurrencyMod.Diplomacia.Letter', 'CurrencyMod.Diplomacia.CongressNews', 'CurrencyMod.Diplomacia.InterceptRisk',
    'CurrencyMod.Diplomacia.ArmyMission', 'CurrencyMod.Diplomacia.SpyTrack', 'CurrencyMod.Diplomacia.IaRuntime', 'CurrencyMod.Diplomacia.NationDetail',
    'CurrencyMod.Diplomacia.CallRecord', 'CurrencyMod.Diplomacia.Council.CouncilMeeting', 'CurrencyMod.Diplomacia.Council.CouncilSpeech',
    'CurrencyMod.Diplomacia.Council.CouncilExchange', 'CurrencyMod.Diplomacia.Council.Minister', 'CurrencyMod.Diplomacia.Council.Personality'
$aTypes = @{}; foreach ($t in AllTypes $a) { $aTypes[$t.FullName] = $t }
foreach ($n in $json) {
    $ta = $aTypes[$n]; $tb = $bTypes[$n]
    if (-not $ta) { $problems.Add("tipo JSON $n não existe no original (lista desatualizada?)"); continue }
    if (-not $tb) { $problems.Add("tipo JSON $n mudou de nome"); continue }
    $fa = @($ta.Fields | Where-Object { $_.IsPublic } | ForEach-Object Name) + @($ta.Properties | ForEach-Object Name) | Sort-Object
    $fb = @($tb.Fields | Where-Object { $_.IsPublic } | ForEach-Object Name) + @($tb.Properties | ForEach-Object Name) | Sort-Object
    if (Compare-Object $fa $fb) { $problems.Add("tipo JSON $n mudou campos: " + ((Compare-Object $fa $fb | ForEach-Object { "$($_.SideIndicator)$($_.InputObject)" }) -join ' ')) }
}
# Tipos anônimos (visualizador F10): mesmas propriedades.
$anonA = @(AllTypes $a | Where-Object { $_.Name -like '<>f__AnonymousType*' } | ForEach-Object { ($_.Properties | ForEach-Object Name | Sort-Object) -join ',' } | Sort-Object)
$anonB = @($bAll | Where-Object { $_.Name -like '<>f__AnonymousType*' } | ForEach-Object { ($_.Properties | ForEach-Object Name | Sort-Object) -join ',' } | Sort-Object)
if (Compare-Object $anonA $anonB) { $problems.Add('propriedades de tipos anônimos mudaram (visualizador F10)') }

# 6. P/Invoke
foreach ($n in 'CryptProtectData', 'CryptUnprotectData', 'LocalFree') {
    if (-not ($bAll | ForEach-Object { $_.Methods } | Where-Object { $_.Name -eq $n -and $_.IsPInvokeImpl })) { $problems.Add("P/Invoke $n renomeado") }
}

# 7. O que precisava esconder: nomes internos e textos.
$renamed = @(AllTypes $a | Where-Object { -not $bTypes.ContainsKey($_.FullName) }).Count
$bytes = [IO.File]::ReadAllBytes($Ofuscada)
$u16 = [Text.Encoding]::Unicode.GetString($bytes)
$visible = @('dossiê', 'Você é o líder', 'Banco Central', 'invalid_key', 'workers.dev') | Where-Object { $u16.Contains($_) }

$a.Dispose(); $b.Dispose()
"métodos de patch conferidos: $patchCount · classes [HarmonyPatch]: $($harmonyA.Count) · tipos renomeados: $renamed · textos ainda em claro: $(if ($visible) { $visible -join ', ' } else { 'nenhum' })"
if ($visible) { $problems.Add("textos em claro: $($visible -join ', ')") }
if ($renamed -lt 20) { $problems.Add("só $renamed tipos renomeados: a ofuscação não pegou?") }
if ($problems.Count) { $problems | ForEach-Object { "  PROBLEMA: $_" }; exit 1 }
'  ofuscação conferida: nada que precisa do nome foi renomeado'
