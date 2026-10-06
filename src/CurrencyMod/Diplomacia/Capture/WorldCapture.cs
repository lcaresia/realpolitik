using System;
using System.Collections.Generic;

namespace CurrencyMod.Diplomacia.Capture
{
    /// <summary>
    /// Fotografia do mundo tirada no começo do turno, na thread do jogo (TurnCapture). Só tipos simples: depois de
    /// pronta, pode ser lida em qualquer thread sem tocar na simulação.
    /// </summary>
    internal sealed class WorldCapture
    {
        public string Guid;
        public int Turn;
        public int LocalEmpire;
        /// <summary>true = tirada na thread principal (recarga a quente no meio do turno): melhor esforço.</summary>
        public bool FromMainThread;
        /// <summary>Crise no Congresso mundial possível: DLC Together We Rule, opção da partida e votação de crise liberada.</summary>
        public bool CongressCrisisOpen;
        /// <summary>Congresso mundial (research\congress.md). Null quando a partida não tem a expansão ou o Congresso está
        /// desligado nas opções.</summary>
        public CapturedCongress Congress;
        public DateTime CapturedAtUtc;
        public CapturedEmpire[] Empires = new CapturedEmpire[0];
        public string[] TerritoryNames = new string[0];
        /// <summary>Dono de cada território: índice do império maior, ou -1 (sem dono / povo independente).</summary>
        public int[] TerritoryOwner = new int[0];
        public bool[] TerritoryOcean = new bool[0];
        public int[][] Adjacent = new int[0][];
        /// <summary>Jazidas de cada recurso no mapa (índice = ResourceType; 0 = o recurso não existe neste mundo).</summary>
        public int[] ResourceDeposits = new int[0];
        /// <summary>Compras das rotas comerciais entre impérios maiores: quem compra quais recursos de quem.</summary>
        public List<CapturedTrade> Trades = new List<CapturedTrade>();
        /// <summary>Pedágios cobrados no fim do turno anterior pelo bloqueio comercial do mod.</summary>
        public List<CapturedToll> Tolls = new List<CapturedToll>();
        /// <summary>Postos com pedágio ou bloqueio, somados por dono e império afetado.</summary>
        public List<CapturedTradeRule> TradeRules = new List<CapturedTradeRule>();
        /// <summary>Espiões (exércitos com unidade agente) de todos os impérios, com o que a interceptação de cartas usa.</summary>
        public List<CapturedSpy> Spies = new List<CapturedSpy>();
        /// <summary>Territórios onde o jogo marcou atividade de espiões estrangeiros neste turno (o dono recebeu o aviso).</summary>
        public List<int> StealthActivity = new List<int>();
        /// <summary>Detecção de furtividade do dono em cada território (0 sem dono).</summary>
        public float[] OwnerDetection = new float[0];
        /// <summary>Povos independentes vivos com assentamento, e a relação de cada império maior com eles.</summary>
        public List<CapturedMinor> Minors = new List<CapturedMinor>();

        public CapturedMinor Minor(int index) => Minors.Find(m => m.Index == index);

        public CapturedEmpire Empire(int index) => index >= 0 && index < Empires.Length ? Empires[index] : null;

        public int OwnerOf(int territory) => territory >= 0 && territory < TerritoryOwner.Length ? TerritoryOwner[territory] : -1;

        public string TerritoryName(int territory) =>
            territory >= 0 && territory < TerritoryNames.Length && !string.IsNullOrEmpty(TerritoryNames[territory]) ? TerritoryNames[territory] : $"território {territory}";

        public IEnumerable<int> AiEmpires()
        {
            foreach (CapturedEmpire empire in Empires)
            {
                if (empire != null && empire.Alive && !empire.Human)
                {
                    yield return empire.Index;
                }
            }
        }

        public IEnumerable<int> AliveEmpires()
        {
            foreach (CapturedEmpire empire in Empires)
            {
                if (empire != null && empire.Alive)
                {
                    yield return empire.Index;
                }
            }
        }
    }

