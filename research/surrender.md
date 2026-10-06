# Rendição de guerra — mapa completo para oferecer, impor e responder (pesquisa 2026-10-04)

Pesquisa só de leitura do código descompilado. Nenhum arquivo do jogo ou do mod foi alterado.
Complementa `crisis-negotiation.md` (§Rendição), `order-catalog.md` (§2.1, §2.2) e `diplomacy-native-timers.md` (§5).

**Notação** (caminhos relativos a `_Modding\decompiled\`)
- SIM = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\` · IO = `...Firstpass\Amplitude.Mercury.Interop\` · IOAI = `...Firstpass\Amplitude.Mercury.Interop.AI\` (e `.Data\`) · SBX = `...Firstpass\Amplitude.Mercury.Sandbox\`
- DA = SIM\DiplomaticAncillary.cs · DRH = SIM\DiplomaticRelationHelper.cs · BDS = SIM\BaseDiplomaticState.cs · DOFA = SIM\DepartmentOfForeignAffairs.cs · DR = SIM\DiplomaticRelation.cs · DSW = SIM\DiplomaticState_War.cs · AMB = SIM\DiplomaticAmbassy.cs
- GEN = `Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.Generators.Diplomacy\` · AIA = `...AI.Brain.Actuators\` · AIT = `...AI.Brain.Tasks\` · AIM = `...AI.Brain.Analysis.Military\` · AID = `...AI.Brain.Analysis.Diplomacy\`
- UI = `Assembly-CSharp\Amplitude.Mercury.UI\` · MOD = `_Modding\src\CurrencyMod\Diplomacia\`
- "Vencedor" e "perdedor" = `WinnerEmpire` / `LoserEmpire` da proposta. O jogo **não** confere quem está ganhando de verdade: qualquer lado em guerra pode se oferecer em rendição ao outro.

---

## 0. Resumo

- **Onde vive:** cada rendição é uma `SurrenderProposition` num pool global (DA:63). Há três "vagas" por guerra:
  - rascunho de oferta do perdedor (`CurrentSurrenderToOtherProposition`);
  - rascunho forçado do vencedor (`ForcedOtherToSurrenderProposition`);
  - proposta enviada (`DiplomaticState_War.ProposedSurrenderIndex`, uma só por relação).
- **Orçamento = placar de guerra do vencedor**, congelado quando o rascunho é aberto (DA:1383).
  - Custo dos termos: exigência 10, território 25, vassalagem variável (RPN `SubmissionCost`), ouro 1 ponto por parcela.
  - O total tem de ficar entre placar − 5 e placar (BDS:139-161).
  - O ouro preenche a sobra sozinho (DA:1614-1631): na prática, **o que sobra vira ouro**.
- **Exigências obrigatórias:** todas as exigências do vencedor entram sem escolha quando placar ≥ custo das exigências. É o caso normal, porque cada exigência também soma 10 ao placar (DR:435-477).
- **Oferecer (perdedor):** StartToFill → termos → ProposeToSurrender. Quem responde é o **vencedor**.
- **Impor (vencedor):** só com o apoio à guerra do outro em 0 e o seu acima de 0 (BDS:444-447). AllowToForce → termos → DeclareSurrender. Quem responde é o **perdedor** (BDS:279-286).
  - **Nada é imposto sem resposta.** A IA nativa perdedora sempre aceita (GEN\EndSurrender.cs:100).
- **Prazo:** não há prazo em turnos (DA:821 nunca dispara). Mas uma proposta enviada segura o fim do turno:
  - com cerca de 16 s de impaciência o jogo **recusa sozinho** (DA:333-338);
  - com cerca de 24 s, cancela (DA:327-332).
- **Aceitar:**
  - aplica as exigências, transfere os territórios, cobra o ouro e aplica a vassalagem;
  - a guerra acaba (paz ou vassalagem), e o apoio à guerra dos dois vai a 0 (DA:1496-1603; DRH:427-441);
  - cidades ocupadas que não entraram nos termos **voltam ao dono** (DSW:255-330).
- **Travas recomendadas** (thread da IA):
  - prefix em `Surrender.GetParents`, `ForceSurrender.GetParents` e `FillSurrenderTerms.GetContexts`;
  - postfix em `EndSurrender.TryComputeAction`, por relação, com volta ao nativo.
- **Armadilhas principais:**
  1. O humano que impõe rendição a uma nação da IA de linguagem fica preso no popup obrigatório até ela responder. Isso é um bug do jogo (IO\DiplomaticEmpireSummary.cs:175-178) e pede patch de UI.
  2. Rascunho abandonado bloqueia paz branca e rendição; o forçado bloqueia os dois lados. Nunca expira: sempre cancelar.
  3. `OrderChangeSurrenderDemandState` não confere a faixa do índice. Uma exceção no Validate deixa o ticket pendurado.
  4. As ordens de termo são rejeitadas no TurnFinish (SBX\Sandbox.cs:1703).

---

## 1. Modelo de dados

### 1.1 Onde vive

| O quê | Onde | Observação |
|---|---|---|
| Pool de propostas | `DiplomaticAncillary.SurrenderAllocator` (DA:63), `PoolAllocator<SurrenderProposition>` | Slot livre tem `PoolAllocationIndex < 0`. `AllocateRef` **não zera** os campos (Firstpass\Amplitude.Mercury\PoolAllocator.cs:81-94) |
| Rascunho de oferta | `AMB.CurrentSurrenderToOtherProposition` (AMB:65), na embaixada de quem se rende (DRH:379-381) | EmpireToFill = perdedor |
| Rascunho forçado | `AMB.ForcedOtherToSurrenderProposition` (AMB:67), na embaixada do vencedor (DRH:398) | EmpireToFill = vencedor |
| Proposta enviada | `DiplomaticState_War.ProposedSurrenderIndex` (DSW:20); `DR.HasPendingSurrenderProposition` (DR:366-373) | Só existe em Guerra; uma por relação |
| Save | pool (DA:1312), campos da embaixada (AMB:375-376), índice da guerra (DSW:46) | `ProposedAtTurn` **não** é salvo (IO\SurrenderProposition.cs:100-126) |

### 1.2 `SurrenderProposition` (IO\SurrenderProposition.cs:16-50)

| Campo | Sentido |
|---|---|
| WinnerEmpire / LoserEmpire | Quem recebe / quem cede |
| EmpireToFill | Quem escolhe os termos: perdedor na oferta, vencedor na forçada. Conferido em DOFA:2590 |
| CurrentPropositionStatus | None (rascunho) → Proposed → Signed. Recusa e cancelamento liberam o slot na hora |
| IsSurrenderForced | Só vale depois de DeclareSurrender (DA:1610). Num rascunho pode ser lixo de uso anterior do slot |
| ProposedAtTurn | Turno do envio (DA:1609); não é salvo |
| WinnerWarScore | Orçamento, copiado na abertura (DA:1383). **Nunca recalculado** |
| OverallDemandCost | 10 × número de exigências do vencedor (DA:1862) |
| IncludedDemands[] | Uma `SurrenderDemandInfo` por exigência em aberto do vencedor, na ordem de `OnGoingDemands` (DA:1848-1861). Detalhes abaixo da tabela |
| IncludedDemandCost | Soma das exigências incluídas |
| SurrenderTerritories[] | Uma `SurrenderTerritoryInfo` por território do mundo, com índice = TerritoryIndex (IO\SurrenderTerritoryInfo.cs:8-18). Detalhes abaixo da tabela |
| IncludedTerritoriesCost | Soma dos territórios incluídos que não vêm de exigência (DA:1809-1824) |
| MoneyRetribution | IsIncluded (padrão true), NumberOfRetribution (parcelas), MoneyGainPerRetribution (ouro por parcela), WarMoralCostPerRetribution (= 1) (IO\SurrenderMoneyRetribution.cs:7-17) |
| SubmissionDemand | Included, FailureFlags, WarCost (IO\SurrenderSubmissionDemand.cs:7-11) |
| `MustIncludeDemands` | WinnerWarScore ≥ OverallDemandCost (:64) |
| `ComputeIncludedTermsCost()` | Exigências + territórios + vassalagem, **sem** o ouro (:66-74) |

**Campos de `SurrenderDemandInfo`:**
- `DemandIndex`: índice no `DemandsAllocator` do vencedor.
- `Included`.
- `WarCostPerGrievance` = 10.
- `NumberOfGrievances`: sempre 1 (DOFA:530).
- `DemandGainType` / `DemandGain`.
- `IsObsolete` e `MoneyGainedForObsolescence`.

**Campos de `SurrenderTerritoryInfo`:** `Included`, `WarCost` = 25, `FailureFlags`, `Connectivity`, `TargetedByDemandCount`.

### 1.3 Termos e custo (em pontos de placar)

**Exigência do vencedor** (dinheiro, território, cívico, religião ou ação diplomática)
- **Custo:** 10 cada (DA:1860).
- **Regra para entrar:**
  - todas entram por padrão (DA:1859);
  - só dá para tirar ou pôr quando placar ≤ custo das exigências (DOFA:1135-1138).
- **No aceite:** `DiplomaticGrievanceHelper.TryToApply` (DA:1527; SIM\DiplomaticGrievanceHelper.cs:505-516). Se não der para entregar, vira ouro (`ApplyObsoleteGain`, :684-691).

**Território do perdedor ou de vassalo dele**
- **Custo:** 25 cada (DA:1682).
- **Regra para entrar** (o orçamento **não** é checado na ordem):
  - placar > custo das exigências (DOFA:1230);
  - `FailureFlags` = None (DOFA:1235);
  - `Connectivity` ≠ None (DOFA:1243).
- **No aceite**, por `WrapTerritoryGain` (SIM\DiplomaticGrievanceHelper.cs:518-571):
  - centro de cidade leva a cidade; os anexos não pedidos são destacados e ficam com o perdedor como postos;
  - anexo vira posto novo do vencedor;
  - posto muda de dono.

**Vassalagem (submissão)**
- **Custo:** RPN `SubmissionCost`(perdedor, vencedor, [territórios-cidade dos vassalos do perdedor]) mais os modificadores de custo de `ForceOtherIntoSubmission` (DA:1875-1903).
  - Custa **0**, e já vem incluída (DA:1868), se o perdedor tem reclamação ou exigência AbandonedUs, CivilWar ou VassalWantsFreedom (DA:1878-1895).
- **Regra para entrar** (o orçamento não é checado na ordem):
  - placar > custo das exigências;
  - `FailureFlags` = None (DOFA:1195-1202). As falhas possíveis (DA:1633-1650):
    - `NotAvailable`: o vencedor não tem a ação ForceOtherIntoSubmission, ou a opção de partida "vassalagem" está desligada (SIM\DepartmentOfDevelopment.cs:1318);
    - `HasALiege`: o perdedor já é vassalo de alguém;
    - `NotEnoughToFulfillDemands`.
- **No aceite:** VassalToLiege com o vencedor de suserano; cancela as votações de crise do perdedor (DA:1556-1564).

**Ouro (reparação)**
- **Custo:** 1 ponto por parcela. Uma parcela vale 5 × `SurrenderMoneyMultiplier` × `EraLevel` **do vencedor** (DA:1870-1871).
- **Regra para entrar:**
  - incluído por padrão, e preenche a sobra sozinho: parcelas = piso(placar − exigências − territórios − vassalagem) (DA:1614-1631);
  - dá para tirar com `OrderChangeSurrenderMoneyState` (DOFA:1158-1174), mas a UI do jogo não tem botão para isso (UI\ISurrenderTermItemClient.cs:5-18).
- **No aceite:** o perdedor faz `GainMoney(-x)` (pode ficar negativo) e o vencedor recebe (DA:1549-1554). O mod de moedas converte essa transferência (`src\CurrencyMod\TransferConversion.cs:39`).

**Cívico e religião**
- Não existem como termo próprio. Só entram como exigência já existente (ForceCivic / ForceReligion), com as regras de exigência.

**Validação do total** (`CheckWarScoreForSurrender`, BDS:139-161)
- Custo = exigências + territórios + parcelas de ouro × 1 + vassalagem.
- Custo > placar → `NotEnoughWarScore`.
- Placar − custo > 5 → `BetterPropositionPossible` (o vencedor teria direito a mais).
- Só é checada no ProposeToSurrender e no DeclareSurrender (BDS:299, 316). As ordens de termo não olham orçamento.

**Regimes**
1. **Normal: placar > custo das exigências.**
   - As exigências ficam fixas.
   - A sobra vai para territórios, vassalagem ou ouro (automático).
   - Com o ouro incluído e nada estourando, a proposta é sempre válida.
2. **Placar ≤ custo das exigências.** Só acontece com `WarScoreGlobalMultiplier` < 1.
   - Territórios, vassalagem e ouro ficam bloqueados (`NotEnoughToFulfillDemands`: DA:1638-1641, 1676-1679; DOFA:1169, 1195, 1230).
   - Escolhe-se um subconjunto das exigências.
   - Trocar exigência não recalcula o ouro (`ChangeDemandIncluded` não chama `UpdateMoneyRetribution`, DA:1436-1455).
   - Então placar − exigências escolhidas tem de dar ≤ 5 só com exigências, de 10 em 10. Às vezes é impossível.
3. **Placar = custo das exigências** (nenhuma cidade tomada, nenhum bônus).
   - Só as exigências entram; nada mais muda.
   - DOFA:1135 deixa trocar exigência, mas a UI não deixa (UI\DiplomaticCrisisPanel_SurrenderPanel_DemandTermsGroup.cs:25).
4. **Sem exigências e sem cidades tomadas:** placar 0. A "rendição" fica vazia e funciona como uma paz branca que o outro aceita ou recusa.

### 1.4 Placar de guerra (o orçamento)

**Fórmula** (`DR.GetWarScoreFor(vencedor)`, DR:435-477):
- (exigências do vencedor × 10 + territórios das cidades originalmente do perdedor hoje capturadas pelo vencedor × 25 + `WarScoreWhitePeaceBonus` + bônus de debug) × `WarScoreGlobalMultiplier`.
- O multiplicador padrão é 1 (AMB:307-308).

**Consequências práticas**
- Ocupar uma cidade soma 25 por território dela, exatamente o preço de pedir esses territórios.
- `WarScoreWhitePeaceBonus` vem da paz branca anterior: 33% do placar, ou 60% para Belicista ou Expansionista (AMB:310-317; DRH:532-535, 1198; DSW:184-187). A rendição não guarda placar.
- O placar só existe em Guerra (DR:437-440).
- O mod já captura os dois placares (MOD\Capture\TurnCapture.cs:751-755).

### 1.5 Apoio à guerra (moral) e guerra total

**Apoio à guerra**
- É `AMB.EmpireMoral.Moral` (0 a 100), atualizado no TurnEnd (DA:745-804).
- Ganhos "instantâneos", como o de uma recusa, só entram no fim do turno (SIM\DiplomaticMoral.cs:84-96; SIM\DiplomaticMoralHelper.cs:29-40).

**Guerra total**
- No NewTurnBegin, se um lado está em 0 ou menos e não há rascunho forçado aberto, a guerra vira total (DA:581-603; DSW:134-158).
- Não volta atrás.

**Penalidade de quem não impõe**
- Com o perdedor em 0, o vencedor acumula `AllOutWarNumberOfTurnLoserAt0WarSupportAndForceSurrenderAvailable` (DA:605-652).
- A cada 5 turnos ele recebe um lembrete da penalidade de estabilidade (DA:654-701).
- A fórmula de `AllOutWarStabilityPenalty` está nos dados (não descompilada); o valor atual fica na embaixada (AMB:300).
- A IA nativa usa esse valor para decidir encerrar a guerra (AIM\MilitaryStrategy.cs:109-112).

**Recusar uma rendição**
- Fora da guerra total, ninguém ganha nada.
- **Em guerra total**, quem **propôs** ganha +10 (`InstantOtherRefusedOurSurrenderMoralGain`: AMB:213-214; SIM\DiplomaticMoralHelper.cs:531-536). Quem recusa ganha 0 (:495-500).
- O contador `AllOutWarNumberOfSurrenderProposalRefused` do autor da recusa sobe só na forçada em guerra total (DRH:449-465).

**Aceitar:** o apoio dos dois vai a 0 (DRH:431-432; SIM\DiplomaticMoral.cs:98-103). Uma nova guerra formal fica distante, porque precisa de 80.

**Exigência de apoio em cada passo**

| Passo | O que o apoio à guerra precisa |
|---|---|
| StartToFill (oferecer) | Quem oferece não pode estar em 0 **fora** de guerra total (`NoMoreMoral`, BDS:263-266). Em 0, normalmente a guerra já é total |
| AllowToForce (impor) | Seu apoio > 0 **e** o deles = 0 (`WrongMoralForceOtherToSurrender`, BDS:444-447). Não exige guerra total |
| Propose / Declare / Accept / Refuse | Só a checagem geral `NoMoreMoral` (BDS:183-187), e o ganho dessas ações é 0 |

### 1.6 Como ler

**Simulação** (thread do jogo: a captura do mod já roda lá, MOD\Capture\TurnCapture.cs:16-19)
- Relação: `relation = Sandbox.DiplomaticAncillary.GetRelationFor(a, b)`.
- Proposta enviada: `(relation.DiplomaticState as DiplomaticState_War)?.ProposedSurrenderIndex`.
- Rascunhos: `relation.GetEmpireEmbassy(x).CurrentSurrenderToOtherProposition` e `.ForcedOtherToSurrenderProposition`.
- Conteúdo: `ref var p = ref Sandbox.DiplomaticAncillary.SurrenderAllocator.GetReferenceAt(i)`.
- Copie para um DTO: os arrays são mexidos no lugar.
- Na thread principal funciona para índices e flags. A leitura pode sair "rasgada", o que é aceitável para o dossiê.

**Saber se um rascunho é forçado:** olhe em qual campo da embaixada ele está, nunca `IsSurrenderForced`.

**Snapshot da IA** (`Amplitude.Mercury.Interop.AI.Snapshots.Surrender.SurrenderPropositions`: IOAI\Snapshots.cs:29; IOAI\SurrenderSnapshot.cs:13-33)
- Copia só os slots ocupados e **não limpa os liberados**: ficam slots fantasmas com índice ≥ 0.
- Acesse só pelos índices da embaixada da IA:
  - `SurrenderToOtherPropositionToFill`, `ForcedOtherToSurrenderPropositionToFill`, `CurrentSurrenderPropositionProposed` (IOAI.Data\DiplomaticEmbassy.cs:79-83, 238-240);
  - auxiliares `GetSurrenderDetailsToFill` e `GetSurrenderPropositionToFill` (:183-225).
- `CopyTo` não copia `ProposedAtTurn` (IO\SurrenderProposition.cs:76-98).
- As falhas de cada ação já vêm em `AvailableDiplomaticActions[ação].DiplomaticActionFailureFlags` e o custo em `.MyEmpireInfluenceCost` (IOAI.Data\DiplomaticEmbassy.cs:47, 247-258).

**Apresentação** (só do humano local)
- `Snapshots.DiplomaticCursorSnapshot.PresentationData.RelationDetails.SurrenderProposition` (IO\DiplomaticRelationDetailInfo.cs:272-287).
  - Mostra o maior índice entre a proposta enviada e os rascunhos do próprio humano.
  - Não mostra o rascunho do outro lado.
- `DiplomaticSurrenderSnapshot` (IO\DiplomaticSurrenderSnapshot.cs:30-63) só é ligado pela janela de debug e não copia `IsSurrenderForced`.

**Prévia exata sem abrir rascunho** (recomendada; thread do jogo)
1. Monte uma `SurrenderProposition` local com WinnerEmpire, LoserEmpire e WinnerWarScore = `GetWarScoreFor(vencedor)`.
2. Chame o método privado `DiplomaticAncillary.InitializeSurrenderProposition(ref p, relation)` (DA:1827-1873) via AccessTools.
3. Ele só mexe na struct passada e devolve o rascunho que o jogo criaria: exigências, territórios com FailureFlags e Connectivity, custo da vassalagem e ouro.
4. Ele usa RPN e a fila estática `territoryToCheck` (DA:69): **rode só na thread do jogo**.
5. Para simular um território incluído: `Included = true`, depois `ResetConnectivity(ref p)` (DA:1770) e `UpdateMoneyRetribution(ref p)` (DA:1614). Os dois são privados estáticos.

---

## 2. Fluxos e ordens

### 2.1 Pré-condições de cada ação

Fontes: BDS:163-582 e DRH:100-237, todas consultadas por `DOFA.GetDiplomaticActionFailureFlags` (DOFA:1330-1356).

**Comum a todas:**
- estado Guerra (`DSW.IsAvailable`, DSW:68-92); em outro estado = `WrongDiplomaticAction` (ex.: SIM\DiplomaticState_Peace.cs:120-137);
- ação habilitada no cenário;
- momento do turno certo (BDS:682-726);
- influência suficiente;
- o outro império vivo.

| Ação (valor) | Quem posta | Checagens próprias | Fases |
|---|---|---|---|
| StartToFillSurrenderProposition (30) | perdedor | Sem proposta pendente (`WrongOnGoingSurrenderState`); sem rascunho seu (`WrongSurrenderToOtherState`); apoio (§1.5); nenhum rascunho forçado de nenhum lado (`WrongForceOtherToSurrenderState`); "tratado limpo" — BDS:253-268 | Begin/Main/Finish |
| ProposeToSurrender (32) | perdedor | Sem pendente; nenhum rascunho forçado; ter rascunho (`WrongSurrenderToOtherState`); `CheckWarScoreForSurrender`; tratado limpo; influência — BDS:302-318 | Begin/Main |
| CancelSurrenderProposition (31) | dono do rascunho | Ter rascunho, de qualquer tipo — BDS:247-252 | Begin/Main/Finish |
| AllowToForceOtherToSurrender (37) | vencedor | Sem rascunho forçado seu; Guerra (`NotAtWar`); apoio (§1.5); sem pendente (`WrongForceOtherToSurrenderState`); tratado limpo — BDS:434-456 | Begin/Main/Finish |
| DeclareSurrender (3) | vencedor | Sem pendente; ter rascunho forçado (senão `WrongDiplomaticAction`); `CheckWarScoreForSurrender`; tratado limpo — BDS:293-301 | Begin/Main |
| AcceptSurrender (34) / RefuseSurrender (33) | Forçada: **perdedor**. Oferta: **vencedor** (senão `WrongEmpire`) | Pendente existe (`WrongOnGoingSurrenderState`); status Proposed ou Countered — BDS:269-292 | Begin/Main/Finish |
| AllowToForceOtherToSurrenderToAlly (38) | só o sistema | Postada como ordem, loga erro (DOFA:1363-1365) | — |

**"Tratado limpo"** (DRH:169-216, igual para todas as ações de rendição; lê direto da relação):
- Proposta de tratado em None:
  - Proposed ou Countered → `AnswerPropositionFirst` + `WrongTreatyStatus`;
  - Refused → `TreatyRefusedByYou` ou `TreatyRefusedByOther`;
  - Insulted → `Insulted`.
- Proposta de acordo em None (Refused → `TreatyRefused*`).
- Proposta de consulado em None.
- Influência para pagar a ação.

**Influência de ProposeToSurrender**
- Só custa durante a recarga (DOFA:2506-2523).
- A recarga é de 5 turnos (AMB:273-274). Começa:
  - a cada ProposeToSurrender (DRH:377);
  - para quem declarou a guerra, no início dela (DSW:181).
- Paga na execução (DRH:279-288).
- A IA nativa só oferece com custo 0 (GEN\Surrender.cs:26).

### 2.2 Ordens de termos

Fontes: DOFA:1123-1256; campos em IO\OrderChangeSurrender*.cs.

**Comum às quatro** (`ValidateOrderChangeSurrender`, DOFA:2569-2595):
- `OtherEmpireIndex` válido (senão LogError);
- o império da ordem tem rascunho (o de oferta; se não houver, o forçado);
- ele é o EmpireToFill.
- O Process age sobre o mesmo rascunho (DOFA:1150-1256).

**OrderChangeSurrenderDemandState** (DOFA:1123-1148)
- **Campos:**
  - `SurrenderDemandIndex`: posição em `IncludedDemands`;
  - `Included`;
  - `GrievanceType`: só conferido ≠ None e ≠ Count (senão LogError).
- **Validação extra:**
  - placar ≤ custo das exigências;
  - `DemandIndex` ≥ 0;
  - `Included` muda.
- **Não confere** o status nem a faixa do índice: índice inválido = exceção no Validate.
- **Efeito:** `ChangeDemandIncluded` (DA:1436-1455).

**OrderChangeSurrenderMoneyState** (DOFA:1158-1174)
- **Campo:** `Include`.
- **Validação extra:** status None; placar > custo das exigências.
- **Efeito:** inclui ou tira o ouro e recalcula as parcelas (DA:1478-1485).

**OrderChangeSurrenderSubmissionState** (DOFA:1184-1204)
- **Campo:** `Included`.
- **Validação extra:** status None; placar > exigências; FailureFlags None. Não confere se o valor muda.
- **Efeito:** inclui ou tira a vassalagem e recalcula o ouro (DA:1487-1494).

**OrderChangeSurrenderTerritoryState** (DOFA:1214-1248)
- **Campos:** `TerritoryIndex` (LogError se fora da faixa); `Included`.
- **Validação extra:** status None; placar > exigências; FailureFlags None; `Included` muda; `Connectivity` ≠ None.
- **Efeito:** `ChangeTerritoryIncluded`, depois `ResetConnectivity`, depois recalcula o ouro (DA:1457-1476).

**Conectividade** (DA:1665-1825; IO\SurrenderTerritoryConnectivity.cs)
- **`NearBorder`:** vizinho de um território com assentamento do vencedor que não foi capturado (`IsNearMyBorder`, DA:1652-1663).
- **`OccupiedCity`:** centro de cidade do perdedor que o vencedor ocupa agora (DA:1708-1719).
- **`NearIncludedTerritory`:** vizinho de território já incluído. É recalculado a cada inclusão (DA:1787-1808). Ao tirar um território, os que dependiam dele saem também (DA:1809-1824).
- **`LinkedToDemand`:** alvo de exigência. Entra com a exigência; não se marca à parte (DA:1755-1767).
- **`None`:** não encosta em nada; não pode ser incluído.

**Falhas de território** (DA:1676-1753; IO\SurrenderTerritoryFailureFlags.cs:8-17)
- `CityCenterNotOccupied`: centro de cidade do perdedor **não ocupada** pelo vencedor (DA:1736-1741). Só se leva a cidade inteira se ela estiver ocupada.
- `CityBeingBesieged` / `ContainsBoroughBeingBesieged`: cidade ou bairro principal sitiado.
- Dono errado: `WrongOwner`, `OccupiedBySomeoneElse`, `NotClaimed`.
- `NotEnoughToFulfillDemands`: o placar não sobra depois das exigências.
- `LinkedToDemand`: já está numa exigência.

**A ordem das inclusões importa:** primeiro os territórios de borda, depois os vizinhos (BFS). As ordens são validadas e processadas em sequência, cada uma logo depois da anterior (`order-catalog.md` §1); então postar em ordem BFS funciona.

### 2.3 (a) O perdedor oferece

1. **Abrir o rascunho:** `OrderDiplomaticAction{OtherEmpireIndex = vencedor, DiplomaticAction = StartToFillSurrenderProposition}` (DRH:379-381; DA:1375-1388).
   - Winner = o outro, Loser = você, EmpireToFill = você.
   - O placar fica congelado.
   - Vêm todas as exigências e o ouro; a vassalagem só se custar 0.
   - Não avisa o outro (DOFA:1367-1371) e não entra no histórico (DR:484-487).
2. **Ajustar os termos** (§2.2).
3. **Enviar:** `ProposeToSurrender` (DRH:369-378; DA:1605-1612).
   - Status Proposed, `ProposedAtTurn` preenchido, `IsSurrenderForced` = false, grava `ProposedSurrenderIndex`.
   - Libera o rascunho de oferta que o outro lado tiver.
   - Inicia a recarga.
   - Avisa o vencedor com `DiplomaticReactionNotificationData` (DOFA:1403, 1423-1428).
   - Não acorda a IA do outro, ao contrário do Declare.
4. **Termos inválidos:**
   - o Propose falha com `NotEnoughWarScore` ou `BetterPropositionPossible`, e **o rascunho continua aberto**;
   - ninguém o limpa: use `CancelSurrenderProposition` (DRH:468-475);
   - a UI cancela sozinha quando o humano sai do painel (UI\DiplomaticCrisisPanel_SurrenderPanel.cs:139-153).
5. **Enquanto o rascunho existe:** você não propõe paz branca (`WrongSurrenderToOtherState`, BDS:329-332) e não abre outro.
6. **O vencedor pode atropelar:** AllowToForce apaga o seu rascunho (DRH:393-397).

### 2.4 (b) O vencedor impõe

1. **Abrir o rascunho forçado:** `AllowToForceOtherToSurrender`, só com o outro em 0 e você acima de 0.
   - Apaga os rascunhos de oferta dos dois lados (DRH:383-397).
   - Cria o rascunho forçado com EmpireToFill = você (DRH:398).
   - **Avisa o perdedor** (DOFA:1407, 1423-1428).
2. **Ajustar os termos** (§2.2).
3. **Enviar:** `DeclareSurrender`.
   - Status Proposed, `IsSurrenderForced` = true.
   - Acorda a IA do perdedor (DRH:421-426) e o avisa.
4. **Há resposta:** o **perdedor** aceita ou recusa (BDS:279-282); não é imposição imediata.
   - A IA nativa perdedora sempre aceita (GEN\EndSurrender.cs:100).
   - O humano decide no painel.
5. **Rascunho forçado aberto, antes do Declare:**
   - bloqueia, para os **dois** lados, StartToFill, ProposeToSurrender (BDS:267, 308-315) e paz branca (BDS:325-328);
   - impede a guerra de virar total (DA:593);
   - atravessa os turnos (DA:821 nunca dispara).
6. **Humano vencedor com rascunho forçado:** popup obrigatório até declarar ou cancelar (`IsForcedSurrenderPending`: IO\DiplomaticEmpireSummary.cs:183-190; UI\MandatoryDiplomaticPropositionPending.cs:119-122).

### 2.5 (c) Respostas

**Quem e quando**
- Quem responde: ver §2.1 (forçada → perdedor; oferta → vencedor).
- Quando: TurnBegin, TurnMain ou TurnFinish (BDS:704-705). A `OrderDiplomaticAction` é aceita no TurnFinish (SBX\Sandbox.cs:1703); as ordens de termo não.

**Prazo**
- Não há prazo em turnos.
- O jogo não força a IA a responder durante o turno.
- No TurnFinish, a proposta pendente segura o fim do turno (DA:323-339), e a impaciência resolve (SIM\DepartmentOfCommunication.cs:671-690; SBX\SandboxState_TurnFinish.cs:9, 97-120):
  - **cerca de 8 s:** nada;
  - **cerca de 16 s (Medium):** `ExecuteAction(vencedor, perdedor, RefuseSurrender)` (DA:333-338);
  - **cerca de 24 s (High):** cancela sem registro e loga erro (DA:327-332; DepartmentOfCommunication.cs:682-685).
- Humano preso em popup obrigatório zera a impaciência (SandboxState_TurnFinish.cs:103-109).

**Peculiaridade da recusa automática:** o autor é sempre o **vencedor**, até na forçada.
- Numa forçada recusada sozinha, o +10 da guerra total vai para o **perdedor**.
- O contador de recusas sobe no vencedor (DRH:449-465; DiplomaticMoralHelper.cs:732-735).
- Uma `RefuseSurrender` de forçada com autor = vencedor só pode vir da impaciência: dá para reconhecer.

**RefuseSurrender** (DRH:442-467)
- Libera a proposta.
- Marca a proposta de **tratado** como Refused (DRH:448). Até o próximo turno, ninguém na relação faz StartToFill, Propose, AllowToForce, Declare ou paz branca (`TreatyRefused*`).
- Volta ao normal no NewTurnBegin (DA:547-561).

**AcceptSurrender** (DA:1496-1603; DRH:427-441)
1. Status Signed e evento `SimulationEvent_SurrenderSigning` (DA:1504). Terceiros recebem reclamações:
   - aliados do vencedor ainda em guerra com o perdedor ganham `SurrenderedToOurEnemy` contra o perdedor (SIM\DiplomaticGrievanceSpawner_SurrenderedToOurEnemy.cs:37-55);
   - terceiros que tinham exigência própria sobre um território que também está nas exigências do vencedor ganham `StoleMySpoils` contra o vencedor (…StoleMySpoils.cs:73-129);
   - com vassalagem, quem ocupa cidades do perdedor ganha `ThwartedOurOccupation` (…ThwartedOurOccupation.cs:29-49).
2. Na forçada, o perdedor recebe `ForcedToSurrenderNotificationData` com os termos (DA:1505-1520; UI\NotificationsController.cs:229-277).
3. Evento de IA `Surrender`, que vira memória da IA (DA:1521; AID\ComputeRelationLogs.cs:318-327).
4. Exigências incluídas aplicadas (as obsoletas viram ouro). **Todas** as exigências entre os dois, dos dois lados, são descartadas (DA:1522-1537; DOFA:509-519).
5. Territórios incluídos transferidos, ações diplomáticas das exigências executadas e ouro cobrado (DA:1538-1554).
6. Com vassalagem → VassalToLiege; sem → Paz (DA:1555-1572).
   - `DSW.OnEnd` libera rascunhos e proposta (DSW:230-254).
   - **Devolve aos donos as cidades ocupadas** que não foram transferidas (DSW:255-330).
   - `SimulationEvent_DiplomaticStateChanged` sai duas vezes (DA:1571 e DRH:991).
7. Status "Vitorioso" nas cidades do vencedor (DA:1573-1583; DOFA:98).
8. Apoio à guerra dos dois → 0. O **proponente** recebe `WarEndedBySurrender` (DRH:431-440); os dois recebem `DiplomaticStateChanged` (DRH:992-1003).

### 2.6 O que derruba uma proposta (além das respostas)

| Gatilho | Efeito | Onde |
|---|---|---|
| Território **incluído** muda de dono, cidade é capturada ou o cerco muda | Proposta enviada cancelada no próximo `ResetTerritories`, que roda depois de qualquer ordem e no TurnBegin | DA:2045-2049, 2051-2147, 2204-2227, 225 |
| Território **não incluído** muda | Só marca WrongOwner nele | DA:2077-2087, 2120-2142 |
| Vassalagem começa ou termina envolvendo um dos dois | Proposta cancelada (no rascunho, a vassalagem é desmarcada) | DA:2014-2043 |
| A guerra acaba por outro caminho | Tudo liberado | DSW:230-254 |
| Impaciência High | Cancelada | DA:327-332 |
| Rascunho: território, era ou distritos mudam | Recalcula territórios e o custo da vassalagem, **mas não o ouro** | DA:1991-2012, 2149-2202 |

- O cancelamento não deixa registro nem notificação.
- Se o humano estava olhando, a UI mostra "rendição cancelada" (UI\DiplomaticCrisisPanel_SurrenderPanel.cs:158-161, 476-488).

### 2.7 (d) O que o humano vê

**A IA de linguagem oferece rendição ao humano** (humano vencedor, responde)
- **Popup obrigatório:** sim. `IsOtherSurrenderPropositionPending` (DiplomaticEmpireSummary.cs:175-178 → MandatoryDiplomaticPropositionPending.cs:119-122) trava o botão de fim de turno (UI\EndTurnWindow.cs:338-364).
- **Painel:** aba Crise, `SurrenderPanel` no estado AllowAnswer (0x46): botões Recusar e Aceitar, só os termos incluídos, rodapé com custo/placar (UI\DiplomaticCrisisPanel_SurrenderPanel.cs:236-272, 400-456).
- **Se não responder:** não consegue terminar o turno.

**A IA de linguagem impõe rendição ao humano** (humano perdedor, responde)
- **Popup obrigatório:** não por essa via, porque o flag só olha "o vencedor sou eu". Talvez venha por `MandatoryDiplomatic`, se o dado `DiplomaticActionUIMapper(DeclareSurrender).NotificationType` for Mandatory (UI\MandatoryDiplomatic.cs:42-63). Não dá para verificar no código.
- **Painel:** AllowAnswer_Forced (0x32). O humano já recebe um aviso quando o rascunho forçado é aberto.
- **Se não responder:** no TurnFinish, cerca de 16 s depois de todos estarem prontos, recusa automática.

**O humano oferece rendição à IA de linguagem** (humano perdedor, espera)
- **Popup obrigatório:** não.
- **Painel:** WaitingTheirAnswer ("esperando resposta").
- **Se não responder:** sem trava; resolve no TurnFinish, e com o hold do mod (§5.4) espera o próximo turno.

**O humano impõe rendição à IA de linguagem** (humano vencedor, espera)
- **Popup obrigatório:** **sim, por bug.** É o mesmo `IsOtherSurrenderPropositionPending`, que olha só "vencedor = eu" e ignora `IsSurrenderForced`.
- **Painel:** WaitingTheirAnswer.
- **Consequência:** o humano não termina o turno até a nação responder. A IA nativa responde na hora; a IA de linguagem não. Precisa do patch de UI (§5.4).

**O humano preenche um rascunho forçado** (humano vencedor)
- **Popup obrigatório:** sim (`IsForcedSurrenderPending`) até declarar ou cancelar.
- **Painel:** AllowTermFilling_Forced.

**Entrada e termos na UI**
- Em guerra, a aba Crise mostra os botões ProposeEndWarTreaty, StartToFill e AllowToForce (UI\DiplomaticCrisisPanel_CrisisGroup.cs:563).
- O painel de rendição substitui o de reclamações (UI\DiplomaticCrisisPanel.cs:96-127).
- As cidades agrupam os territórios; cada território é um interruptor (UI\DiplomaticCrisisPanel_SurrenderTerritoryTermItem.cs:95-118).
- Não há interruptor de ouro.
- A notificação `YouCanForceASurrender` existe na UI, mas ninguém a dispara (UI\NotificationsController.cs:775-784).

---

## 3. IA nativa

### 3.1 Quem decide cada passo

**Quer oferecer rendição** (`WantProposeSurrender`, AIM\MilitaryStrategy.cs:129-157)
- Só quando `ShouldEndWar` (:96-116), que é verdadeiro com: fraco demais, apoio acabando em menos de TimerVeryShort, estabilidade em colapso ou inimigo sem cidades.
- E ainda: vai zerar o apoio antes do outro, ou em 1 turno ou menos, ou o poder relativo está abaixo de −0,5.
- O viés ToTheEnd desliga tudo (:47-52).

**Abre a oferta** (GEN\Surrender.cs:24-48)
- Com WantProposeSurrender, StartToFill sem falha e ProposeToSurrender sem custo de influência (:26-29) → tarefa StartToFill.

**Quer impor** (AIM\MilitaryStrategy.cs:118-127)
- ShouldEndWar, ou foi ela quem declarou a guerra e tem o traço Forgiving.

**Abre a forçada** (GEN\ForceSurrender.cs:24-46)
- AllowToForce sem falha e WantForceSurrender → tarefa AllowToForce.

**Preenche os termos**
- Pontua Money, Submission, cada exigência e cada território selecionável; ordena; ajusta dívida e vassalagem (AID\ComputeSurrenderTermScores.cs:28-131).
- Inclui um termo por vez enquanto couber na sobra (GEN\FillSurrenderTerms.cs:26-132).
- Para no primeiro "Money" da lista: o resto vira ouro (:39-42).
- Só inclui, nunca tira (:126).

**Envia ou responde** (`EndSurrender.TryComputeAction`, GEN\EndSurrender.cs:83-129)
- Se pode aceitar:
  - como vencedor, aceita se `WantAcceptTheirSurrender`;
  - como perdedor, aceita a forçada **sempre** (:100).
- Se pode recusar, recusa.
- Senão: Propose (se quer oferecer) ou Declare (se quer impor).

**Aceita a oferta de outro** (AIM\MilitaryStrategy.cs:159-188, mais `IsSurrenderProposalAcceptable` :322-439)
- Aceita se ela mesma queria se render ou propor paz branca.
- Senão, recusa se o outro está para zerar o apoio: prefere impor.
- A soma de Impact e Motive dos termos (AID\DiplomaticTermPass.cs) tem de alcançar os mínimos do estado diplomático, ajustados pelo poder e por ShouldEndWar.

**Aceita a forçada** (AIM\MilitaryStrategy.cs:190-195)
- `WantAcceptMySurrender` = apoio em 0, mas o EndSurrender aceita a forçada de qualquer jeito.

**Ordens que a IA posta**
- `OrderDiplomaticAction` (AIA\DiplomaticAction.cs:40-55).
- Termos (AIA\ChangeSurrenderTerm.cs:17-48). Bug: o termo de exigência sai sem `SurrenderDemandIndex`, sempre 0 (:31-37).
- A IA nunca cancela; o Cancel loga erro (AIA\DiplomaticAction.cs:82-94).

**Custo da oferta:** reserva o ouro, com dívida permitida (AIT\DiplomaticAction.cs:89-106).

**Dados da IA**
- `RelationData.WantForceSurrender`, `WantProposeSurrender`, `WantAcceptTheirSurrender` e `WantAcceptMySurrender` (HeuristicBool), e `SurrenderTermScores` (`Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain.AnalysisData.Relation\RelationData.cs:128-134, 150`).
- `SurrenderTermScore` (mesma pasta, `SurrenderTermScore.cs:8-69`): Category (Money/Submission/Demand/Territory), Motive, Impact, TargetIndex, WarMoraleCost.
- Os geradores rodam na thread da IA e leem o snapshot (§1.6).

### 3.2 Pontos de patch recomendados

Thread da IA, mesmo padrão do `MOD\NativeAiLocks.cs`.

**Não abre oferta**
- Prefix em `Generators.Diplomacy.Surrender.GetParents(DiplomaticEmbassy)`: `return !IsLlmActive(__instance.Brain.EmpireIndex)`.
- Por que aqui: o método é declarado na própria classe. O `GetContexts` é do genérico `DoDiplomaticAction<T>`, compartilhado com outros geradores (GEN\DoDiplomaticAction.cs:20-30).

**Não abre forçada**
- Prefix em `Generators.Diplomacy.ForceSurrender.GetParents`. Mesmo motivo.

**Não preenche termos**
- Prefix em `Generators.Diplomacy.FillSurrenderTerms.GetContexts()`.
- Sem ele, a IA nativa da nação enfia termos no rascunho que o executor está montando e estoura o orçamento.

**Não envia, não aceita, não recusa**
- Postfix em `EndSurrender.TryComputeAction(DiplomaticEmbassy context, RelationData relationData, out DiplomaticAction result)`. É privado: patch pelo nome.
- Código: `if (__result && IsLlmActive(e) && !NativeFallback(e, context.OtherEmpireIndex)) { __result = false; result = DiplomaticAction.Count; }`.
- Por que aqui: a granularidade é por relação. Quando o mod desiste (prazo do hold, API fora), a IA nativa volta a responder só naquela relação.

**Opcional, não basta sozinho**
- Postfix em `MilitaryStrategy.Process` zerando os Want*.
- Sozinho não resolve: o EndSurrender continua recusando ofertas e aceitando forçadas.

**Não usar**
- Prefix em `Actuators.DiplomaticAction.CreateOrder` devolvendo null.
- `PostAndTrackOrder(null)` lança ArgumentNullException dentro da IA (SBX\PostOrderController.cs:76-81; `Amplitude.Mercury.AI.Brain\Amplitude.Mercury.AI.Brain\OrderActuator.cs:80-86`).

**Detalhes comuns às travas**
- `Brain` é campo `protected` (`Amplitude.AI\Amplitude.AI.ProcessingPasses\ProcessingPass.cs:11`), usado como nas travas que já existem.
- A tabela "volta ao nativo" é publicada pela thread principal como objeto imutável, trocado inteiro (mesmo padrão de `patronage` em NativeAiLocks).
- Contadores no `Describe()`.

---

## 4. Armadilhas

**1. Humano preso**
- O humano que impõe rendição a uma nação da IA de linguagem fica no popup obrigatório até ela responder (§2.7).
- Com o hold, isso vira trava permanente do turno. Exige o patch de UI (§5.4) ou resposta imediata.

**2. Rascunho órfão**
- O forçado bloqueia paz branca e rendição dos dois lados; o de oferta bloqueia o dono.
- Não expira e sobrevive ao save.
- Regra: `CancelSurrenderProposition` em qualquer falha do executor, e faxina a cada turno.

**3. Índice da exigência sem checagem**
- `OrderChangeSurrenderDemandState` usa `IncludedDemands[SurrenderDemandIndex]` sem conferir a faixa (DOFA:1139). Exceção no Validate = ticket que nunca fecha (`order-catalog.md` §6.4).
- `GrievanceType` = None ou Count dá LogError (DOFA:1125-1129).
- Use o `GrievanceType` da própria exigência e confira 0 ≤ índice < `IncludedDemands.Length`.

**4. Fase do turno**
- No TurnFinish as ordens de termo são **rejeitadas** (SBX\Sandbox.cs:1703); no TurnBegin ficam na fila (:1700).
- Monte ofertas e imposições só no TurnMain, como o executor já faz (MOD\ActionExecutor.cs:110-113).

**5. Orçamento só no fim**
- As ordens de termo não checam orçamento; a falha só aparece no Propose ou no Declare.
- Calcule antes de postar.

**6. Placar congelado** na abertura (DA:1383): abra e envie no mesmo pulso.

**7. Recusa trava a relação**
- Uma recusa trava propostas de tratado e ações de rendição na relação até o turno seguinte (DRH:448; DA:547-561).

**8. Paz branca pendente bloqueia tudo**
- Uma proposta de paz branca pendente, inclusive a segurada pelo ProposalHold do mod (MOD\ProposalHold.cs:30-72), bloqueia toda ação de rendição na relação (`AnswerPropositionFirst`).
- O dossiê tem de dizer: "responda antes à proposta de paz".

**9. Impaciência:** a recusa automática troca o autor na forçada (§2.5), e o nível High cancela e loga erro.

**10. Lixo em slot reaproveitado**
- `ProposedAtTurn` não é salvo.
- Slots reaproveitados não são zerados: num rascunho, `IsSurrenderForced` e `ProposedAtTurn` podem ser lixo.
- O hold precisa guardar seu próprio "visto no turno", por (vencedor, perdedor, forçada).

**11. Snapshot da IA com slots fantasmas** (§1.6).

**12. Cidades ocupadas voltam ao dono** se não entrarem nos termos (DSW:255-330).
- O vencedor que quer ficar com uma cidade ocupada **tem** de pedir o território central dela (custa 25 por território, o mesmo que ela soma ao placar).

**13. Conversão de moedas:** o ouro passa pela conversão do mod de moedas.

**14. Variante "ao aliado"** (`AllowToForceOtherToSurrenderToAlly`)
- Nasce de uma exigência AtWarWithAlly aceita. A reclamação tem GainParam 38 = essa ação (SIM\DiplomaticGrievanceSpawner_AtWarWithAlly.cs:30-31).
- Cria o rascunho forçado **na embaixada do aliado**, que preenche e declara (DRH:400-420).
- A validade é conferida com os papéis trocados (SIM\DiplomaticGrievanceHelper.cs:218-238 → BDS:434-447): exige o **aliado** em 0 de apoio e o alvo acima de 0. Parece bug; costuma virar ouro.
- Se a nação da IA de linguagem for o aliado, ela ganha um rascunho forçado sem pedir.
  - O dossiê tem de mostrar.
  - `impor_rendicao` tem de reaproveitar o rascunho: AllowToForce falharia com WrongSurrenderToOtherState.

**15. Multiplayer**
- Nada no código de rendição confere partida online.
- O mod já fica de fora em partidas online (MOD\ActionExecutor.cs:110; MOD\ProposalHold.cs:48). Manter assim.

**16. Notificações:** AllowToForce avisa o perdedor (DOFA:1407). Abrir rascunho forçado e desistir gera ruído para o humano.

**17. Flags de crise/queixas**
- `FinishSurrenderFirst` e `OnGoingSurrenderProposition` (BDS:924-952) só valem em guerra, onde ações de queixa já são proibidas (DSW:63-66).
- Na prática nunca aparecem. O texto do mod para elas (MOD\Capture\TurnCapture.cs:955) é inofensivo.

**18. Explain incompleto**
- O `Explain` do executor junta as três flags de estado de rendição numa frase só (MOD\ActionExecutor.cs:616-617).
- Não tem texto para `BetterPropositionPossible` nem para `WrongMoralForceOtherToSurrender` (§5.7).

---

## 5. Plano de implementação

### 5.1 Ferramentas (linhas para `Prompts.cs`)

```
- oferecer_rendicao: {"nacao": "E2", "termos": {"territorios": ["T21", "T22"], "submissao": false}, "motivo": "..."} — EXECUTADA: você se rende a essa nação. O preço é o placar de guerra DELES, que tem de ser gasto quase todo (no máximo 5 de sobra): as exigências deles entram sempre e o que sobrar dos territórios/vassalagem que você escolher vira ouro pago por você. Eles aceitam (a guerra acaba e os termos são cumpridos) ou recusam.
- impor_rendicao: {"nacao": "E2", "termos": {"territorios": ["T40", "T41"], "submissao": true}, "motivo": "..."} — EXECUTADA: só quando o dossiê mostrar "você pode IMPOR rendição" (apoio à guerra deles em 0). Mesmo orçamento, agora o SEU placar. Eles aceitam ou recusam (recusar em guerra total te dá +10 de apoio à guerra).
- responder_rendicao: {"nacao": "E2", "resposta": "aceitar|recusar"} — EXECUTADA: só quando o dossiê mostrar "RENDIÇÃO ESPERANDO VOCÊ".
```

**Semântica dos campos**
- `termos` omitido → padrão do jogo: as exigências obrigatórias e o resto em ouro.
- `territorios`: códigos T do dossiê. O executor ordena em BFS.
- `submissao`:
  - na oferta, "aceito virar vassalo";
  - na imposição, "exijo vassalagem".
- `exigencias: ["D12"]`: só aceito quando o dossiê disser "exigências opcionais" (regime 2 de §1.3). Fora disso, é ignorado com aviso.
- O ouro é sempre implícito, como na UI nativa. Não expor a exclusão.

### 5.2 Validação no parser (thread principal, `ValidationContext`)

- A nação é conhecida e está em guerra (`context.AtWar`).
- `oferecer_rendicao` exige `context.CanOfferSurrender[outro]`.
- `impor_rendicao` exige `context.CanForceSurrender[outro]` ou `context.ForcedDraftOpen[outro]` (variante ao aliado).
- `responder_rendicao` exige `context.SurrenderToAnswer[outro]`.
- Cada T e D pertence ao conjunto permitido daquela guerra e direção, e a soma prevista (pela prévia) cabe no orçamento.
- Mensagens no estilo das existentes (MOD\DecisionParser.cs:383-426).

### 5.3 Executor (thread do jogo, só TurnMain; um passo por ticket, como o presente)

**`oferecer_rendicao`**
1. Pré-checagens:
   - `GetDiplomaticActionFailureFlags(outro, StartToFillSurrenderProposition)` = None;
   - o "tratado limpo" (§2.1);
   - `GetDiplomaticActionInfluenceCostWith(eu, outro, ProposeToSurrender)` ≤ influência disponível.
2. Post `StartToFill`. Em Valid: `p = embaixada(eu).CurrentSurrenderToOtherProposition`.
3. Plano, lendo o rascunho real:
   - regime (`WinnerWarScore` contra `OverallDemandCost`);
   - territórios pedidos em ordem BFS, a partir dos que têm Connectivity ≠ None, usando a adjacência `World.Territories[i].AdjacentTerritories`;
   - vassalagem;
   - custo total ≤ placar. Sem ouro, também placar − custo ≤ 5.
4. Postar os termos um a um (`UponCompletionWithParam`), relendo `p` depois de cada um. Assim o motivo de cada falha sai exato (FailureFlags e Connectivity traduzidos, §5.7).
5. Antes de enviar: `flags(ProposeToSurrender)`. Se ≠ None → Cancel e Report(`Explain`).
6. Post `ProposeToSurrender`. Se voltar Invalid → `CancelSurrenderProposition` (como no presente, MOD\ActionExecutor.cs:244-246).

**`impor_rendicao`**
- Igual, com `AllowToForceOtherToSurrender` (ou o rascunho forçado já aberto) e `DeclareSurrender`.
- Confira o tratado limpo **antes** de abrir: um rascunho aberto que não consegue declarar vira órfão.

**`responder_rendicao`**
- Releia `ProposedSurrenderIndex` e confira o papel: na forçada eu sou o perdedor; na oferta, o vencedor.
- `flags(Accept/RefuseSurrender)` = None; então posta.

**Faxina** (a cada Pump no TurnMain)
- Rascunho de nação da IA de linguagem sem intenção em andamento há mais de N segundos → `CancelSurrenderProposition`.
- Cobre o caso de núcleo recarregado ou save carregado no meio de uma montagem.

**Preferência:** use a prévia (§1.6) na captura do turno, para o dossiê já mostrar o que é selecionável e o custo exato.

### 5.4 Segurar propostas (hold) e UI

**Estender o `ProposalHold`**
- Em `DiplomaticAncillary.ReadyForTurnFinishCompletion` (prefix, postfix e finalizer):
  - esconder com `war.ProposedSurrenderIndex = -1` cada proposta cujo **respondente** é IsLlmActive (forçada → perdedor; oferta → vencedor);
  - limite de N turnos (sugestão: config própria, 1 para forçada e `ProposalTurns` para oferta);
  - restaurar no postfix e no finalizer.
- **Não precisa** patch no `NewTurnBegin`: a rendição não é zerada na virada.
- Enquanto a proposta espera, a guerra continua. Se um território incluído mudar de dono, ela cai (§2.6).

**Patch de UI** (Assembly-CSharp, thread principal)
- Postfix em `MandatoryDiplomaticPropositionPending.RefreshData` (protected, retorna bool; campo privado `___allPendingPropositionsPerEmpireIndex`).
- Para cada i com o bit `Type.Surrender`: se a proposta pendente da relação (local, i) é forçada e o vencedor é o local (ele só está esperando), limpe o bit e recalcule `__result`.
- Corrige também o jogo sem mod; com a IA nativa não muda nada, porque ela responde na hora.

**Volta ao nativo**
- Passado o prazo (ou com a API fora), a relação entra na tabela de fallback do `EndSurrender`.
- A IA nativa então responde: aceita a forçada e avalia a oferta.
- Isso substitui a recusa automática do TurnFinish.

**Opcional:** ao detectar uma proposta nova para a IA de linguagem, pedir uma decisão curta no mesmo turno.
- Detecção pelo poll da thread principal (int `ProposedSurrenderIndex`) ou por um postfix em `DiplomaticAncillary.ProposeSurrender` (DA:1605).

### 5.5 Resultado

**Report imediato do executor**
- `"rendição oferecida a E2: exigências D12, D15 (20) + T22 (25) + 600 de ouro (40) = 85 de 85; E2 aceita ou recusa"`, ou o motivo da falha.

**Desfecho posterior** (thread do jogo, enfileirado como `ActionOutcome` e memória)
- **Aceite:** prefix em `DiplomaticAncillary.AcceptSurrender(int)`. Copie os termos **antes** de aplicar, porque o slot é zerado no fim da guerra.
- **Recusa:** prefix em `DiplomaticRelationHelper.ExecuteAction` com `RefuseSurrender`. Numa forçada, autor = vencedor ⇒ "o jogo recusou sozinho no fim do turno".
- **Queda:** prefix em `DiplomaticAncillary.CancelProposedProposition` (privado) ⇒ "a proposta caiu (território mudou de dono, vassalagem ou impaciência)".

**Histórico do dossiê**
- Já mostra Propose, Declare, Accept e Refuse (MOD\DossierBuilder.cs:677-680).
- StartToFill e Cancel não entram no log do jogo (DR:484-487).

### 5.6 Dossiê por guerra (logo após "placar de guerra", MOD\DossierBuilder.cs:328-329)

**Captura** (thread do jogo, em `CaptureRelation`, MOD\Capture\TurnCapture.cs:728-801)
- `CanForce` / `CanBeForced`: as flags de AllowToForce dos dois lados.
- `CanOffer` e o custo de influência.
- Proposta pendente: vencedor, perdedor, forçada, respondente, turno visto e termos.
- Rascunho forçado aberto.
- Prévia exata nas duas direções.
- Contadores de guerra total e `AllOutWarStabilityPenalty`.

**Texto do perdedor**

```
   Rendição (GUERRA TOTAL; apoio à guerra seu 0, deles 37):
   - Eles podem IMPOR rendição a você a qualquer momento (seu apoio está em 0). Se impuserem, você responde com
     responder_rendicao; recusar dá +10 de apoio a eles.
   - Oferecer rendição (oferecer_rendicao): o preço é o placar deles, 85 pontos, gasto quase todo (sobra máxima 5).
     Obrigatório: as exigências deles — D12 dinheiro 120; D15 território T21 (anexo de Lutécia) — 20 pontos.
     Restam 65: territórios seus, 25 cada — T22 posto Gergóvia; T30 anexo de Alésia; T31 anexo de Alésia (só junto com T30).
     Vassalagem: indisponível (eles não têm a ação de submissão). O resto vira ouro: 1 ponto = 15 de ouro (65 = 975).
     Oferecer agora custa 0 de influência.
   - Costume da IA: quem está quase zerando recebe recusa (eles preferem impor).
```

**Texto do vencedor**

```
   Rendição (GUERRA TOTAL; apoio à guerra seu 54, deles 0):
   - Você pode IMPOR rendição (impor_rendicao). Seu placar: 110 pontos.
     Obrigatório: suas exigências D3 (cívico ...) e D4 (dinheiro 80) — 20. Restam 90:
     territórios deles, 25 cada — T40 Roma (centro; você ocupa), T41 anexo de Roma (com T40), T44 posto (na sua fronteira);
     vassalagem: 60 pontos; o resto vira ouro (1 ponto = 20).
     Cidades que você ocupa e não pedir voltam para eles quando a guerra acabar.
   - Enquanto não impõe, você perde estabilidade (penalidade atual 3); são 4 turnos com eles em 0.
   - RENDIÇÃO ESPERANDO VOCÊ: E2 se ofereceu (turno 51): D3, D4; T40; 300 de ouro (80 de 80 pontos).
     Aceitar encerra a guerra agora (paz, apoio à guerra dos dois volta a 0); recusar mantém a guerra.
```

**Avisos que o dossiê também deve dar quando valerem**
- "há proposta de paz aberta: responda antes" (§4, item 8);
- "houve recusa nesta guerra neste turno: só no próximo" (§4, item 7);
- "um aliado te deu o direito de impor rendição a X (rascunho aberto)" (§4, item 14).

### 5.7 Textos para `Explain` (e para os termos)

**Flags de ação**
- `NotEnoughWarScore`: "os termos custam mais que o placar do vencedor".
- `BetterPropositionPossible`: "os termos deixam mais de 5 pontos do placar sem uso: inclua mais ou deixe o ouro cobrir".
- `WrongMoralForceOtherToSurrender`: "só dá para impor rendição com o apoio à guerra deles em 0 e o seu acima de 0".
- `WrongOnGoingSurrenderState`: "já há rendição esperando resposta nesta guerra (ou não há guerra)".
- `WrongForceOtherToSurrenderState`: "há rendição forçada sendo preparada nesta guerra".
- `WrongSurrenderToOtherState`: "já há rascunho de rendição seu aberto (ou não há rascunho)".
- `TreatyRefused*`: "houve recusa nesta relação neste turno; tente no próximo".
- `AnswerPropositionFirst`: "responda antes à proposta de paz aberta".

**Falhas de território**
- `WrongOwner`: "não é deles".
- `CityCenterNotOccupied`: "centro de cidade: só se você a estiver ocupando".
- `OccupiedBySomeoneElse`: "ocupado por outra nação".
- `CityBeingBesieged` / `ContainsBoroughBeingBesieged`: "sitiado".
- `LinkedToDemand`: "já está numa exigência".
- `NotEnoughToFulfillDemands`: "o placar não sobra depois das exigências".
- Connectivity None: "não encosta na fronteira do vencedor nem num território já incluído".

**Falhas de vassalagem**
- `NotAvailable`: "o vencedor não pode exigir vassalagem (falta a ação, ou a opção da partida está desligada)".
- `HasALiege`: "o perdedor já é vassalo de alguém".

### 5.8 Testes sugeridos (save Francos, de teste)

1. **Oferta ao nativo:** nação da IA de linguagem em 0 oferece ao nativo, com e sem território. Confira os pontos e a resposta da IA nativa.
2. **Imposição ao nativo:** nação da IA de linguagem impõe a nativo em 0 (a resposta deve ser aceite imediato). Confira a transferência e a devolução das cidades ocupadas não pedidas.
3. **Humano impõe:** o humano impõe à nação da IA de linguagem. Com o patch de UI, termina o turno; o hold segura; a nação responde no turno seguinte.
4. **Nativo impõe:** o nativo impõe à nação da IA de linguagem; o hold segura; expira e a IA nativa aceita.
5. **Recusa automática:** derrubar a API no meio. A trava cai e a IA nativa responde; nenhum rascunho órfão.
6. **Termos inválidos:** território sem conexão, vassalagem indisponível, orçamento estourado. O rascunho deve ser cancelado e a explicação certa.
