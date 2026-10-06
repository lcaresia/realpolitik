# Congresso da Humanidade — mapa completo para propor, votar, subornar e travar a IA nativa (pesquisa 2026-10-04)

Pesquisa só de leitura do código descompilado. Nenhum arquivo do jogo ou do mod foi alterado.
Complementa `order-catalog.md` (§2.2, §2.3, §2.19), `crisis-negotiation.md` e `surrender.md` (mesmo estilo e mesmo padrão de travas).

**Notação** (caminhos relativos a `_Modding\decompiled\`)
- SIM = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\` · IO = `...Firstpass\Amplitude.Mercury.Interop\` · IOAI = `...Firstpass\Amplitude.Mercury.Interop.AI\` · SBX = `...Firstpass\Amplitude.Mercury.Sandbox\`
- INT = SIM\InternationalAncillary.cs · CVI = IO\InternationalCivicVoteInfo.cs · KVI = IO\InternationalCrisisVoteInfo.cs · BRI = IO\InternationalBribeInfo.cs · OIA = IO\OrderInternationalAction.cs
- DA = SIM\DiplomaticAncillary.cs · DRH = SIM\DiplomaticRelationHelper.cs · BDS = SIM\BaseDiplomaticState.cs · DOFA = SIM\DepartmentOfForeignAffairs.cs · CM = SIM\CultureManager.cs · DOC = SIM\DepartmentOfCulture.cs
- GENI = `Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.Generators.International\` · ANI = `...AI.Brain.Analysis.International\` · AIA = `...AI.Brain.Actuators\` · AIT = `...AI.Brain.Tasks\`
- UI = `Assembly-CSharp\Amplitude.Mercury.UI\` · MOD = `_Modding\src\CurrencyMod\Diplomacia\`
- **Termos:**
  - "Congresso" = o recurso *International* do DLC Together We Rule (`DiplomacyExpansionPack`).
  - "Peso" = `MajorEmpire.SwayStock` (*International Sway*), o peso do voto.
  - "Lei" = cívico internacional (`CivicDefinition.IsInternational`).
  - "Lei imposta" = o evento de osmose `CivicsShakedown` que o resultado de uma votação de lei cria.

---

## 0. Resumo

- **Três partes, destravadas juntas** (INT:1471-1512): votação de lei, votação de crise e consenso ideológico.
- **Destravamento natural.** No começo de um turno, a primeira nação (em ordem de índice) que tiver as três condições abaixo destrava o Congresso (INT:1232-1275):
  - era ≥ RPN `International_MinimumEraToUnlock`;
  - distrito Consulado funcionando;
  - ter conhecido pelo menos a fração RPN `International_MinimumPercentageEmpiresMetToUnlock` das nações vivas.
  - Também exige o DLC e a opção da partida ligada (INT:1129-1149).
- **Ao destravar, todas as nações passam a se conhecer** (FirstMeet + IntroduceYourself em todos os pares, INT:414-441). Isso também vale no desbloqueio forçado.
- **ForceUnlock\* são ordens de depuração aceitas de qualquer império.** A única checagem é "já destravado" (INT:247-252, 650-655, 1540-1545).
  - Para o teste: postar as três (ForceUnlockCivicVote, ForceUnlockCrisisVote, ForceUnlockIdeologicalConsensus) a partir de uma nação da IA de linguagem, no TurnMain.
  - Quem posta a primeira preside a 1ª sessão. Não sai notificação.
- **Votação de lei.**
  - Quem preside (rodízio fixo) escolhe uma lei internacional; todas as nações votam na opção A (índice 0) ou B (1) com o seu peso.
  - Fecha na virada para o turno `TurnEnd`.
  - Toda nação que não estiver na opção vencedora recebe a "lei imposta": adotar troca a lei; recusar custa influência (INT:473-529).
  - Empate não muda nada.
- **Votação de crise.**
  - Nasce de `DeclareInternationalCrisis` (a ferramenta `crise_internacional` já existe). Todas as nações tomam lado.
  - Empate e falta de votos favorecem o alvo (KVI:366).
  - O jogo executa `ProposeInternationalCrisisCompliance` em nome do vencedor (DA:957-965). O perdedor fica obrigado a cumprir as exigências do vencedor ou a declarar guerra surpresa, sem prazo.
- **Suborno.**
  - Troca alavancagem contra uma nação X por peso **seu** naquela votação, limitado ao peso de X.
  - Exige consulado e tem de vir **antes** do seu voto (INT:103-154; BRI:28-99).
  - No começo do turno seguinte, rende mais um bônus igual à alavancagem gasta; esse bônus só conta se você ainda não tiver votado.
- **A IA nativa nunca consegue subornar.** O atuador não preenche `NumberOfBribeActions`, e a validação recusa 0 (AIA\InternationalAction.cs:27-33, 42-49; INT:129-132).
- **Consenso ideológico:** cada nação pode gastar influência para destravar um eixo mundial. O efeito sai da média ideológica do mundo, e não de um voto (INT:1528-1634).
- **Nada do Congresso segura o TurnFinish da simulação** (DA:266-393 não olha o Congresso).
  - Para o humano há três obrigatórios que travam o botão de fim de turno: presidir a sessão, ter perdido uma crise e a lei imposta (§5).
- **Travas recomendadas** (thread da IA, padrão de MOD\NativeAiLocks.cs):
  - prefix em `GetContexts` de `StartInternationalCivicVote`, `ManageInternationalCivicVote` e `ManageInternationalCrisisVote`;
  - prefix por relação em `ResolveInternationalCrisisVote.GenerateDesires`, com volta ao nativo;
  - `DeclareInternationalCrisis` já está travada pelo DemandsPatch.
- **Armadilhas principais:**
  1. **Guerra surpresa pela IA nativa.** Sem a trava, a IA nativa de uma nação da IA de linguagem que perde uma crise **declara guerra surpresa sozinha** (GENI\ResolveInternationalCrisisVote.cs:49-65). O WarPatch atual não cobre esse caminho.
  2. **1ª sessão parada.** A 1ª sessão espera o primeiro presidente para sempre. Se ele for uma nação da IA de linguagem que não propõe, nunca há votação de lei (INT:183-185, 206-237).
  3. **Presidência de um turno só.** Nas sessões seguintes, o presidente tem um turno; no seguinte, a presidência passa adiante (INT:198-204).
  4. **Lei imposta sem resposta é descartada no TurnEnd**, e a influência é cobrada mesmo que falte (CM:1390-1418, 666-694).
  5. **`ProposeInternationalCrisisCompliance` não pode ser exposta.** Postada por um dos lados durante a votação, declara esse lado vencedor sem voto (BDS:508-513; DRH:929-943).
     - O `order-catalog.md` §2.2 diz que a UI usa essa ação; não usa.
  6. **Validações que estouram exceção** e deixam o ticket pendurado: alvo de suborno fora da faixa (INT:314, 730) e índice de eixo fora da faixa (INT:1553-1558).

---

## 1. Desbloqueio

### 1.1 Condições naturais

`GetUnlockInternationalFailureFlags(MajorEmpire)` (INT:1232-1275) é checado no NewTurnBegin, para cada nação em ordem de índice (INT:1471-1513).

| Falha | Condição | Linha |
|---|---|---|
| InternationalDisabled | opção `GameOption_InternationalEnabled` = "False" nos metadados da partida | INT:1132-1135, 1235-1238 |
| AlreadyUnlocked | as três partes já destravadas (sai antes das outras checagens) | INT:1239-1242 |
| WrongEmpire | nação morta | INT:1243-1246 |
| NotInCorrectEra | `EraLevel` < RPN `International_MinimumEraToUnlock` (a fonte é a própria nação) | INT:1247-1250 |
| NoConsulatBuilt | `Consulat.Entity` nulo ou `DistrictStatus` ≠ None (inativo ou danificado) | INT:1251-1254 |
| NotEnoughEmpiresMet | nações conhecidas (estado ≠ Unknown) ÷ nações vivas (menos ela) < RPN `International_MinimumPercentageEmpiresMetToUnlock` | INT:1255-1273 |

**Consulado**
- É o distrito `EmpireWideConsulatDefinition`, um por império, gravado em `MajorEmpire.Consulat` (SIM\MajorEmpire.cs:133; SIM\DepartmentOfTheInterior.cs:2016-2020, 3570-3574).
- A IA nativa constrói o seu (…AI.Brain.Generators.Construction\PlaceConsulateConstruction.cs). As travas do mod não tocam nisso.

**DLC e opção da partida**
- Sem `DiplomacyExpansionPack`, `CheckUnlockInternational` sai na hora (INT:1473).
- Sem o DLC ou com a opção desligada, o passe `NewTurnBegin_RefreshInternational` **nem é registrado** (INT:1136-1148).
- Consequência para o teste: forçar o desbloqueio nessas condições cria votações que **nunca terminam**. Conferir antes (§7.10).

**Quem destrava**
- A primeira nação que cumpre tudo destrava as três partes e vira `CivicVoteUnlockerEmpireIndex`.
- Ela recebe `InternationalUnlockedByYou`; as outras recebem `InternationalUnlockedByOther` (INT:1485-1510).

**Era mínima:** o valor está só nos dados.
- Indício, não prova: a IA nativa reduz a vontade de ganhar peso antes da era de índice 4 (GENI\ImproveDiplomaticPower.cs:64-70).
- O save de teste está na Medieval e o Congresso segue travado. Isso bate com era mínima ≥ 4 ou com falta de consulado.
- Para ler: `jogo rpn International_MinimumEraToUnlock` (src\CurrencyMod\GameCommands.cs:129-130, 451-478).

### 1.2 O que acontece ao destravar (`UnlockCivicVote`, INT:414-441)

1. `IsCivicVoteUnlocked = true`, e `CivicVoteUnlockerEmpireIndex` recebe quem destravou.
2. **Todos os pares de impérios maiores passam a se conhecer:**
   - par Unknown → `FirstMeet` + `IntroduceYourself` (vira Paz);
   - par PartialyKnown → `IntroduceYourself` de quem já conhecia o outro.
   - Em teste: cada nação da IA de linguagem passa a ver as outras 9 no dossiê, e o humano recebe uma série de primeiros contatos.
3. A ordem de presidência (`OrderedSessionLeaderEmpireIndices`) é montada uma vez só e nunca é refeita (INT:603-626). Ela segue, nesta prioridade:
   - quem destravou;
   - depois, peso decrescente;
   - depois, `InfluenceNet` decrescente;
   - por fim, índice.
4. `UnlockCrisisVote` e `UnlockIdeologicalConsensus` só ligam a flag (INT:866-869, 1704-1707).
5. **Com a crise destravada, qualquer guerra surpresa** (mesmo fora de crise) passa a ter dois efeitos:
   - todas as nações vivas que não são aliadas do agressor ganham alavancagem contra ele (RPN `CrisisVote_UnsanctionedWarLeverageGain`; INT:923-946, 825-836);
   - todas as nações vivas que não são aliadas nem vassalas dele ganham a reclamação `DeclaredUnsanctionedWar` contra ele (SIM\DiplomaticGrievanceSpawner_DeclaredUnsanctionedWar.cs:34-58).
   - A ferramenta `declarar_guerra` deve avisar isso à IA de linguagem.

### 1.3 Desbloqueio forçado (para o teste)

| Ordem `OrderInternationalAction{InternationalAction=…}` | Validação | Efeito | Linhas |
|---|---|---|---|
| ForceUnlockCivicVote (1) | só `AlreadyUnlocked` | `UnlockCivicVote(quem postou)`, com tudo de §1.2 | INT:247-252, 321-323 |
| ForceUnlockCrisisVote (5) | só `AlreadyUnlocked` | liga a flag | INT:650-655, 737-739 |
| ForceUnlockIdeologicalConsensus (10) | só `AlreadyUnlocked` | liga a flag | INT:1540-1545, 1590-1592 |

**Validade:** as três valem para qualquer império.
- `GetInternationalActionFailureFlags` só confere a faixa do índice de quem posta (INT:1277-1308).
- O sandbox não olha GodMode (`order-catalog.md` §0).
- São as mesmas ordens que a janela de depuração posta (Assembly-CSharp\Amplitude.Mercury.Overlay\FloatingWindow_International.cs:129-136, 279-285, 401-407).

**Fase:** só no TurnMain. No TurnBegin a ordem fica na fila; no TurnFinish é recusada (SBX\Sandbox.cs:1700-1703).

**Diferenças em relação ao desbloqueio natural**
- Não sai notificação nem narrador. As de INT:1497-1510 só saem pelo caminho natural, e esse caminho passa a sair cedo, porque tudo já está destravado.
- Quem postou ForceUnlockCivicVote preside a 1ª sessão, mesmo sem consulado e sem a era mínima.

**Postar as três**
- O botão do Congresso só aparece para o humano se alguma nação não tiver NotInCorrectEra, NotEnoughEmpiresMet nem NoConsulatBuilt (IO\InternationalSnapshot.cs:195-205; UI\ControlBanner.cs:630-633).
- Com as três destravadas, a checagem sai em `AlreadyUnlocked` e o botão aparece.
- Com só duas, na Medieval, o botão não aparece.

**Consenso forçado no meio da partida**
- `GlobalConsensus` só é recalculado com o consenso destravado (INT:1636-1641) e no SandboxStarted (INT:1340-1348).
- Depois de forçar, salvar e recarregar para a tela do consenso mostrar valores certos.

### 1.4 Fórmulas (RPN, só nos dados)

| Nome | Uso | Linha |
|---|---|---|
| International_MinimumEraToUnlock | era mínima | INT:71, 1247 |
| International_MinimumPercentageEmpiresMetToUnlock | fração de nações conhecidas | INT:73, 1270 |
| CivicVote_TurnDuration | duração da votação de lei (fonte = presidente) | INT:33, 386 |
| CivicVote_CooldownDuration | intervalo entre sessões (fonte = presidente anterior) | INT:35, 189 |
| CivicVote_InfluenceCostToDiscard | custo para recusar a lei imposta | INT:37, 520 |
| CrisisVote_TurnDuration | duração da votação de crise (fonte = declarante) | INT:41, 790 |
| CrisisVote_UnsanctionedWarLeverageGain | alavancagem ganha contra quem faz guerra surpresa | INT:43, 835 |
| Bribe_LeverageCost / Bribe_BonusSway | custo e bônus de cada suborno (alvo; fonte = quem suborna) | INT:29-31, 144 |
| IdeologicalConsensus_TurnDuration / _InfluenceRequiredToUnlockAxis / _InfluenceCostToContribute | consenso | INT:81-85, 1530-1532, 1729-1731, 1824 |

Cada votação grava os valores efetivos dela (`TurnEnd`, `LeverageCostToBribe`, `MaximumBonusSwayByBribe`). O dossiê lê de lá e não precisa de RPN.

---

## 2. Modelo de dados

### 2.1 Onde vive

| O quê | Onde | Observação |
|---|---|---|
| Estado geral | `Sandbox.InternationalAncillary` (INT:49-69) | `IsCivicVoteUnlocked`, `IsCrisisVoteUnlocked`, `IsIdeologicalConsensusUnlocked`, `CivicVoteUnlockerEmpireIndex`, `OrderedSessionLeaderEmpireIndices` |
| Votações de lei | `CivicVoteInfos` (`ArrayWithFrame<InternationalCivicVoteInfo>`) | Só cresce (INT:380-381, 409). A última é a ativa ou a mais recente; o histórico inteiro fica guardado |
| Votações de crise | `CrisisVoteInfos` (`PoolAllocator<InternationalCrisisVoteInfo>`) | O índice do pool é o `CrisisVoteIndex` das ordens. Liberada no NewTurnBegin seguinte ao resultado (INT:1072-1079) |
| Consenso | `IdeologicalConsensusInfos`, um por `IdeologicalConsensusDefinition` | INT:1162-1174 |
| Crise na relação | `relation.DiplomaticState.Crisis` (`CrisisInfo`) | Status `InternationalCrisisVoteOnGoing` / `InternationalCrisisVoteEnded`, mais `InternationalCrisisWinnerEmpireIndex` (SIM\CrisisInfo.cs:8-21) |
| Peso | `MajorEmpire.SwayStock` | Propriedade calculada pelos dados (SIM\MajorEmpire.cs:472-474). A IA nativa busca peso vassalizando povos (…Generators.MinorRelation\EnactMinorPuppetTreaty.cs:9-33) |
| Alavancagem | `DiplomaticAmbassy.LeverageActionPointStock` da **sua** embaixada com X | SIM\DiplomaticAmbassy.cs:319-324. Ganho a cada TurnEnd (INT:1457-1469) |
| Save | INT:1916-1969 | Tudo é salvo, inclusive os subornos (CVI:38-47; KVI:49-58; BRI:50-65) |

### 2.2 Votação de lei (`InternationalCivicVoteInfo`, CVI:7-173)

**Campos**
- `CurrentState`: Active, EndedByAllEmpireVotes, EndedByForce ou EndedByTimeout.
- `TurnBegin`, `TurnEnd`, `SessionLeaderEmpireIndex`, `CivicName`, `MajorEmpireBallots[]`.

**Cédula** (`BallotInfo`, CVI:18-93)
- `ChoiceIndex` (−1 = ainda não votou) e `SwayContribution`.
- `BribeInfos[alvo]`: os subornos que **esta** nação comprou contra cada alvo.
- Votar fixa o peso na hora: `SwayContribution` = `SwayStock` + soma dos bônus de suborno (CVI:77-81; INT:457-464). O peso que crescer depois não conta.
- Não há troca de voto nem abstenção explícita: abster-se é não votar.

**Sessões e presidência** (`TryComputeNextCivicVoteSessionInfo`, INT:173-240)
- **1ª sessão**
  - Abre no turno do desbloqueio (`nextSessionTurnBegin` = turno atual, INT:176).
  - Espera, **sem prazo**, o primeiro da ordem, que é quem destravou (`num3 = 0`, INT:183-185).
  - Quem não tem lei propunível é pulado.
- **Sessões seguintes**
  - Abrem no `TurnEnd` da anterior + `CivicVote_CooldownDuration` (INT:189). O presidente é o seguinte na ordem.
  - **A cada turno sem proposta, a presidência passa ao próximo** (`num6` += atraso, INT:198-204).
  - Presidente sem lei propunível é pulado, e a sessão atrasa um turno (INT:228-235).
- **Lei propunível** (`CanCivicBeProposedToVote`, INT:443-455): cívico com `IsInternational` e com status Available ou Enacted **para o presidente**.
- **Opções:** toda lei internacional tem duas, índices 0 e 1.
  - A soma só conhece 0 e 1 (CVI:146-167).
  - A interface reserva duas (IO\InternationalSnapshot.cs:134-144).

**Início** (`StartCivicVote`, INT:375-412)
- `TurnEnd` = turno atual + `CivicVote_TurnDuration` (fonte = presidente).
- Monta a cédula de todas as nações e os subornos possíveis de cada uma contra cada uma (§2.4).
- Notifica todas as nações, menos o presidente (`InternationalCivicVoteStartedNotificationData`, INT:399-408).

**Fim** (`CheckActiveCivicVotes`, no NewTurnBegin, INT:550-601)
- Quando o turno chega a `TurnEnd`, a votação fecha por tempo (EndedByTimeout). O último turno para votar é `TurnEnd − 1`.
- Se todas as nações vivas já votaram, fecha como EndedByAllEmpireVotes, mas também **só no NewTurnBegin seguinte**.
- No turno `TurnEnd − 1`, quem ainda não votou recebe "a votação está acabando" (INT:586-592).
- A cada NewTurnBegin, antes da checagem, entra o bônus de conclusão dos subornos (INT:577-583).

**Resultado** (`EndCivicVote`, INT:473-529)
- Equilíbrio = soma do peso de B − soma do peso de A (CVI:146-167).
  - \> 0 → vence B (opção 1).
  - < 0 → vence A (opção 0).
  - **= 0 → nada acontece** (INT:480).
- Em seguida, o jogo passa por **cada** nação (inclusive as que votaram na opção vencedora) e olha o status dela nesse cívico:
  - proibido (`IsCivicForbidden`), congelado (Frozen) ou já na opção vencedora → nada (INT:488-499, 513-514);
  - Unknown ou Locked → o cívico é **destravado** para ela, salvo se o cívico mutuamente exclusivo estiver em vigor (INT:500-507);
  - Available, Enacted ou recém-destravado → `TryTriggerOsmosisEvent(CivicsShakedown)` numa cidade dela (INT:516-525; CM:546-582).
- A lei imposta (osmose) leva:
  - `CivicChoiceName` = a opção vencedora;
  - `DiscardInfluenceCost` = RPN `CivicVote_InfluenceCostToDiscard`;
  - `DiscardFailureFlags` = NotEnoughInfluence se a nação não tiver essa influência **no momento do resultado**;
  - `ProposingEmpireIndex` = o presidente.
- A resposta à lei imposta está em §2.6.

### 2.3 Votação de crise (`InternationalCrisisVoteInfo`, KVI:8-375)

**Campos**
- `PoolAllocationIndex` e `CurrentState`:
  - Active;
  - CancelledByDeclarator, CancelledByTargeted ou CancelledByVassalizationChanged;
  - EndedByAllEmpireVotes, EndedByForce ou EndedByTimeout.
- `LoserActionChosen`: None, AcceptDemands ou DeclareSurpriseWar.
- `TurnBegin`, `TurnEnd`, `DeclaratorEmpireIndex`, `TargetedEmpireIndex`.
- Cópias das exigências dos dois lados no início (`DeclaratorOnGoingDemands`, `TargetedOnGoingDemands`; INT:871-880).
- Cédula: `SideWithEmpireIndex` (o declarante ou o alvo; −1 = não votou), `SwayContribution` e `BribeInfos[]` (KVI:29-104).

**Declarar** (`DeclareInternationalCrisis`, ação 47; ferramenta `crise_internacional`, MOD\IaModule.cs:976-978)
- **Estado da relação:** só Paz ou Vassalagem (SIM\DiplomaticState_Peace.cs:112; SIM\DiplomaticState_VassalToLiege.cs:111).
  - Em Aliança ou Guerra = `WrongDiplomaticAction` (SIM\DiplomaticState_Alliance.cs:100; SIM\DiplomaticState_War.cs:125).
- **Checagens** (BDS:487-507):
  - ter exigências em aberto contra o alvo (`OwnerHasNoDemand`);
  - `GetStartCrisisVoteFailureFlags` (INT:758-779): Congresso de crise destravado, **consulado de quem declara**, e nenhuma votação válida entre os dois (inclusive uma terminada que ainda espera o perdedor);
    - se já houver crise entre os dois → `PendingInternationalCrisis`;
    - qualquer outra falha dessa função vira `Locked`;
  - apoio à guerra ≥ limiar da guerra formal, **ou** o alvo ter recusado as suas exigências (senão, `BelowFormalWarMoralThreshold`).
- **Custo:** nenhum. Não custa influência (DOFA:2506-2523) nem mexe no apoio à guerra (SIM\DiplomaticMoralHelper.cs:751-791).
- **Fase:** TurnBegin ou TurnMain (BDS:682-726).
- **Efeito** (DRH:922-928):
  - status da crise vira `InternationalCrisisVoteOnGoing`;
  - zera a exigência forçada pelo consulado naquela relação (INT:808);
  - abre a votação com `TurnEnd` = turno + `CrisisVote_TurnDuration` (fonte = declarante);
  - notifica **só o alvo** (INT:803-806), além do aviso de reação diplomática (DOFA:1416-1428).

**O que fica travado enquanto há crise pendente** (status OnGoing ou Ended, `HasPendingInternationalCrisis`; SIM\CrisisInfo.cs:50-57)
- aceitar exigências, só durante a votação (BDS:383-386). Depois do veredito, aceitar volta a valer, e é a resposta do perdedor;
- recusar ou retirar exigências (BDS:395-398, 409-412);
- enrolar: só vale com status OnGoing (BDS:420-423);
- guerra formal (BDS:199-203). A guerra surpresa continua possível;
- propor fim da crise e propor aliança (BDS:221-222, 242-245);
- transformar reclamações em exigências (BDS:675-678);
- exigência forçada pelo consulado (SIM\DiplomaticConsulatHelper.cs:104-107);
- o apoio à guerra para de mudar pela diferença de exigências (SIM\DiplomaticMoralHelper.cs:279-301).
- **Não existe "retirar a crise".** Só uma guerra entre os dois ou uma mudança de vassalagem cancela a votação (ver "Cancelamento", abaixo).

**Votar**
- Qualquer nação viva com peso > 0 pode votar, inclusive os dois lados (§3).
- A IA nativa de cada lado vota em si mesma no turno `TurnEnd − 1`; se for Impulsive, logo no início (ANI\ComputeInternationalCrisisVote.cs:83-101).

**Fim:** igual ao da votação de lei (`CheckActiveCrisisVotes`, INT:1056-1115): fecha por tempo em `TurnEnd` ou quando todas as nações votaram, com aviso em `TurnEnd − 1`.

**Resultado** (`ComputeVoteResults`, KVI:350-369; aplicado em DA:957-965)
- Equilíbrio = soma do peso do lado do alvo − soma do peso do lado do declarante.
  - **≥ 0 → vence o alvo.** Empate e ausência de votos favorecem o alvo.
- O jogo executa `ExecuteAction(vencedor, perdedor, ProposeInternationalCrisisCompliance)` (DRH:929-943):
  - as exigências do **perdedor** contra o vencedor são descartadas;
  - se o vencedor tem exigências: status `InternationalCrisisVoteEnded`, com `InternationalCrisisWinnerEmpireIndex` = vencedor. **O perdedor precisa responder**;
  - se não tem (o alvo venceu sem exigências próprias): a crise acaba (None), o apoio à guerra dos dois é zerado e a votação conta como resolvida (INT:882-892).

**Resposta do perdedor** (com status Ended)
- **`AcceptDemands`** (DRH:722-744):
  - entrega as exigências do vencedor;
  - zera o apoio à guerra dos dois e encerra a crise (None);
  - marca a votação como resolvida (INT:906-921).
- **`DeclareSurpriseWar`**:
  - abre a guerra, e todas as outras nações ganham alavancagem e reclamação contra o perdedor (§1.2, item 5);
  - marca a votação (INT:955-958);
  - exige apoio à guerra no limiar da surpresa (BDS:209-213). `UseFormalWarInstead` não aparece com crise pendente (BDS:214).
- `RefuseDemands` e `StallForTime` **não** valem nesse estado (BDS:409-412, 420-423).
- **Não há prazo nenhum.** Enquanto o perdedor não responde:
  - o status continua Ended e a relação fica travada (lista acima);
  - a votação continua ocupando o pool;
  - nada no TurnFinish olha isso (DA:266-393);
  - a IA nativa responde no mesmo turno; o humano é obrigado pelo popup (§5).

**Cancelamento:** os estados Cancelled\* não aplicam resultado (DA:959).
- Guerra surpresa entre os dois com a votação ativa → cancelada por quem declarou a guerra (INT:947-954).
  - Se quem declara é o perdedor que estava sendo esperado, a guerra conta como a resposta dele (INT:955-958).
- Guerra formal ou guerra puxada por aliança entre os dois → cancelada (INT:894-904).
- Vassalo libertado ou vassalagem imposta numa rendição → CancelledByVassalizationChanged (DRH:777-780; DA:1556-1564).
- Nação eliminada: as crises dela que esperavam resposta são canceladas, e o voto dela nas outras é apagado (INT:1035-1054, 1372-1377).
- Save antigo: na carga, as crises de relações em guerra são canceladas (INT:1399-1418).

**Memória da IA nativa**
- Cada voto numa crise fica gravado por muito tempo (`TimerVeryLong` = 150) nas nações dos dois lados:
  - quem recebeu o apoio grava `YouSidedWithMeDuringCrisisVote`;
  - o outro lado grava `YouSidedAgainstMeDuringCrisisVote`.
- Fontes: INT:990; AI.Brain\…Analysis.Diplomacy\ComputeRelationLogs.cs:469-481; Amplitude.Mercury.Data\…AI\LogNames.cs:62-63.

### 2.4 Suborno (`InternationalBribeInfo`, BRI:6-105)

**Montagem:** no início de cada votação, para cada par (quem suborna, alvo) (INT:140-145):
- `TargetedInitialSwayStock` = o peso do alvo naquele turno;
- `LeverageCostToBribe` = RPN `Bribe_LeverageCost`;
- `MaximumBonusSwayByBribe` = RPN `Bribe_BonusSway`;
- `MaximumBribeActionCount` = piso(peso do alvo ÷ bônus por suborno). Contra si mesmo, 0 (BRI:34-43).

**Comprar n subornos** (INT:147-154; BRI:77-88)
- Tira n × custo da alavancagem da **sua** embaixada com o alvo (SIM\DiplomaticConsulatHelper.cs:547-564).
- Soma n × bônus ao seu peso nesta votação. O último suborno possível fica limitado ao que sobra do peso do alvo.
- Guarda n × custo como "bônus de conclusão", somado ao seu peso no NewTurnBegin seguinte (BRI:90-99; INT:577-583, 1093-1099).

**Efeitos**
- O alvo **não perde peso**: o suborno "toma emprestado" um peso proporcional ao do alvo.
- O voto fixa o peso (§2.2). Subornar e votar no mesmo turno perde o bônus de conclusão.
- Depois de votar, qualquer suborno é recusado (`AlreadyVoted`).
- Vale para votação de lei e de crise; cada votação tem as próprias contas.

### 2.5 Consenso ideológico

- Há uma entrada por `IdeologicalConsensusDefinition`, cada uma ligada a um eixo ideológico (INT:1162-1174; IO\InternationalIdeologicalConsensusInfo.cs:9-29).
- **O eixo fica bloqueado** até `ContributionCount` ≥ `ContributionRequiredCount`.
  - Cada contribuição soma 1 e custa influência: RPN `IdeologicalConsensus_InfluenceCostToContribute`, em função do nº de impérios e do nº de contribuições **suas** naquele eixo. Cada contribuição sua custa mais que a anterior (INT:1528-1533).
  - O número exigido depende do nº de impérios e de quantos eixos já foram destravados (INT:1709-1735).
- **Eixo destravado:**
  - a média do eixo entre impérios maiores e povos com cidade (`GlobalConsensus`, INT:1636-1702) escolhe uma orientação e uma seção;
  - os efeitos dessa seção são aplicados (INT:1751-1806) e reavaliados a cada `IdeologicalConsensus_TurnDuration` turnos (INT:1808-1826);
  - todas as nações são avisadas.
- Nos extremos do eixo social, surgem tecnologias "à venda" por osmose (INT:1828-1914).
- **Única decisão possível:** contribuir com influência (`ContributeToIdeologicalConsensus`). A direção do efeito não é votada; vem da ideologia média do mundo.
- A IA nativa contribui com motivação de 0,2 a 0,8, mais alta quando o consenso atual cai na orientação dela (GENI\ContributeToInternationalIdeologicalConsensus.cs:76-94).

### 2.6 Lei imposta (osmose `CivicsShakedown` criada pelo Congresso)

- **Onde aparece:** uma por nação afetada, numa cidade dela (CM:546-582).
  - De preferência a capital, se ela não tiver outro evento.
  - Se todas as cidades já tiverem evento, a lei imposta **substitui** um deles.
- **Aceitar:** `OrderPurchaseCulturalOsmosisEvent` (DOC:484-522) adota a lei ou troca a opção (CM:620-659), sem o custo normal de influência.
- **Recusar:** `OrderDiscardCulturalOsmosisEvent` (DOC:448-482) paga `DiscardInfluenceCost` (CM:666-694). A validação exige ter essa influência (DOC:472-475).
- **Sem resposta, o TurnEnd recusa sozinho e cobra a influência**, mesmo que falte; a influência pode ficar negativa (CM:1390-1418; DOC:97-104).
- **Reclamação do presidente** (`DiscardedCivicsShakedown`) contra quem recusou: só sai se `DiscardTriggeringGrievanceType` estiver marcado.
  - `EndCivicVote` não marca (INT:516-525); só a limpeza feita na carga do save marca (CM:771-774).
  - Na prática: recusar no mesmo turno não gera reclamação; recusar depois de uma recarga gera.
- **Como reconhecer uma lei imposta pelo Congresso:**
  - o nome de efeitos é `OsmosisEffectsInternationalCivicVoteResult` (INT:91, 522), mas a carga troca pelo genérico (CM:765);
  - por isso, comparar `ItemName` e `CivicChoiceName` com a última votação de lei terminada.

---

## 3. Ordens e validação

### 3.1 Ações do jogador

`OrderInternationalAction` (OIA:6-46) tem os campos `InternationalAction`, `CivicChoiceIndex`, `CrisisVoteIndex`, `IdeologicalConsensusIndex`, `TargetedEmpireIndex` (padrão −1 nos quatro índices) e `NumberOfBribeActions` (padrão 0).
- Validação: `DOFA.ValidateOrderInternationalAction` → `GetInternationalActionFailureFlags` (DOFA:1814-1826; INT:1277-1308).
- Execução: INT:1310-1338.

| Ação | Ordem e campos | Checagens (em ordem) | Custo | Linhas |
|---|---|---|---|---|
| Propor lei (presidente) | `OrderStartCivicVote{CivicName}` | Locked; WrongEmpire; WrongCivicProposal (não é internacional, ou o status para o presidente não é Available/Enacted); WrongSessionLeader (não é o próximo presidente); VoteInactive (a sessão ainda não abriu) | — | DOFA:1971-1983; INT:342-373 |
| Votar lei | `VoteForCivicChoiceIndex` (2), `CivicChoiceIndex` 0 ou 1 | Locked; sem votação ativa = VoteInactive; AlreadyVoted; NotEnoughSway (peso + bônus ≤ 0); WrongVoteChoice só se < 0 | — | INT:269-301 |
| Subornar (lei) | `BribeForCivicVote` (3), `TargetedEmpireIndex`, `NumberOfBribeActions` | VoteInactive; AlreadyVoted; alvo fora da faixa = exceção antes do WrongOrder (§3.3); alvo = você = WrongEmpire; NoConsulatBuilt; NotEnoughLeverage (alavancagem < n × custo); NotEnoughAction (n < 1, sem subornos restantes, ou n acima dos restantes) | alavancagem | INT:303-315, 103-138 |
| Tomar lado (crise) | `SideWithCrisisDeclarator` (6) / `SideWithCrisisTargeted` (7), `CrisisVoteIndex` | Locked; índice fora da faixa ou slot livre = WrongOrder; votação não ativa = VoteInactive; AlreadyVoted; NotEnoughSway (só o peso **base** ≤ 0; o bônus de suborno não conta aqui) | — | INT:656-687 |
| Subornar (crise) | `BribeForCrisisVote` (8), `CrisisVoteIndex`, `TargetedEmpireIndex`, `NumberOfBribeActions` | Índice ou slot = WrongOrder; AlreadyVoted; as mesmas do suborno de lei. **Não confere se a votação está ativa** | alavancagem | INT:714-731 |
| Contribuir ao consenso | `ContributeToIdeologicalConsensus` (11), `IdeologicalConsensusIndex` | Locked; eixo já destravado = AlreadyUnlocked; NotEnoughInfluence | influência | INT:1546-1571, 1593-1626 |
| Declarar crise | `OrderDiplomaticAction{DeclareInternationalCrisis}` | ver §2.3 | — | BDS:487-507 |
| Responder ao veredito | `OrderDiplomaticAction{AcceptDemands}` ou `{DeclareSurpriseWar}` | ver §2.3 | o dinheiro das exigências | BDS:374-388, 209-218 |
| Responder à lei imposta | `OrderPurchaseCulturalOsmosisEvent` / `OrderDiscardCulturalOsmosisEvent`, `CulturalOsmosisEventIndex` | ver §2.6 | influência (só para recusar) | DOC:448-522 |

### 3.2 O que não expor à IA de linguagem

| Ação | Por quê | Linhas |
|---|---|---|
| ForceUnlock\* (1, 5, 10) | depuração; só para o teste (§1.3) | INT:247-252, 650-655, 1540-1545 |
| ForceEndCivicVote (4) / ForceEndCrisisVote (9) | qualquer império encerra na hora qualquer votação ativa | INT:253-260, 330-334, 688-705, 749-751 |
| ForceApplyIdeologicalConsensusEffects (12) | depuração | INT:1572-1577, 1627-1629 |
| `OrderStartCrisisVote` | abre crise sem exigências e sem mudar o estado da relação; só a janela de depuração usa | DOFA:1985-1997; INT:781-810 |
| `ProposeInternationalCrisisCompliance` (48) | ver detalhes abaixo | BDS:508-513; DRH:929-943; DA:963 |

**Detalhes de `ProposeInternationalCrisisCompliance`**
- A única checagem é haver votação em andamento (BDS:508-513).
- Postada por um dos lados, **declara esse lado vencedor**: descarta as exigências do outro e, se o lado que postou tiver exigências, obriga o outro a cumpri-las (DRH:929-943).
- A votação continua aberta e reaplica o resultado quando fechar.
- A UI nunca posta essa ação; só o próprio jogo, no fim da votação (DA:963).

### 3.3 Validações com falhas

**Estouram exceção** (o ticket nunca fecha; `order-catalog.md` §6.4)
- Suborno com `TargetedEmpireIndex` fora da faixa, inclusive −1. O jogo lê `BribeInfos[alvo]` **antes** de checar a faixa (INT:314, 730).
- Consenso com índice fora da faixa: o jogo loga erro e lê o array mesmo assim (INT:1553-1558).
- Osmose com índice igual a `Capacity`: a checagem usa `>` em vez de `>=` (DOC:454, 490).

**Passam e desperdiçam**
- `CivicChoiceIndex` > 1 é aceito: a nação conta como "já votou", mas o voto é ignorado na soma (INT:291-294; CVI:161-164).
- Suborno de crise numa votação já terminada (que ainda espera o perdedor) é aceito e gasta alavancagem.

### 3.4 Fases do turno

| Ordem | TurnBegin | TurnMain | TurnFinish | TurnEnd |
|---|---|---|---|---|
| OrderInternationalAction, OrderStartCivicVote | fila | aceita | **recusada** | recusada |
| OrderDiplomaticAction com DeclareInternationalCrisis ou DeclareSurpriseWar | aceita | aceita | WrongSandboxState | recusada |
| OrderDiplomaticAction com AcceptDemands | aceita | aceita | aceita | recusada |
| Osmose (Purchase/Discard) | fila | aceita | recusada | recusada |

Fontes: SBX\Sandbox.cs:1700-1707; BDS:682-726.

### 3.5 Multiplayer

- **`Pack`/`Unpack` de `OrderInternationalAction` não transmitem `NumberOfBribeActions`** (OIA:22-40).
  - Online, o suborno de um cliente chega com 0 e é recusado.
  - Na réplica dos outros, a ordem é processada com 0, o que arrisca dessincronia.
- **Solo:** o `NullNetworkingManager` entrega o próprio objeto (Amplitude.Framework\Amplitude.Framework.Networking\NullNetworkingManager.cs:64-80, 100-112; SBX\Sandbox.cs:1498-1499).
  - Então o campo chega, e é disso que o suborno da UI depende (UI\InternationalScreen_BribePopup.cs:270-290).
  - Confirmar no teste (§7.10, passo 5).
- O mod já fica de fora em partida online (MOD\ActionExecutor.cs:121; MOD\Capture\TurnCapture.cs:40). Manter assim.

---

## 4. IA nativa

### 4.1 Quem decide cada passo

**Propor lei** (GENI\StartInternationalCivicVote.cs)
- **Quando roda:** com o Congresso de lei destravado, o turno ≥ início da próxima sessão e o cérebro sendo o próximo presidente (:47-53).
- **Como escolhe** (:78-166), nesta ordem de critério:
  1. prefere lei com status Available;
  2. depois, a maior vontade de adotar (`CivicEvaluations`; só conta para lei sem opção ativa);
  3. depois, a lei mais "controversa", com o mundo dividido (:168-219);
  4. por fim, a que foi votada há mais tempo.
- **Ordem postada:** AIA\StartInternationalCivicVote.cs:15-21.

**Votar lei** (ANI\ComputeInternationalCivicVote.cs → GENI\ManageInternationalCivicVote.cs)
- **Opção** (:130-226): a de melhor avaliação para ela; sem avaliação, segue as nações de quem gosta (rapport).
- **Momento** (:61-128):
  - imediato se ela já tem a lei em vigor (CoolHeaded adia);
  - "reativo" se a votação está indo contra ela;
  - senão, num turno entre o início e `TurnEnd − 1` (ANI\Helpers.cs:9-34), mais cedo se nenhum humano falta votar.
- **Ordem postada:** AIA\InternationalAction.cs:20-26.

**Votar crise** (ANI\ComputeInternationalCrisisVote.cs → GENI\ManageInternationalCrisisVote.cs)
- **Os dois lados:** votam em si mesmos no turno `TurnEnd − 1`; se Impulsive, logo no início (:83-101).
- **As outras nações** (:103-205):
  - dão uma nota de relação para cada lado: aliança, guerra, nêmesis, rapport ou superioridade;
  - notas iguais → abstenção;
  - notas extremas → voto imediato;
  - senão → voto adiado.

**Subornar** (mesmas análises; `PickBribeTargets` em ANI\InternationalBribeTargetSelector.cs)
- **Nunca passa na validação.**
  - O atuador não preenche `NumberOfBribeActions` (AIA\InternationalAction.cs:27-33, 42-49), e n = 0 dá NotEnoughAction (INT:129-132).
  - A tarefa falha (AI.Brain\…\OrderActuator.cs:94-105), e o plano de suborno alterna a cada passada (ANI\ComputeInternationalCivicVote.cs:234-238).
- Consequência: uma nação da IA de linguagem que suborna tem vantagem real sobre a IA nativa.

**Declarar crise**
- **Cadeia de decisão:**
  1. `ComputeInternationalCrisisWill` (ANI\ComputeInternationalCrisisWill.cs:31-100) calcula a vontade = (motivo das nossas exigências − impacto das deles) × 0,5 + poder diplomático. Quer a crise se a vontade passar de 1,3 ± 0,2.
  2. `ComputeCrisisStrategy` usa essa vontade (AI.Brain\…Analysis.Diplomacy\ComputeCrisisStrategy.cs:74-154).
  3. `ComputeDemandsAction` escolhe `DeclareInternationalCrisis` (…\ComputeDemandsAction.cs:147-151).
- **Já travado** pelo DemandsPatch do mod (MOD\NativeAiLocks.cs:166-200).
  - Exceção: relações com exigência forçada pelo consulado, mas nelas a IA nunca escolhe a crise.

**Responder ao veredito** (GENI\ResolveInternationalCrisisVote.cs; modo Minimal, roda sempre)
- **Quando roda:** em cada relação em que a nação perdeu a crise (:32-47).
- **Como decide** (:49-90):
  - guerra surpresa indisponível → aceita;
  - exigências toleráveis (limiares × 1,2) → aceita (motivação 0,9) ou guerra surpresa (0,1);
  - exigências intoleráveis → **guerra surpresa**.
- **Ordem postada:** AIA\DiplomaticAction.cs:57-70. A IA só posta DeclareSurpriseWar nesse caso.
- **Não é coberto pelas travas atuais:** o WarPatch só zera `CanDeclareWar` (MOD\NativeAiLocks.cs:64-91), e este gerador não consulta essa análise.

**Consenso:** GENI\ContributeToInternationalIdeologicalConsensus.cs:30-45, 76-94.

**Lei imposta** (AI.Brain\…Generators.Culture\AnswerCivicsOsmosis.cs:38-100 → AIA\DoOsmosisAction.cs:14-27)
- Aceita se não puder pagar a recusa.
- Recusa quando a opção atual vale mais para ela (ou a nova não vale nada).
- Se recusar sair de graça mas derrubar a ordem pública, aceita.

**Peso:** `ImproveDiplomaticPower` (GENI\ImproveDiplomaticPower.cs) busca peso. O mod já o enviesa conforme o foco (MOD\NativeAiBias.cs:62). Não precisa travar.

### 4.2 Pontos de patch recomendados

Rodam na thread da IA, no padrão de MOD\NativeAiLocks.cs: `__instance.Brain`, `IsLlmActive`, contadores e `LogOnce`.
- Os métodos são `protected override`: aplicar o patch pelo nome.
- `StartInternationalCivicVote` e `InternationalAction` existem com o mesmo nome em Generators, Tasks e Actuators: usar o namespace completo.

| Trava | Patch | Por quê aqui |
|---|---|---|
| Não propõe lei | Prefix em `Generators.International.StartInternationalCivicVote.GetContexts()`: `return leaderFallback.Contains(e) \|\| !BlocksCongress(Brain)` | Sem contexto, nenhuma tarefa nasce. A volta ao nativo só serve para a 1ª sessão parada (§6, item 2) |
| Não vota nem suborna em lei | Prefix em `ManageInternationalCivicVote.GetContexts()` | Um só gerador cuida do voto e do suborno |
| Não vota nem suborna em crise | Prefix em `ManageInternationalCrisisVote.GetContexts()` | Idem |
| Não responde ao veredito | Prefix em `ResolveInternationalCrisisVote.GenerateDesires(DiplomaticEmbassy context)`: devolve `false` se `IsLlmActive(e) && !verdictFallback.Contains((e << 16) \| context.OtherEmpireIndex)` | Granularidade por relação: vencido o prazo (ou com a API fora), a IA nativa responde só naquela relação |
| Não contribui ao consenso | Prefix em `ContributeToInternationalIdeologicalConsensus.GetContexts()` | Só quando a ferramenta existir (fase 2). Antes disso, deixar com a IA nativa |
| (opcional) Lei imposta conforme a vontade da IA de linguagem | Postfix em `Actuators.DoOsmosisAction.CreateOrder()`, trocando a ordem (Discard ↔ Purchase) conforme a postura gravada (§7.4) | Responde no mesmo turno, sem depender da demora da API (CM:1390-1418) |

**Notas**
- `DeclareInternationalCrisis`: nada a fazer, o DemandsPatch já cobre.
- As análises (`ComputeInternationalCivicVote`, `ComputeInternationalCrisisVote`) podem continuar rodando: só preenchem dados.
- As tabelas de volta ao nativo (`leaderFallback`, `verdictFallback`) são publicadas pela thread principal como objetos imutáveis, trocados inteiros. É o mesmo padrão de `surrenderFallback` (MOD\NativeAiLocks.cs:241-246; MOD\ProposalHold.cs:46-76).
- Usar um contador próprio (`blockedCongress`) e mostrá-lo no `Describe()`.

**Não usar como trava principal**
- Prefix devolvendo `null` em `Actuators.InternationalAction.CreateOrder`: `PostAndTrackOrder(null)` lança exceção dentro da IA (`surrender.md` §3.2).
- A rede de segurança P4 (marcar a ordem em `Brain.PostAndTrackOrder` e recusá-la em `OrderPolicyController.GetPolicy`, MOD\ArmyOrders.cs:884-939).
  - Com ela a tarefa falha, e o gerador a recria a cada passada: spam de ordens.
  - Serve só como contador ou rede extra para `OrderInternationalAction`/`OrderStartCivicVote` de nação travada.

---

## 5. O que o humano vê

| Situação | O que aparece | Trava o fim do turno? |
|---|---|---|
| Congresso formado (natural) | Notificação imersiva com narrador; clicar abre a tela (UI\NotificationsController.cs:1456-1491) | não |
| Congresso forçado | Só o botão do Congresso (§1.3) | não |
| Humano preside, no turno de abertura da sessão | `MandatoryStartCivicVote` (prioridade 40) abre a escolha de lei (UI\MandatoryStartCivicVote.cs:11-54) | **sim**, até propor (UI\EndTurnWindow.cs:338-364, 565-577). Na 1ª sessão, todo turno até propor |
| Votação de lei aberta | Notificação para todas as nações menos o presidente (UI\NotificationsController.cs:1492-1513). Na tela: placar por opção, quem votou e quem subornou (UI\InternationalScreen_VoteDetail.cs:163-202; UI\InternationalScreen_MemberItem.cs:114-162) | não. Não votar = abster-se |
| Falta 1 turno e o humano não votou | Aviso "votação acabando" (INT:586-592, 1101-1107) | não |
| Crise aberta contra o humano | Notificação só para ele (UI\NotificationsController.cs:1536-1556). Os outros veem no painel do Congresso | não |
| Humano perdeu a crise | `MandatoryLostCrisisVote` (prioridade 26): botões Cumprir e Guerra surpresa (UI\MandatoryLostCrisisVote.cs:51-82; UI\InternationalScreen_CrisisVotePanel.cs:279-297, 402-410) | **sim**, até responder |
| Lei imposta ao humano | `MandatoryCivicsShakedown` (prioridade 20) (UI\MandatoryCivicsShakedown.cs:54-70) | **sim**. Se escapar, o TurnEnd recusa e cobra |
| Suborno | Botão por nação, só antes de votar. O popup mostra quantidade, custo e peso resultante (UI\InternationalScreen_BribePopup.cs:180-220) | não |
| Humano declara crise | Botão "Crise internacional" na aba Crise, com o Congresso destravado (UI\DiplomaticCrisisPanel_CrisisGroup.cs:476-483, 504-507) | não |

- **Obrigatórios:** só travam o turno se não forem "puláveis" ou se a opção de obrigatórios estiver ligada (UI\Mandatory.cs:94-108). As prioridades estão em UI\MandatoriesController.cs:27-39.
- **Votos públicos:** o humano vê quem votou em cada lado e com que peso (UI\InternationalScreen_VoteDetail.cs:180-188, 217-226). O dossiê pode mostrar o mesmo.
- **Na hora:** as ações da IA de linguagem aparecem na tela nativa imediatamente. O ancillary marca `Frame` e `SetSynchronizationDirty` (INT:463, 470, 989, 996, 1337).
- **Sem patch de UI:** nenhuma situação do Congresso precisa, ao contrário da rendição.

---

## 6. Armadilhas

1. **Guerra surpresa pela IA nativa depois de perder a crise.** Sem a trava de §4.2, a IA nativa da nação da IA de linguagem responde ao veredito e pode declarar guerra surpresa (GENI\ResolveInternationalCrisisVote.cs:49-65).
2. **1ª sessão parada.**
   - A 1ª sessão espera o primeiro da ordem, sem prazo (INT:183-185, 206-237).
   - Se ele for uma nação da IA de linguagem e não propuser, nunca há votação de lei.
   - Solução: voltar ao nativo depois de N turnos (§7.4).
3. **Presidência de um turno só**, a partir da 2ª sessão (INT:198-204).
   - Uma resposta que chega depois do fim do turno é executada no turno seguinte e falha com WrongSessionLeader ou VoteInactive.
   - O dossiê tem de avisar no turno certo.
4. **A votação fecha na virada de turno**, e o executor só posta no TurnMain (MOD\ActionExecutor.cs:121).
   - Um voto decidido no último turno (`TurnEnd − 1`) que chegue depois do fim do turno se perde.
   - O dossiê deve recomendar votar antes do último turno.
5. **Peso congelado no voto, e suborno só antes do voto** (§2.2, §2.4).
   - Postar os subornos antes do voto.
   - Avisar que subornar e votar no mesmo turno perde o bônus de conclusão.
6. **A lei imposta se resolve sozinha no TurnEnd**, cobrando a influência (CM:1390-1418). Não travar a resposta da IA nativa sem ter a postura da IA de linguagem (§7.4).
7. **Veredito sem prazo.**
   - O perdedor pode ficar parado para sempre, enquanto o humano é forçado a responder no mesmo turno.
   - Para manter a paridade sem forçar a IA de linguagem: dar `ProposalTurns` turnos e depois voltar ao nativo (§7.4).
8. **Proposta pendente bloqueia a resposta ao veredito.**
   - `AcceptDemands` e a guerra conferem "responda antes à proposta" (BDS:192, 387).
   - Uma proposta segurada pelo ProposalHold (MOD\ProposalHold.cs) trava a resposta. O dossiê precisa dizer "responda antes à proposta".
9. **O dossiê atual engana:**
   - oferece `crise_internacional` mesmo com o Congresso travado, em guerra ou sem consulado (MOD\DossierBuilder.cs:601-602);
   - com crise pendente, ainda manda usar `responder_exigencias`, mas recusar e enrolar falham (MOD\DossierBuilder.cs:618-622);
   - o `Explain` traduz `Locked` como "travado no momento" (MOD\ActionExecutor.cs:651).
10. **Exceções no Validate** (§3.3): checar a faixa do alvo, do eixo e dos índices antes de postar.
11. **Abstenção é legítima** (regra de liberdade da IA de linguagem): não forçar voto.
    - Só conta o peso de quem vota.
    - Na própria crise, a IA nativa sempre vota em si; uma nação da IA de linguagem que não vota na própria crise tende a perder.
12. **Empates:** na crise, o empate favorece o alvo (KVI:366); na lei, não muda nada (INT:480).
13. **Nova crise com a mesma nação logo depois de outra:** dá `AlreadyInActiveCrisis` até a antiga ser liberada, no NewTurnBegin seguinte (INT:774-777, 838-850, 1072-1079).
14. **Funções que só rodam na thread do jogo** (usam RPN ou arrays estáticos):
    - `TryComputeNextCivicVoteSessionInfo`;
    - `GetStartCivicVoteFailureFlags` (array estático `workingNextSessionCanProposeCivics`, INT:39, 363);
    - `GetUnlockInternationalFailureFlags`;
    - `ComputeInfluenceCostForIdeologicalConsensusContribution` (array estático, INT:97, 1528-1533).
    - A captura do começo do turno (thread do jogo) pode chamá-las; o plano B na thread principal não. É a mesma regra da prévia de rendição (MOD\Capture\TurnCapture.cs:63-70).
15. **O histórico de crises some:** o slot é liberado depois do resultado (INT:1029-1033, 1072-1079).
    - Para notícias e memória, assinar `SimulationEvent_InternationalCrisisVoteEnded` (SIM\SimulationEvent_InternationalCrisisVoteEnded.cs:9-19).
    - As votações de lei ficam no array.
16. **Nada disso trava o TurnFinish da simulação** (DA:266-393): o mod não precisa de hold para o Congresso.

---

## 7. Plano de implementação

**Fase 1:**
- captura e seção do dossiê;
- `propor_votacao`, `votar_congresso`, `subornar`, `responder_congresso`;
- ajustes em `crise_internacional`;
- travas, com as voltas ao nativo;
- comandos de teste.

**Fase 2:**
- postura `se_perder` para a lei imposta;
- `contribuir_consenso`;
- notícias do Congresso na memória.

### 7.1 Ferramentas (linhas para MOD\Prompts.cs)

```
- propor_votacao: {"lei": "V7", "motivo": "..."} — EXECUTADA: só quando o dossiê disser "VOCÊ PRESIDE"; põe essa lei em votação no Congresso mundial. Todas as nações votam por alguns turnos; quem não estiver na opção vencedora recebe a lei e escolhe entre adotá-la ou pagar influência para recusar.
- votar_congresso: {"votacao": "lei", "opcao": "A|B|abster"} ou {"votacao": "K3", "apoiar": "E2|abster"} — EXECUTADA: seu voto pesa o seu peso no Congresso neste momento (mais os subornos já comprados). Um voto por votação, sem troca. Não votar = abster-se.
- subornar: {"votacao": "lei|K3", "nacao": "E4", "vezes": 1} — EXECUTADA: só ANTES do seu voto e com consulado. Troca alavancagem sua contra essa nação por peso extra seu nesta votação (o dossiê mostra custo e ganho). Votando só no turno seguinte, você ganha um bônus extra.
- responder_congresso: {"nacao": "E1", "resposta": "cumprir|guerra"} — EXECUTADA: só quando o dossiê mostrar "O CONGRESSO DECIDIU CONTRA VOCÊ". Cumprir entrega as exigências deles; guerra é guerra surpresa não sancionada (todas as nações que não são suas aliadas ganham alavancagem e uma reclamação contra você).
- crise_internacional: {"nacao": "E2"} — (já existe) leva ao Congresso a disputa das suas exigências contra eles. Só com o Congresso formado, consulado seu, paz ou vassalagem entre vocês, e apoio à guerra alto ou recusa deles.
(fase 2)
- votar_congresso, campo opcional "se_perder": "adotar|recusar" — o que seus governadores fazem com a lei imposta se a sua opção perder.
- contribuir_consenso: {"eixo": "I2"} — EXECUTADA: paga influência para ajudar a destravar esse eixo do consenso mundial.
```

**Códigos novos** (livres no dossiê; já em uso: E, C, A, T, G, M)
- `V#` = lei, pelo nome da definição do cívico.
- `K#` = votação de crise (índice do pool).
- `I#` = eixo do consenso.

### 7.2 Validação no parser (MOD\DecisionParser.cs, `ValidationContext` :58-97)

**Campos novos**, preenchidos pelo `AppendCongress` do dossiê:
- `PresideNow` e `ProposableLaws` (código → nome da definição);
- `LawVoteOpen` e `LawVoted`;
- `CrisisVotes` (código → pool, declarante, alvo, ativa, já votou);
- `BribeLimits` (votação + alvo → máximo de subornos agora);
- `HasConsulate`;
- `VerdictAgainstMe` (nações cujo veredito eu preciso responder);
- `CongressCrisisOpen` (crise destravada).

**Regras e mensagens**, no estilo das existentes (:383-426):
- `propor_votacao`: exige `PresideNow` e a lei em `ProposableLaws`.
  - Senão: "você não preside a sessão (preside EX)" ou "essa lei não está entre as que você pode propor".
- `votar_congresso`:
  - lei: exige `LawVoteOpen && !LawVoted` e opção A/B/abster;
  - crise: exige o código em `CrisisVotes`, a votação ativa, não ter votado, e `apoiar` igual ao declarante, ao alvo ou "abster".
- `subornar`:
  - exige `HasConsulate`, a votação aberta e ainda não ter votado nela;
  - exige a nação em `BribeLimits` e 1 ≤ vezes ≤ limite;
  - o executor ajusta para baixo, com aviso.
- `responder_congresso`: exige a nação em `VerdictAgainstMe`.
- `crise_internacional`: exige `CongressCrisisOpen`, `HasConsulate`, paz ou vassalagem, e `DemandsMade` (já existe).
  - As mensagens dizem qual falta: "o Congresso ainda não existe", "você não tem consulado", "não há crise com quem está em guerra ou aliado".
- **Ordem dentro de uma decisão:** mover `subornar` para antes de `votar_congresso` da mesma votação (o executor processa a fila em ordem, MOD\ActionExecutor.cs:126-140).

### 7.3 Executor (novo `CongressFlow.cs`, thread do jogo, só no TurnMain)

**Novos campos em `ActionIntent`** (MOD\ActionExecutor.cs:14-58)
- `CivicName`, `CivicChoice` (−1 = abster);
- `CrisisVote`, `CrisisSide`;
- `BribeTarget`, `BribeCount`;
- `LawVote` (lei ou crise);
- `IfLost` (fase 2), `ConsensusAxis` (fase 2).

**Despacho:** casos novos no `switch` de `Execute` (:151-193), chamando `CongressFlow.Propose`, `Vote`, `Bribe`, `AnswerVerdict` e `Contribute`. O mapeamento JSON → intenção fica em MOD\IaModule.cs (perto de :910-990).

**`propor_votacao`**
1. Se `IsCivicVoteUnlocked` = false → "o Congresso ainda não existe".
2. `flags = GetStartCivicVoteFailureFlags(e, nome)`. Se ≠ None → Report(`ExplainCongress(flags)` + quem preside agora).
3. Post `OrderStartCivicVote{CivicName = new StaticString(nome)}`.
4. Em Valid: reler a última votação e reportar "lei V7 em votação até o turno X (último turno para votar: X−1)".

**`votar_congresso` (lei)**
1. `TryGetActiveCivicVote`; se não houver → "não há votação de lei aberta".
2. "abster" → Report(ok, "abstenção registrada"), sem ordem. Fica na memória.
3. Montar `OrderInternationalAction{VoteForCivicChoiceIndex, CivicChoiceIndex = 0|1}`.
4. Conferir `GetInternationalActionFailureFlags(e, ordem)` e postar.
5. Reportar o peso gravado (`SwayContribution`) e o placar.

**`votar_congresso` (crise)**
1. `ref v = CrisisVoteInfos.GetReferenceAt(pool)`; exigir `PoolAllocationIndex >= 0 && IsActive()`.
2. Lado = declarante → `SideWithCrisisDeclarator`; lado = alvo → `SideWithCrisisTargeted`.
3. Conferir as flags e postar.

**`subornar`**
1. Resolver a votação. Para crise, exigir `IsActive()`, porque a validação não confere (§3.3).
2. Conferir o alvo: 0 ≤ alvo < `NumberOfMajorEmpires`, ≠ eu e vivo. Sem isso, a validação estoura exceção.
3. Ajustar n para o mínimo entre `MaximumBribeActionCount − PurchasedBribeActionCount` e piso(alavancagem ÷ `LeverageCostToBribe`).
4. Montar a ordem com `NumberOfBribeActions = n`, conferir as flags e postar.
5. Em Valid: reler `BribeInfos[alvo]`.
   - Se `PurchasedBribeActionCount` não subiu → logar "NumberOfBribeActions perdido" (o caso de §3.5).
   - Reportar "+X de peso agora, +Y no próximo turno; alavancagem contra E4: antes → depois".

**`responder_congresso`**
1. Reler a relação: status `InternationalCrisisVoteEnded`, `HasLostInternationalCrisisVote(eu)` e vencedor = o outro.
2. "cumprir" → `AcceptDemands`; "guerra" → `DeclareSurpriseWar`.
3. Conferir as flags com `GetDiplomaticActionFailureFlags` e explicar com o `Explain` existente (já cobre `AnswerPropositionFirst` e `BelowSurpriseWarMoralThreshold`).
4. Postar `OrderDiplomaticAction`.

**`crise_internacional`** (o caso padrão já existe, :184-191)
- Quando a flag for `Locked`, calcular `GetStartCrisisVoteFailureFlags(e, outro)` e explicar com `ExplainCongress`: "o Congresso ainda não existe" ou "você não tem consulado funcionando".

**`contribuir_consenso`** (fase 2): conferir a faixa do índice, conferir as flags e postar.

### 7.4 Travas e voltas ao nativo

**MOD\NativeAiLocks.cs** (nova região "Congresso"):
- os quatro prefixes de §4.2;
- `SetCongressFallback(HashSet<int> verdicts, HashSet<int> leaders)`;
- contador `blockedCongress` no `Describe()`.

**`CongressFlow.TrackFallbacks(turn)`** é chamado pela thread principal a cada segundo, dentro de `IaModule.UpdateLocks` (MOD\IaModule.cs:324-365), ao lado de `ProposalHold.TrackSurrenders`.
- **Veredito:**
  - para cada relação com status `InternationalCrisisVoteEnded`, guardar o turno em que foi visto pela primeira vez, com a chave (perdedor << 16 | vencedor);
  - se turno − visto ≥ `IaConfig.ProposalTurns` → a chave entra em `verdictFallback`, e a IA nativa responde (normalmente cumpre).
- **1ª sessão:**
  - com `IsCivicVoteUnlocked` e `CivicVoteInfos.Length == 0`, guardar o turno do desbloqueio;
  - se o presidente da foto do turno for uma nação da IA de linguagem e turno − desbloqueio ≥ `ProposalTurns` → ela entra em `leaderFallback`, e a IA nativa propõe.
- **API fora (F1):** `IsLlmActive` cai sozinho e tudo volta ao nativo. Não precisa de outra regra.

**Postura sobre a lei imposta (fase 2)**
- `IaNation.CongressStance[nomeDoCívico] = adotar|recusar`, gravada a partir de `votar_congresso.se_perder`.
- Se a nação votou na opção vencedora, a postura é adotar.
- A thread principal publica a tabela (empire << 16 | hash do cívico).
- O postfix em `DoOsmosisAction.CreateOrder()` troca a ordem quando o evento for do Congresso: mesmo `ItemName`/`CivicChoiceName` da última votação terminada.
- Sem postura (abstenção), a IA nativa decide.

### 7.5 Captura (MOD\Capture\TurnCapture.cs, novo `CaptureCongress`; thread do jogo)

**`WorldCapture.Congress`**
- `Dlc`, `Disabled`, `CivicUnlocked`, `CrisisUnlocked`, `ConsensusUnlocked`, `Unlocker`, `LeaderOrder[]`.
- `UnlockFlags[]` por nação, com os números para o texto:
  - era mínima (RPN avaliada com a nação como fonte);
  - fração conhecida vs fração exigida.
- `Sway[]`, `HasConsulate[]`.
- `Leverage[i][j]`: só as linhas das nações da IA de linguagem.
- `NextLeader`, `NextSessionTurn`, `ProposableCount`.
- `Proposable[]` do próximo presidente. Para cada lei:
  - nome da definição e nomes das duas opções;
  - quantas nações estão em cada opção;
  - a opção de cada nação da IA de linguagem.
- `LawVote`: nome, presidente, `TurnBegin`, `TurnEnd`, estado e cédulas (opção, peso, bônus de suborno). Também os subornos de cada nação da IA de linguagem (máximo, comprados, custo, bônus e flags com n = 1, via `GetBribeActionFailureFlags`).
- `LawHistory`: as últimas 3 votações terminadas (lei, vencedor, cédulas).
- `Crises[]`: pool, estado, declarante, alvo, `TurnBegin`, `TurnEnd`, cédulas, subornos das nações da IA de linguagem, se está esperando resposta e o perdedor.
- `Consensus[]` (fase 2): definição, destravado, contribuições/exigido, contribuições e custo de cada nação da IA de linguagem, `GlobalConsensus`, orientação e seção ativas.
- `PendingImposedLaws[]`: varredura do pool de osmose (`CivicsShakedown` cujo `ItemName` é o da última votação de lei terminada).

**`CaptureRelation`** (:734-801): gravar `CrisisWinner = InternationalCrisisWinnerEmpireIndex` quando o status for Ended. Para o lado perdedor, gravar também as flags de `AcceptDemands` e `DeclareSurpriseWar`.

**Plano B na thread principal:** pular as funções com RPN e arrays estáticos (§6, item 14), como já faz o `previewAllowed`.

### 7.6 Dossiê (MOD\DossierBuilder.cs)

**Ajustes**
- `AppendCongress` entra logo depois de `AppendKnownEmpires` (:56).
- Corrigir :601-602: só sugerir `crise_internacional` quando ela for possível.
- Corrigir :604-636: com crise pendente, trocar `responder_exigencias` pelo texto do Congresso.
- Mapear `ProposeInternationalCrisisCompliance` no histórico (:766-809): "o Congresso decidiu a disputa a favor de <iniciador>".
- Na linha de `declarar_guerra` do prompt, avisar da guerra não sancionada quando a crise estiver destravada.

**Congresso travado**
```
== CONGRESSO MUNDIAL ==
Ainda não existe. Forma-se sozinho no começo do turno em que alguma nação estiver na era <Moderna> ou depois, tiver o distrito Consulado funcionando e conhecer pelo menos <70%> das nações vivas. Quando ele se formar, todas as nações passam a se conhecer.
Você: era Medieval (falta a era), sem consulado, conhece 7 de 9.
```

**Congresso formado (bloco geral)**
```
== CONGRESSO MUNDIAL ==
Formado no turno 131. Peso no Congresso (o peso do voto de cada nação): você 118 (12% do total) · E4 160 · E7 140 · E1 95 · E3 75 · E2 60 · …
Seu consulado: funcionando. Sua alavancagem (gasta em subornos e ações de consulado): contra E4 6 · contra E7 2 · contra E1 0.
Guerra surpresa agora é "não sancionada": todas as nações que não são aliadas do agressor ganham alavancagem e uma reclamação contra ele.
```

**Votação de lei aberta**
```
Votação de lei em andamento (sessão 2, presidida por E4 <nome>): V7 «<lei>» — A «<opção 0>» × B «<opção 1>».
   Fecha na virada para o turno 140: o último turno para votar é o 139 (faltam 3). Empate: nada muda.
   Placar: A 210 (E4, E7) · B 95 (E1). Ainda não votaram: você (118), E2 (60), E3 (75), E5, E6, E8, E9.
   Sua lei hoje: B «<opção 1>». Se A vencer, você recebe A como lei imposta: adotar troca a sua lei; recusar custa cerca de 60 de influência.
   Votar: votar_congresso {"votacao":"lei","opcao":"A|B|abster"}. Seu peso entra como está no momento do voto.
   Subornar (antes de votar): E4 até 5 vezes, 3 de alavancagem cada, +20 de peso agora e +3 no próximo turno; E7 até 4 vezes, 3 cada, +18/+3.
```

**Presidência**
```
VOCÊ PRESIDE A SESSÃO DO CONGRESSO NESTE TURNO: escolha a lei a votar com propor_votacao. Se não propuser neste turno, a presidência passa para E9 <nome>.
   Leis que você pode propor (opção A × opção B · quantas nações usam cada uma hoje · a sua):
   - V7 «<lei>»: A «…» × B «…» — A 3, B 4, sem a lei 3 · a sua: B.
   - V12 «<lei>»: A «…» × B «…» — A 6, B 1 · a sua: A.
```
- 1ª sessão: trocar a última frase por "Esta é a primeira sessão: ela espera por você. Se você não propuser até o turno X, seus diplomatas propõem por você."
- Fora da sua vez: "Próxima sessão: no turno 145, presidida por E7 <nome> (depois: E9, você)."

**Crises**
```
Crises no Congresso:
- K3: E2 <nome> contra E5 <nome> — exigências de E2: pagar 300 Dinares; território Arneb (T146). Fecha na virada para o turno 138 (último turno para votar: 137).
   Placar: E2 150 (E2, E8) × E5 120 (E5). Empate ou falta de votos favorecem E5, o alvo. Ainda não votaram: você (118), E1, E3, …
   Se E2 vencer, E5 terá de cumprir as exigências ou declarar guerra surpresa. Se E5 vencer, as exigências de E2 caem.
   Votar: votar_congresso {"votacao":"K3","apoiar":"E2|E5|abster"}. Quem você apoiar lembra por muito tempo; o outro lado também.
- K1 (SUA CRISE): você contra E1 <nome> — suas exigências: pagar 900 Dinares. Fecha … Placar … VOCÊ AINDA NÃO VOTOU: na sua própria crise, o seu peso só conta se você votar em si.
   Enquanto a votação corre, ninguém aceita, recusa ou retira exigências entre vocês, e guerra formal está proibida.
```

**Veredito**
```
O CONGRESSO DECIDIU CONTRA VOCÊ na disputa com E1 <nome> (turno 136): cumpra as exigências deles — pagar 900 Dinares — com responder_congresso {"nacao":"E1","resposta":"cumprir"}, ou declare guerra surpresa ({"resposta":"guerra"}; guerra não sancionada: alavancagem e reclamação contra você para todas as nações que não são suas aliadas). [Guerra indisponível agora: apoio à guerra insuficiente.]
   Até você responder, a disputa fica parada (nem guerra formal, nem exigências novas entre vocês). Se não responder até o turno 138, seus diplomatas decidem.
O Congresso decidiu A SEU FAVOR contra E5 (turno 136): eles têm de cumprir as suas exigências ou declarar guerra surpresa. Esperando a resposta deles.
```

**Lei imposta e notícias** (fase 2)
```
Lei imposta pelo Congresso: V7 «<lei>» → «<opção A>». Seus governadores <adotam | recusam (custa 60 de influência)> neste turno, conforme a sua postura (se_perder).
Notícias do Congresso: turno 136, V7 «<lei>»: venceu A, 310 × 205 (A: E4, E7, E2; B: E1, E5; não votaram: E3). Turno 136, crise K3 (E2 × E5): venceu E2 (E8 apoiou E2; E5 votou sozinho).
```

**Consenso** (fase 2)
```
Consenso ideológico (o mundo destrava eixos com influência; o efeito vem da média ideológica de todas as nações):
- I0 «<eixo>»: travado — 7 de 12 contribuições; contribuir custa a você 40 de influência (você já contribuiu 1 vez). Hoje a média do mundo cai em «<orientação>», perto da sua posição.
- I2 «<eixo>»: em vigor desde o turno 120: «<orientação>».
```

### 7.7 Textos para `ExplainCongress(InternationalFailureFlags)`

**Congresso e sessões**
- `Locked`: "o Congresso ainda não existe (ou essa parte dele ainda está fechada)".
- `InternationalDisabled`: "o Congresso está desligado nesta partida".
- `WrongSessionLeader`: "você não preside esta sessão (preside EX)".
- `VoteInactive`: "não há votação aberta (a sessão ainda não abriu ou a votação já fechou)".
- `SessionHasNotStarted`: "a sessão ainda não abriu".
- `VoteActive`: "já há votação de lei em andamento".
- `NoCrisisVote` / `NoCivicVote`: "não há votação desse tipo".

**Propostas e votos**
- `WrongCivicProposal`: "essa lei não pode ser proposta por você (não é internacional, ou não está disponível para você)".
- `WrongVoteChoice`: "opção inválida: use A ou B".
- `AlreadyVoted`: "você já votou nesta votação (o voto não muda; suborno só antes de votar)".
- `NotEnoughSway`: "seu peso no Congresso é zero".

**Subornos e alvos**
- `NoConsulatBuilt`: "você não tem consulado funcionando".
- `NotEnoughLeverage`: "alavancagem insuficiente contra essa nação".
- `NotEnoughAction`: "número de subornos inválido ou acima do limite contra essa nação".
- `WrongEmpire`: "nação inválida (ou você mesmo)".
- `WrongOrder`: "votação ou alvo inválido".

**Crise, consenso e outros**
- `AlreadyInActiveCrisis`: "já há crise entre vocês no Congresso (ou a anterior ainda não foi encerrada)".
- `NotEnoughInfluence`: "influência insuficiente".
- `AlreadyUnlocked`: "já destravado".
- `NotInCorrectEra` / `NotEnoughEmpiresMet`: "era insuficiente" / "você conhece poucas nações".

### 7.8 Resultados e memória

**Report imediato** (pelo executor)
- "votou A em V7 com peso 118 (placar A 328 × B 95)".
- "2 subornos contra E4: +40 de peso agora, +6 no próximo turno; alavancagem contra E4 6 → 0".

**Desfechos depois** (thread do jogo, com fila para a thread principal, como o `ActionOutcome`)
- Assinar `SimulationEvent<SimulationEvent_InternationalCivicVoteEnded>` e `SimulationEvent<SimulationEvent_InternationalCrisisVoteEnded>` (eventos internos; o mod compila contra os assemblies públicos).
- Copiar as cédulas no próprio evento.
- Gravar na memória de cada nação da IA de linguagem:
  - "turno 136: V7 — você votou A e venceu";
  - "E4 votou contra você na crise K1".
- Guardar as últimas 10 notícias no `IaWorld` (subir a versão do save do mod).

### 7.9 Comandos de desenvolvimento

- `ia congresso` (ampliar MOD\IaCommands.cs:182-209) mostra:
  - DLC e opção da partida; flags de desbloqueio por nação e os valores de RPN;
  - quem destravou, a ordem de presidência, a próxima sessão e as leis propuníveis;
  - a votação ativa, com cédulas e subornos;
  - as crises, com estado, cédulas e se esperam resposta;
  - peso, consulado e alavancagem das nações da IA de linguagem;
  - os contadores das travas.
- `jogo congresso liberar E#`: posta ForceUnlockCivicVote (de E#), ForceUnlockCrisisVote e ForceUnlockIdeologicalConsensus. Só no TurnMain.
- `jogo congresso encerrar lei|K#`: posta ForceEndCivicVote ou ForceEndCrisisVote. Só para teste: o resultado sai na hora.
- `jogo rpn <nome>` (já existe): as fórmulas de §1.4.
- **Apoio ao teste** com ordens de editor, aceitas em partida normal (`order-catalog.md` §2.19):
  - `EditorOrderCreateExtensionDistrictAt{DistrictDefinitionName = <EmpireWideConsulatDefinition>, TileIndex = casa livre num território de cidade}`: cria o consulado e grava `Consulat` (SIM\EditorOrderProcessors.cs:702-749; SIM\DepartmentOfTheInterior.cs:3570-3574). Postar com alvo −1. Escolher uma casa sem distrito; numa casa com distrito, a ordem faz upgrade e substitui o distrito.
  - `EditorOrderAddOrRemoveLeveragePointStock{OtherEmpireIndex, PointDelta}`: postar **para o império** que ganha (o processador fica no DOFA, DOFA:1078-1096).
  - `EditorOrderUnlockCivic{EmpireIndex, CivicName}`: deixa uma lei internacional disponível para o presidente (SIM\EditorOrderProcessors.cs:2085-2109).
  - `ia acao E# {json}` (já existe): as ferramentas novas.

### 7.10 Testes no save Francos (de teste; Medieval, 10 impérios, Congresso travado)

**0. Preparação**
- Salvar ("Antes congresso T#").
- `ia congresso`: conferir DLC = sim, opção = ligada, flags por nação, consulados e alavancagem.
  - **Se o DLC ou a opção faltar, parar**: forçar criaria votações eternas (§1.1).
- `jogo rpn` das 10 fórmulas de §1.4. Anotar a era mínima e as durações.

**1. Liberar**
- `jogo congresso liberar E0` no TurnMain.
- Esperado:
  - as três flags ligadas e E0 na posição de quem destravou;
  - a ordem de presidência montada;
  - primeiros contatos para todos, e o dossiê de E0 mostrando as 9 nações;
  - o botão do Congresso aparecendo para o humano.
- Salvar e recarregar (inicializa o consenso).

**2. Proposta pela IA de linguagem**
- O dossiê de E0 mostra "VOCÊ PRESIDE".
- Esperar a decisão da nação, ou usar `ia acao E0 {"acao":"propor_votacao","lei":"V#"}`.
- Esperado:
  - votação ativa com `TurnEnd` certo;
  - notificação para o humano;
  - a IA nativa de E0 não propôs (contador).
- Se ninguém tiver lei propunível na Medieval: `EditorOrderUnlockCivic` para E0 numa lei internacional e repetir.

**3. Votos**
- Deixar as nações votarem; o humano vota pela tela.
- Conferir no `ia congresso`:
  - cédulas e pesos;
  - nenhuma nação da IA de linguagem votou pela IA nativa (contadores);
  - as nações nativas votam no ritmo delas;
  - não há "votar" depois de "já votou".

**4. Abstenção e atraso**
- Uma nação da IA de linguagem que não vota não recebe voto da IA nativa.
- Resposta atrasada (depois do fim do turno) falha com explicação clara.

**5. Suborno**
- Nação da IA de linguagem com consulado e alavancagem; criar os dois com as ordens de editor de §7.9, se faltar.
- `ia acao E# {"acao":"subornar","votacao":"lei","nacao":"E4","vezes":1}`.
- Esperado:
  - `PurchasedBribeActionCount` = 1, o que prova que o campo chega no solo (§3.5);
  - alavancagem reduzida;
  - o bônus de conclusão aparece no turno seguinte.
- Votar no turno seguinte e conferir `SwayContribution`.

**6. Resultado**
- Usar `jogo congresso encerrar lei` ou passar até `TurnEnd`.
- Conferir:
  - quem recebeu lei imposta;
  - o humano com o obrigatório (testar aceitar e recusar);
  - nas nações da IA de linguagem, a IA nativa responde (fase 1) ou a postura `se_perder` é seguida (fase 2);
  - nenhuma lei imposta sobra para o TurnEnd.

**7. Rodízio**
- Na sessão seguinte, se o presidente for uma nação da IA de linguagem que não propõe, no turno seguinte a presidência passa (conferir `NextLeader`).
- Na 1ª sessão: com E0 calado por `ProposalTurns` turnos, a IA nativa propõe (volta ao nativo).

**8. Crise**
- Escolher um par em paz em que uma nação da IA de linguagem tenha exigências e consulado (criar com `exigir`; se for preciso, recusa do outro).
- `crise_internacional` → K#; votos; `jogo congresso encerrar K#`.
- **Perdedor da IA de linguagem:**
  - o dossiê mostra "O CONGRESSO DECIDIU CONTRA VOCÊ";
  - a IA nativa dele **não** responde nem declara guerra (contador);
  - testar `responder_congresso` cumprir e guerra;
  - sem resposta por `ProposalTurns` turnos, a IA nativa cumpre.
- **Humano perdedor:** o obrigatório aparece e trava o fim do turno até responder.
- **Mensagens:** com o Congresso travado, sem consulado e em guerra, conferir que `crise_internacional` explica certo.

**9. API fora**
- Derrubar a chave: as travas caem (F1), e a IA nativa vota, propõe e responde.

**10. Guerra não sancionada**
- Depois do Congresso, uma guerra surpresa de qualquer nação gera reclamação e alavancagem para as outras.
- O dossiê avisa isso antes de `declarar_guerra`.

**A confirmar no teste** (não dá para ver no código)
- os valores de RPN;
- se há lei internacional disponível na Medieval;
- se os obrigatórios do Congresso são "puláveis" (`MandatoryUIMapper.Skippable`, nos dados);
- `NumberOfBribeActions` no solo (passo 5).