    internal sealed class CapturedEmpire
    {
        public int Index;
        public bool Alive;
        public bool Human;
        public string FullName;
        public string Leader;
        public string Culture;
        public int EraIndex;
        public string EraName;
        public uint Archetypes;
        public uint Biases;

        public double Money;
        public double MoneyNet;
        public double Influence;
        public double InfluenceNet;
        public double Science;
        public double Stability;
        public double Fame;
        public int FameRank;
        public int EraStars;
        public int Population;
        public int Technologies;
        public int TerritoryCount;
        public double MilitaryStrength;
        public int UnitCount;
        /// <summary>Acesso a cada recurso (índice = ResourceType): jazidas exploradas mais o que compra.</summary>
        public int[] ResourceAccess = new int[0];
        /// <summary>Moeda do império no CurrencyMod (plural, ex.: "Reais"); null se o mod de moeda não tiver dados.</summary>
        public string CurrencyName;
        public string CurrencySymbol;

        public List<CapturedCity> Cities = new List<CapturedCity>();
        public List<CapturedArmy> Armies = new List<CapturedArmy>();
        /// <summary>Relação com cada outro império maior (null no próprio índice).</summary>
        public CapturedRelation[] Relations = new CapturedRelation[0];
        /// <summary>Exércitos de outros impérios que esta nação está vendo agora (névoa e furtividade aplicadas).</summary>
        public List<SeenArmy> SeenArmies = new List<SeenArmy>();
        /// <summary>Assentamentos de outros impérios em tiles que esta nação já explorou.</summary>
        public List<SeenCity> SeenCities = new List<SeenCity>();
        /// <summary>Impérios maiores com território vizinho a um território desta nação.</summary>
        public List<int> Neighbors = new List<int>();

        public CapturedRelation RelationWith(int other) => other >= 0 && other < Relations.Length ? Relations[other] : null;
    }

    internal sealed class CapturedCity
    {
        public string Key;
        public string Name;
        public bool IsCity;
        public bool Capital;
        public int Population;
        public double PublicOrder;
        /// <summary>Indústria líquida e crescimento (comida que sobra) por turno.</summary>
        public double Production;
        public double Growth;
        public int CenterTerritory = -1;
        public List<int> Territories = new List<int>();
        public bool Besieged;
        public bool Captured;
        public int OriginalOwner = -1;
    }

    internal sealed class CapturedArmy
    {
        public string Key;
        public string Name;
        public int Units;
        public double Strength;
        public double Health;
        public int Territory = -1;
        public string State;
        public bool Moving;
        public bool Naval;
        public bool Spy;
        public bool Besieging;
    }

    /// <summary>
    /// Espião: exército com pelo menos uma unidade agente (UnitTagAsAbility.Agent). Os campos de furtividade são em
    /// relação ao dono do território onde ele está (pesquisa: research\espionage-interception.md).
    /// </summary>
    internal sealed class CapturedSpy
    {
        public int Owner;
        public string Key;
        public string Name;
        public int Territory = -1;
        /// <summary>Dono do território onde o espião está (-1 = sem dono ou povo independente).</summary>
        public int TerritoryOwner = -1;
        public int Agents;
        /// <summary>Menor veterania entre as unidades (o jogo usa o mínimo para o exército).</summary>
        public double Veterancy;
        public double Stealth;
        public double StealthMax;
        /// <summary>Oculto do dono do território (furtivo e sem visão compartilhada de aliança).</summary>
        public bool Hidden;
        /// <summary>Pode agir contra o dono do território: oculto e sem acordo que proíba ações de agente.</summary>
        public bool CanAct;
        /// <summary>Detecção do dono do território ali.</summary>
        public double OwnerDetection;
        /// <summary>Maior detecção hostil contra o espião ali.</summary>
        public double HostileDetection;
        /// <summary>Turnos até a furtividade acabar com a detecção atual (int.MaxValue = ninguém detecta ali).</summary>
        public int TurnsBeforeRevealed = int.MaxValue;
        /// <summary>O dono do território caça espiões revelados (habilidade do jogo).</summary>
        public bool OwnerHuntsSpies;
        /// <summary>Missão "Vigiar cidade" ativa.</summary>
        public bool WatchingCity;
        /// <summary>Infiltração em curso ou concluída (WatchCity, ExploitDistrict...); null = nenhuma.</summary>
        public string Infiltration;
        public int InfiltrationTarget = -1;
        public string InfiltrationSettlement;
        public bool Infiltrated;
        public int InfiltrationTurnsLeft;
    }

