# Crise / reclamações / exigências — pesquisa para a Mesa de Negociação (2026-10-03)

Só leitura do código descompilado. FP = `Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\`, IO = `…\Amplitude.Mercury.Interop\`, AI = `Amplitude.Mercury.AI.Brain\`, UI = `Assembly-CSharp\Amplitude.Mercury.UI\`.

## Resumo
- Não existe negociação: exigências são uma pilha fixa; o outro só aceita tudo, recusa tudo ou enrola (StallForTime, uma vez).
- Única "contraproposta" do jogo: tratados/acordos, e só adiciona um preço em ouro (`Bribe`), um slot por relação.
- Rendição é o mais próximo de oferta configurável (vencedor escolhe termos; perdedor aceita/recusa).
- A IA não precifica ofertas em ouro: cada termo tem Motive/Impact (0..1) comparados a limiares por humor.

## Modelo de dados
- **Reclamação**: `DiplomaticGrievanceInfo` (IO) em `DepartmentOfForeignAffairs.GrievanceAllocator` (FP/DepartmentOfForeignAffairs.cs:71); por relação em `DiplomaticAmbassy.AvailableGrievances`. `DemandGain {GainGuid, GainParam, GainParamName}`.
  - Spawners `DiplomaticGrievanceSpawner_*` (41 tipos), `MaxDuration => 10`. Dinheiro via RPN `EmpireAction_AggressionDemandCost` × `GrievanceMoneyMultiplier`.
  - Contador "7"/"6" = `MaxDuration - (CurrentTurn - TurnWhenObserved)`; expira em `TurnEnd_CleanOldGrievances` (DoFA:2190).
- **Exigência**: `DiplomaticDemandInfo` (IO) em `DemandsAllocator`; por relação `DiplomaticAmbassy.OnGoingDemands`. Criada em `CreateDemandAgainst` (DoFA:521). Não expira.
- **Pontuação de guerra**: `DiplomaticRelation.GetWarScoreFor` (:435) = (exigências×10 + territórios de cidade capturados×25 + bônus paz branca) × multiplicador.
- **Apoio à guerra (Moral)**: 0..100 por embaixada, `TurnEnd_UpdateMoral` (DiplomaticAncillary:745).
- **Crise**: `CrisisInfo {Status None/OnGoing/DemandRefused/…, LastRefuserEmpireIndex}`; casus belli = `Status==DemandRefused && LastRefuser != e`. Guerra formal exige moral ≥ 80 ou guerra justificada.
- **Rendição**: `SurrenderProposition` (IO); `InitializeSurrenderProposition` (:1827): exigência 10 pts, território 25 pts, dinheiro 1 pt = 5×mult×Era, vassalagem via RPN. `AcceptSurrender` (:1496).

## Ordens (DepartmentOfForeignAffairs)
- `OrderExecuteGrievanceAction` (Renounce/CreateDemand) e `…Batch` ("Exigir tudo"/"Renunciar a tudo").
- `OrderDiplomaticAction` → `DiplomaticRelationHelper.ExecuteAction` (:239): AcceptDemands (:722), RefuseDemands (:768, cria casus belli), WithdrawDemands (:745), StallForTime (:919), ProposeEndCrisisTreaty (assinado → `DismissAllGrievances`), rendição (Start/Propose/Declare/Accept/Refuse/Cancel).
- Termos de rendição: `OrderChangeSurrenderDemandState/MoneyState/SubmissionState/TerritoryState`.
- Ações só em TurnBegin/TurnMain; respostas também em TurnFinish.

## IA
- `ComputeCrisisStrategy` (AI/Analysis.Diplomacy:45): Deescalate/Wait/RefuseDemands/Escalate/PushAdvantage/DeclareInternationalCrisis.
- `ComputeDemandsAction.Process` (:31): aceita se `AreDemandsTolerable` (:162): `TheirDemandsImpactOnUs <= MaximumTolerableImpact` e `OurDemandsMotive <= MaximumTolerableLostMotive` (limiares em `AIDiplomaticStateConfiguration` por humor; ×1.25 em Fácil).
- Avaliadores estáticos: `DiplomaticTermPass` (`ComputeMoneyTermScores`, `ComputeTerritoryTermScores`, `ComputeCivicTermScore`, `ComputeSubmissionTermScore`, `ComputeReligionTermScore`, `ComputeInterventionTermScore`); `ComputeDemandsOutrage.ComputeOutrage`; `MilitaryStrategy.IsSurrenderProposalAcceptable` (:322).
- Esses avaliadores dependem do cérebro da IA (snapshots, possivelmente assíncrono) → mais seguro reimplementar um avaliador leve sobre dados da simulação com as mesmas fórmulas/limiares.

## Tipos de ganho (`DemandGainType`)
Money (`ApplyMoneyGain`), Territory (`ApplyTerritoryGain`/`WrapTerritoryGain`), ForceCivic, ForceReligion, DiplomaticAction. Sem influência/cidade/ideologia diretos. Despacho em `DiplomaticGrievanceHelper.TryToApply` (:505).

## Infra de propostas
- Tratados/acordos: `DiplomaticPropositionInfo {Status, Bribe, …}`, `TreatyStatus None/Proposed/Countered/Refused/Insulted/Signed/Cancelled`; um slot por relação; propostas pendentes bloqueiam o fim do turno.
- Presentes: `GiftProposition {Money, Influence, EntityToGive}` — veículo de transferência funcional (`AcceptGift`).
- Enums fechados (`DiplomaticAction`, `TreatyType`, `OrderIdentifier`): não dá para criar novos valores sem quebrar switches.

## UI da aba Crise
- `DiplomaticCrisisPanel`, `DiplomaticCrisisPanel_CrisisGroup` (`RefreshActions()` :456, `ActionItem_OnClick` :664, `MyGrievancesAction_OnClick` :627), `_SelectionPanel` (:289), `_GrievanceItem/Line`, `_DemandItem/Line`, `_SurrenderPanel`.
- Ganchos: postfix `RefreshActions`/`Refresh`/`Load`/`PostLoad`.
- Snapshot `DiplomaticCursorSnapshot.PresentationData.RelationDetails` só atualiza quando `relation.Frame` muda.

## Falas do líder
- Via "transações": `DepartmentOfForeignAffairs.RegisterTransaction(...)` (:2597-2650) → `DiplomaticDialogId.GetDialogId`. Existem falas de contraproposta, aceitar/recusar, ridicularizar exigências, pedir desculpas, etc. Um mod pode disparar falas registrando transações com ações existentes.

## Para construir
- Estrutura de oferta (vários itens, dois sentidos), slot de oferta pendente e ciclo (propor → IA aceita/recusa/contrapropõe → jogador responde), avaliador da IA + gerador de contraproposta, caminho de comando, painel de UI + notificação.

## Riscos
- Threading da IA; enums fechados; saves versionados (estado do mod em arquivo à parte ou só mutar campos existentes); propostas pendentes bloqueiam turno; efeitos colaterais de `ExecuteAction` (moral, badges, eventos de IA) não acontecem em acordos parciais se não forem replicados; validade de exigências; atualizar `relation.Frame` + `SetSynchronizationDirty` para a UI refrescar.
