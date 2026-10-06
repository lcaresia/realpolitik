# Prazos nativos da diplomacia × propostas "em análise" (pesquisa 2026-10-04)

Pesquisa só de leitura, para decidir o F3 do design (`docs\design-diplomacia-ia.md` §15.1).
Notação: SIM = `decompiled\Amplitude.Mercury.Firstpass\Amplitude.Mercury.Simulation\`,
SBX = `...\Amplitude.Mercury.Sandbox\`, UI = `decompiled\Assembly-CSharp\Amplitude.Mercury.UI\`.
Os valores base vêm de `[PropertyDefinition(BaseValue=…)]` em `SIM\DiplomaticAmbassy.cs` e podem ser alterados por
dados (asset bundles).

## Resumo
- **Nenhuma proposta nativa entre impérios atravessa a virada do turno.** Isso vale para tratado, acordo, consulado,
  presente, rendição e exigência forçada.
- **Bloqueio de fim de turno:** enquanto houver proposta pendente, o turno não termina. Se nenhum humano estiver
  preso a um popup obrigatório, o jogo resolve sozinho em cerca de 16 a 24 segundos.
- **Reset na virada:** no começo do turno seguinte, o que sobrou de tratado e acordo volta para `None`.
- **Consequência:** "em análise por vários turnos" tem que ser estado 100% do mod. A alternativa é patchear
  `DiplomaticAncillary.ReadyForTurnFinishCompletion` **e** `NewTurnBegin_ResetTreatyStatus`.
- **Exigências (demandas) não expiram.** O "relógio" delas é a moral de quem exige: +3 por turno até 80, quando
  passa a poder declarar guerra formal.

## O mecanismo central: bloqueio e "impaciência"

**Bloqueio.** `DiplomaticAncillary.ReadyForTurnFinishCompletion` (`SIM\DiplomaticAncillary.cs:266-393`) devolve
false enquanto houver, em qualquer relação:
- tratado ou acordo `Proposed` ou `Countered`;
- consulado `Proposed`;
- rendição ou presente propostos;
- `EmpireWhoNeedToAnswerEnforcedDemand >= 0` com crise `OnGoing`.

Os `HasPending*` ficam em `SIM\DiplomaticRelation.cs:341-397`.

**Escalonamento** (`SBX\SandboxState_TurnFinish.cs`):
- `TimeoutSeconds = 8` (:9). A cada 8 s parado, a impaciência sobe um nível: None → Low → Medium → High → Maximum
  (:97-120). Cada nível posta `OrderForceFinishTurn` (`SIM\DepartmentOfCommunication.cs:671-690`).
- O cronômetro zera quando chega ordem nova (:73-83).

**O que cada nível faz:**
- **Low (~8 s):** nada.
- **Medium (~16 s):**
  - tratado → `IgnoreTreaty`; acordo → `IgnoreAgreement`; consulado → `Refuse`;
  - rendição → `RefuseSurrender`;
  - **presente → `AcceptGift` (aceita sozinho!)**;
  - exigência forçada → `RefuseDemands`, o que gera pretexto de guerra.
- **High (~24 s):** limpa tudo à força.
- **Maximum:** termina o turno de qualquer jeito (:67-71).

**Exceção humana.** Com um humano vivo em `IsLockedByMandatories`, o cronômetro zera e o turno espera para sempre
(:103-109). Não existe esse escudo para IA.

**Quando cada ação é permitida** (`SIM\BaseDiplomaticState.cs:682-726`):
- propor: só em TurnBegin/TurnMain;
- responder (Sign/Counter/Ignore/Accept/Refuse e presentes): também em TurnFinish.

**Nome enganoso:** `TurnEnd_RefuseOnGoingPropositions` (`SIM\DiplomaticAncillary.cs:806`) não recusa nada.

## 1. Tratados e acordos

**Reset na virada:** `NewTurnBegin_ResetTreatyStatus` (`SIM\DiplomaticAncillary.cs:547-579`).
- Todo status diferente de `Refused` volta para `None`.
- `Refused` também volta no turno seguinte, porque `TurnWhenRefused` fica em -1 (`SIM\DiplomaticRelationHelper.cs:1312,1336`).
- A proposta de consulado é sempre zerada.

**Um espaço por relação:**
- Proposta pendente bloqueia outras ações com `AnswerPropositionFirst` e similares
  (`SIM\DiplomaticRelationHelper.cs:221-237`), **inclusive declarar guerra** (`SIM\BaseDiplomaticState.cs:192`).
- Recusada ou insultada, não pode ser reproposta no mesmo turno (:187-214).
- Criar uma exigência derruba as propostas abertas (`SIM\DepartmentOfForeignAffairs.cs:1612-1620`).

**Recarga para repropor:**
- `NumberOfTurnTimeoutOnDiplomaticAction` = **5 turnos** (`SIM\DiplomaticAmbassy.cs:273`).
- Repropor antes custa influência (`SIM\DepartmentOfForeignAffairs.cs:2506-2523`).

## 2. Presentes
- Não expiram por turno (`ProposedAtTurn` só é lido pela IA).
- **Pendentes, bloqueiam o fim do turno e são aceitos sozinhos no nível Medium** (`SIM\DiplomaticAncillary.cs:340-355`).
- São liberados quando começa uma guerra (`SIM\DiplomaticState_War.cs:188`).
- Presente pendente impede novo presente (`AnswerGiftPropositionFirst`).
- No aceite, a validade é conferida de novo (`InvalidGiftProposition`).

## 3. Reclamações, exigências e crise
- **Reclamação:** dura 10 turnos (`TurnEnd_CleanOldGrievances`, `SIM\DepartmentOfForeignAffairs.cs:2190-2223`).
- **Exigência:** `CreateDemandAgainst` (:521-562), **sem prazo**.
- **Relógio implícito:** a moral, em `TurnEnd_UpdateMoral` (`SIM\DiplomaticAncillary.cs:745`).
  - Sem guerra, quem tem mais exigências ganha **+3 por turno** (`SIM\DiplomaticAmbassy.cs:156`).
  - Saindo de 50, chega a 80 (`DeclareFormalWarMoralThreshold`) em cerca de 10 turnos e aí pode declarar guerra
    formal sem recusa.
  - `NewTurnBegin_RegisterUltimatum` avisa o alvo (:717-734).
- **StallForTime ("enrolar"):** uma vez por crise.
- **Recusa:** deixa a crise em `DemandRefused`. O pretexto de guerra (`IsJustified`) não expira; só some com
  guerra, retirada, fim de aliança ou nova exigência.
- **Exigência forçada (consulado, `LeverageEnforceDemands`):** tem que ser respondida **no mesmo turno**. Bloqueia o
  fim do turno e é recusada sozinha no nível Medium.
- **Votação de crise internacional:** dura `CrisisVote_TurnDuration` (dado de jogo) e termina por tempo ou quando
  todos votam (`SIM\InternationalAncillary.cs:1080-1113`).

## 4. Requisitos para declarar guerra (`SIM\BaseDiplomaticState.cs:190-219`)
- **Formal:** moral ≥ 80 **ou** pretexto de guerra, e sem votação internacional pendente.
- **Surpresa:** moral ≥ 0. Fica bloqueada (`UseFormalWarInstead`) quando a formal é possível.
- **Ambas:** não pode haver proposta pendente na relação.

## 5. Rendição e guerra total
- **Moral ≤ 0** de um lado → guerra total (`NewTurnBegin_CheckMoralAgainstSurrender`,
  `SIM\DiplomaticAncillary.cs:581-603`).
- **Rendição forçada:** o vencedor preenche os termos → `DeclareSurrender` → o perdedor aceita ou recusa
  (`SIM\DiplomaticRelationHelper.cs:382-475`).
  - Pendente, bloqueia o turno; no nível Medium é recusada sozinha.
  - Não existe aceite automático.
- **Se o vencedor nunca preenche os termos:** a proposta fica aberta para sempre.
  - `TurnEnd_CancelOldSurrenderProposition` está com a conta invertida e nunca dispara.
  - O custo de esperar vem da penalidade de estabilidade por turno de guerra total sem uso da rendição forçada
    (:605-652), com lembrete a cada 5 turnos.

## 6. Paz
- Paz branca = tratado comum (seção 1).
- Não existe trégua com duração.

## 7. Popups obrigatórios para o humano
- `MandatoryDiplomaticPropositionPending`, `MandatoryDemandEnforcedPending`, `MandatoryDemandsRefused` e
  `MandatoryLostCrisisVote` (em UI).
- Com qualquer um ativo, o fim do turno só abre o popup. O humano não tem tempo limite no singleplayer.

## 8. Espionagem e consulados
- **Agentes:** são exércitos invisíveis, com furtividade que se regenera (`SIM\StealthAncillary.cs`).
- **Ações de infiltração** (`SIM\AgentAncillary.cs`, `SIM\InfiltrationArmyAction.cs`): explorar distrito (rouba
  recursos), sabotar, vigiar cidade (visão), manipular cidade, rastrear exército, desorganizar exército, isca.
- **Consulados:** dão acordos e ações de alavancagem: exigir à força, marcar exércitos como indesejados, sanções,
  reduzir placar de guerra, revelar agentes (`SIM\DiplomaticConsulatHelper.cs:146-199`).

## 9. Povos independentes
- **Ordens:**
  - `OrderPatronizeMinorEmpire`, `OrderSetPatronageInvestmentLevels`, `OrderChangePatronageAmount`;
  - `OrderEnactTreatyMinorEmpire`.
- **Tratados:** OpenContact, TradingCharter, MercenaryCharter, ScienceCollaboration, ProfitSharing,
  PrivilegedContract, CulturalExchange, **Puppet** (vassalo) e **Assimilate** (anexar).
- Têm custo em influência e exigem patronagem mínima.
- **São aplicados na hora:** não há proposta nem prazo.

## Implicações para o design
1. Para uma proposta do jogador "ficar em análise" na IA de linguagem, há dois caminhos:
   - **(A) Registro só do mod:** a proposta nativa é ignorada no fim do turno e o mod a guarda. Se a IA aceitar no
     turno seguinte, aplica com `ForceSign…` ou com uma contraproposta.
   - **(B) Patch no fluxo nativo:** patchear o bloqueio de fim de turno e o reset da virada. A proposta continua
     pendente na tela nativa até a IA responder.
   - Em ambos: o espaço pendente bloqueia guerra, outras propostas e presentes naquela relação, e se a API cair a
     regra nativa volta (F1).
2. **Nunca deixar presente nativo pendurado:** é aceito sozinho.
3. Exigências podem ficar sem resposta; o relógio real é a moral de quem exige.
4. A IA nativa responde na hora, então é preciso afastá-la dessas relações (§11.0 do design).