    /// <summary>Povo independente (império menor). O índice é reaproveitado quando um povo some e outro aparece.</summary>
    internal sealed class CapturedMinor
    {
        public int Index;
        public string Name;
        /// <summary>Young, Zenith, InDecline ou Dying.</summary>
        public string Status;
        public bool Peaceful;
        public int CenterTerritory = -1;
        public string CityName;
        public List<int> Territories = new List<int>();
        /// <summary>Império maior com mais patrocínio (-1 = ninguém).</summary>
        public int TopPatron = -1;
        /// <summary>Relação com cada império maior (índice = império maior; null se não conhece).</summary>
        public CapturedMinorRelation[] Relations = new CapturedMinorRelation[0];
    }

    internal sealed class CapturedMinorRelation
    {
        public double Patronage;
        /// <summary>Parte do patrocínio total (0 a 1).</summary>
        public double Share;
        public string MoneyInvestment;
        public string InfluenceInvestment;
        /// <summary>Motivo do jogo para não poder patrocinar agora (null = pode).</summary>
        public string PatronizeBlocked;
        public List<CapturedMinorTreaty> Treaties = new List<CapturedMinorTreaty>();
    }

    internal sealed class CapturedMinorTreaty
    {
        /// <summary>Valor de MinorTreaty.</summary>
        public int Treaty;
        /// <summary>Nome da definição do jogo (traduzido no dossiê pela GameGlossary).</summary>
        public string Definition;
        public bool Enacted;
        public bool Available;
        public double Cost;
        /// <summary>Por que não dá agora ("falta patrocínio", "falta influência", "outro tratado do grupo já escolhido").</summary>
        public string Blocked;
    }

    internal sealed class SeenArmy
    {
        /// <summary>GUID do exército (hex), para o código A# de alvo de ataque.</summary>
        public string Key;
        public int Owner;
        public int Units;
        public double Strength;
        public int Territory = -1;
        public bool Naval;
        /// <summary>Parte do exército é furtiva e não foi detectada: os números são só da parte visível.</summary>
        public bool Partial;
    }

    internal sealed class SeenCity
    {
        public int Owner;
        public string Key;
        public string Name;
        public bool IsCity;
        public bool Capital;
        public int CenterTerritory = -1;
        /// <summary>O tile está visível agora (população e cerco valem só nesse caso).</summary>
        public bool Visible;
        public int Population;
        public bool Besieged;
    }

    internal sealed class CapturedRelation
    {
        public int Other;
        public string State;
        public bool Knows;
        public bool AtWar;
        public bool AllOutWar;
        public bool Alliance;
        public bool OpenBorders;
        public bool Trade;
        public bool SharedMaps;
        public bool NonAggression;
        public double MyMoral;
        public double TheirMoral;
        public double MyWarScore;
        public double TheirWarScore;
        /// <summary>Reclamações desta nação contra a outra que ainda não viraram exigência.</summary>
        public List<CapturedGrievance> MyGrievances = new List<CapturedGrievance>();
        /// <summary>Tipos das reclamações da outra contra esta nação.</summary>
        public List<string> TheirGrievances = new List<string>();
        public bool DemandsAgainstThem;
        public bool DemandsAgainstMe;
        /// <summary>Exigências em aberto desta nação contra a outra, e da outra contra esta.</summary>
        public List<CapturedDemand> MyDemands = new List<CapturedDemand>();
        public List<CapturedDemand> TheirDemands = new List<CapturedDemand>();
        /// <summary>Quem já pediu prazo ("enrolar") nesta crise: só uma vez por crise.</summary>
        public bool IStalled;
        public bool TheyStalled;
        /// <summary>Exigência forçada pelo consulado: quem precisa responder no mesmo turno (-1 = nenhuma). Fica com a IA nativa.</summary>
        public int EnforcedDemandOn = -1;
        public string Crisis;
        /// <summary>Quem recusou as exigências por último (o outro lado ganha pretexto para guerra formal).</summary>
        public int CrisisRefuser = -1;
        public List<LogItem> Log = new List<LogItem>();
        /// <summary>Proposta de tratado pendente ("paz", "aliança", "fim da crise"): quem propôs, quem responde e quando.</summary>
        public PendingProposal Treaty;
        /// <summary>Proposta de acordo pendente ("acordo econômico (comércio)" etc.).</summary>
        public PendingProposal Agreement;
        /// <summary>Rendição nesta guerra (research\surrender.md): só com AtWar.</summary>
        public CapturedSurrender Surrender;
        /// <summary>Veredito do Congresso sobre a disputa (crise com status InternationalCrisisVoteEnded): quem venceu (-1 =
        /// nenhum) e, se esta nação perdeu, por que ela não pode cumprir ou ir à guerra surpresa agora (null = pode).</summary>
        public int CongressWinner = -1;
        public string VerdictAcceptBlocked;
        public string VerdictWarBlocked;
    }

    /// <summary>Rendição numa guerra, do ponto de vista de uma nação (a "minha" é a dona da relação).</summary>
    internal sealed class CapturedSurrender
    {
        /// <summary>Posso impor rendição (AllowToForceOtherToSurrender sem falha: apoio deles em 0, o meu acima de 0).</summary>
        public bool CanForce;
        public string ForceBlocked;
        /// <summary>Eles podem me impor rendição agora.</summary>
        public bool CanBeForced;
        /// <summary>Posso oferecer rendição (StartToFillSurrenderProposition sem falha).</summary>
        public bool CanOffer;
        public string OfferBlocked;
        /// <summary>Rascunhos meus abertos (de oferta e forçado, este também pela variante "ao aliado").</summary>
        public bool MyOfferDraft;
        public bool MyForcedDraft;
        /// <summary>Proposta enviada nesta guerra, esperando resposta.</summary>
        public CapturedSurrenderPending Pending;
        /// <summary>Prévia exata (InitializeSurrenderProposition) se eu me render a eles / se eu impuser a eles.</summary>
        public CapturedSurrenderPreview IfIOffer;
        public CapturedSurrenderPreview IfIForce;
    }

    internal sealed class CapturedSurrenderPending
    {
        public int Winner;
        public int Loser;
        public bool Forced;
        /// <summary>Quem responde: o perdedor na forçada, o vencedor na oferta.</summary>
        public int Responder;
        public int Score;
        public int Cost;
        public int Demands;
        public List<int> Territories = new List<int>();
        public bool Submission;
        public int Money;
    }

    internal sealed class CapturedSurrenderPreview
    {
        /// <summary>Placar do vencedor: o preço que os termos têm de gastar (sobra máxima de 5).</summary>
        public int Budget;
        public int DemandCount;
        public int DemandCost;
        /// <summary>Placar acima do custo das exigências: as exigências entram todas e o resto fica livre.</summary>
        public bool DemandsFixed;
        public List<CapturedSurrenderTerritory> Territories = new List<CapturedSurrenderTerritory>();
        public bool SubmissionAvailable;
        public int SubmissionCost;
        public string SubmissionBlocked;
        public int MoneyPerPoint;
    }

    internal sealed class CapturedSurrenderTerritory
    {
        public int Territory;
        /// <summary>"centro de São Paulo (ocupada)", "anexo de Santa Catarina", "posto".</summary>
        public string Detail;
        /// <summary>Pode entrar já (encosta na fronteira do vencedor ou é cidade ocupada); senão, só junto com Via.</summary>
        public bool Ready;
        public int Via = -1;
    }

    /// <summary>
    /// Reclamação do jogo (DiplomaticGrievanceInfo). Pool = índice no GrievanceAllocator do dono; o código no dossiê é
    /// "G" + Pool. O executor confere o tipo de novo antes de agir, porque o jogo reaproveita índices.
    /// </summary>
    internal sealed class CapturedGrievance
    {
        public int Pool;
        public string Type;
        public int TurnsLeft;
        public CapturedGain Gain;
        /// <summary>Motivo do jogo para não poder exigir agora (null = pode).</summary>
        public string Blocked;
    }

    /// <summary>Exigência em aberto (DiplomaticDemandInfo).</summary>
    internal sealed class CapturedDemand
    {
        public string Type;
        public int Turn;
        public CapturedGain Gain;
    }

    /// <summary>O que a exigência entrega a quem exige (DemandGainType + DemandGain).</summary>
    internal sealed class CapturedGain
    {
        /// <summary>Money, Territory, ForceCivic, ForceReligion ou DiplomaticAction.</summary>
        public string Kind;
        public int Amount;
        public int Territory = -1;
        /// <summary>Nomes de definição do cívico e da escolha (null = revogar o cívico); traduzidos no dossiê.</summary>
        public string Civic;
        public string CivicChoice;
        /// <summary>Ação diplomática exigida (DiplomaticAction) contra a nação Third.</summary>
        public string Action;
        public int Third = -1;
        /// <summary>false = o jogo já não consegue entregar isso (vira dinheiro).</summary>
        public bool Valid = true;
    }

    internal sealed class PendingProposal
    {
        public string Kind;
        public int From;
        public int Answerer;
        public int Turn;
    }

    internal sealed class LogItem
    {
        public int Turn;
        public int Initiator;
        public string Action;
    }

    /// <summary>Um lado de uma rota comercial: o comprador e os recursos (ResourceType) que compra do vendedor.</summary>
    internal sealed class CapturedTrade
    {
        public int Buyer;
        public int Seller;
        public bool Suspended;
        public List<int> Resources = new List<int>();
    }

    /// <summary>Pedágio de um turno: cada lado na sua moeda.</summary>
    internal sealed class CapturedToll
    {
        public int Owner;
        public int Payer;
        public double Received;
        public double Paid;
        public int Routes;
    }

    // ---------------- Congresso mundial (research\congress.md) ----------------

    internal sealed class CapturedCongress
    {
        public bool CivicUnlocked;
        public bool CrisisUnlocked;
        public bool ConsensusUnlocked;
        /// <summary>Quem formou o Congresso (preside a primeira sessão).</summary>
        public int Unlocker = -1;
        /// <summary>Ordem fixa da presidência das sessões.</summary>
        public int[] LeaderOrder = new int[0];
        /// <summary>Peso no Congresso de cada nação maior (o peso do voto).</summary>
        public double[] Sway = new double[0];
        /// <summary>Consulado funcionando (exigido para subornar e declarar crise).</summary>
        public bool[] Consulate = new bool[0];
        /// <summary>Alavancagem de cada nação contra cada outra: Leverage[i][j] = a da embaixada de i com j.</summary>
        public double[][] Leverage = new double[0][];
        /// <summary>Antes de o Congresso existir: o que falta a cada nação para formá-lo ("era", "consulado", "contatos").</summary>
        public string[] UnlockBlocked = new string[0];
        /// <summary>Era mínima e fração de nações conhecidas exigidas (RPN; -1 quando a foto veio da thread principal).</summary>
        public int MinEra = -1;
        public double MinMetFraction = -1;
        /// <summary>Sessões de lei até agora (0 = a primeira ainda não abriu e espera o primeiro presidente).</summary>
        public int SessionCount;
        public int NextLeader = -1;
        public int NextSessionTurn = -1;
        /// <summary>Leis que o próximo presidente pode propor.</summary>
        public List<CongressLaw> Proposable = new List<CongressLaw>();
        public CongressLawVote LawVote;
        /// <summary>Últimas votações de lei encerradas (a mais recente primeiro).</summary>
        public List<CongressLawVote> LawHistory = new List<CongressLawVote>();
        public List<CongressCrisis> Crises = new List<CongressCrisis>();
        public List<CongressAxis> Consensus = new List<CongressAxis>();
        /// <summary>Leis impostas pelo Congresso esperando adotar ou recusar (eventos de osmose CivicsShakedown).</summary>
        public List<CongressImposedLaw> ImposedLaws = new List<CongressImposedLaw>();
    }

    /// <summary>Lei internacional (cívico com IsInternational). O código no dossiê é "V" + Code (índice do cívico).</summary>
    internal sealed class CongressLaw
    {
        public int Code;
        public string Civic;
        public string ChoiceA;
        public string ChoiceB;
        /// <summary>Opção em vigor em cada nação maior: 0 = A, 1 = B, -1 = sem a lei.</summary>
        public int[] Current = new int[0];
    }

    internal sealed class CongressLawVote
    {
        public CongressLaw Law;
        public int Leader = -1;
        public int TurnBegin;
        public int TurnEnd;
        public string State;
        public bool Active;
        /// <summary>Nas encerradas: 0 = A venceu, 1 = B venceu, -1 = empate (nada muda).</summary>
        public int Winner = -1;
        public CongressBallot[] Ballots = new CongressBallot[0];
    }

    internal sealed class CongressBallot
    {
        /// <summary>Lei: 0 = A, 1 = B. Crise: a nação apoiada. -1 = não votou.</summary>
        public int Choice = -1;
        /// <summary>Peso gravado no voto (só de quem votou).</summary>
        public double Sway;
        /// <summary>Peso extra já comprado com suborno (entra no voto) e o bônus que entra no começo do próximo turno.</summary>
        public double BribeBonus;
        public double BribePending;
        /// <summary>Subornos possíveis agora contra cada alvo (índice = alvo; null = nenhum). Só para as nações do computador.</summary>
        public CongressBribe[] Bribes;
    }

    internal sealed class CongressBribe
    {
        /// <summary>Quantos subornos ainda dá para comprar contra esse alvo nesta votação.</summary>
        public int Remaining;
        public int Purchased;
        /// <summary>Alavancagem por suborno e peso ganho por suborno.</summary>
        public double Cost;
        public double Bonus;
        /// <summary>Quantos a alavancagem atual paga (limitado ao que resta).</summary>
        public int Affordable;
    }

    /// <summary>Votação de crise no Congresso. O código no dossiê é "K" + Pool.</summary>
    internal sealed class CongressCrisis
    {
        public int Pool;
        public int Declarator;
        public int Target;
        public int TurnBegin;
        public int TurnEnd;
        public string State;
        public bool Active;
        /// <summary>Encerrada, esperando o perdedor cumprir ou ir à guerra surpresa.</summary>
        public bool WaitingLoser;
        public int Winner = -1;
        public int Loser = -1;
        public CongressBallot[] Ballots = new CongressBallot[0];
    }

    /// <summary>Eixo do consenso ideológico. O código no dossiê é "I" + Index.</summary>
    internal sealed class CongressAxis
    {
        public int Index;
        public string Definition;
        public bool Unlocked;
        public int UnlockTurn = -1;
        public int Count;
        public int Required;
        public string Orientation;
        /// <summary>Custo em influência da próxima contribuição de cada nação maior (-1 = não lido) e quantas ela já fez.</summary>
        public double[] Cost = new double[0];
        public int[] Contributions = new int[0];
    }

    internal sealed class CongressImposedLaw
    {
        public int Empire;
        public int EventIndex;
        public string Civic;
        public string Choice;
        public int Proposer = -1;
        public double DiscardCost;
        public bool CanDiscard;
    }

    /// <summary>Quantos postos do dono cobram pedágio de (ou bloqueiam) o império afetado.</summary>
    internal sealed class CapturedTradeRule
    {
        public int Owner;
        public int Target;
        public bool Block;
        public int Posts;
        /// <summary>Pedágio por recurso e por turno, na moeda do dono: menor e maior entre os postos (só pedágio).</summary>
        public double MinPrice;
        public double MaxPrice;
    }
}
