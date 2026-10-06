using System;
using System.Collections.Generic;
using System.Text;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using SandboxStatic = Amplitude.Mercury.Sandbox.Sandbox;

namespace MoreEmpires
{
    /// <summary>
    /// Comando "imperios": lista os impérios maiores por índice e resume a diplomacia dos pares que envolvem os
    /// índices 10 a 15 (teste de aceitação 3: guerra, aliança, fronteiras, comércio e visão compartilhada).
    /// Só leitura; roda na thread principal enquanto a simulação pode estar mudando (é um retrato aproximado).
    /// </summary>
    internal static class EmpireReport
    {
        internal static string Describe()
        {
            var text = new StringBuilder();
            MajorEmpire[] majors = SandboxStatic.MajorEmpires;
            if (majors == null || SandboxStatic.NumberOfMajorEmpires == 0 || SandboxManager.Sandbox == null)
            {
                return "Sem partida carregada.";
            }
            int n = SandboxStatic.NumberOfMajorEmpires;
            int local = SandboxManager.Sandbox.LocalEmpireIndex;
            text.AppendLine($"{n} impérios maiores, {SandboxStatic.NumberOfMinorEmpires} menores; jogador local = {local}; turno {SandboxManager.Sandbox.Turn}.");
            for (int i = 0; i < n; i++)
            {
                MajorEmpire empire = majors[i];
                string faction = empire.FactionDefinition != null ? empire.FactionDefinition.Name.ToString().Replace("Faction_", string.Empty) : "?";
                string state = i == local ? "(você)" : Relation(local, i, out _);
                text.AppendLine($"  {i,2}: {empire.PersonaName} — {faction} — cor {empire.ColorIndex} — {(empire.IsAlive ? "vivo" : "eliminado")} — com você: {state}");
            }

            var counts = new Dictionary<string, int>();
            var lines = new List<string>();
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    if (a < Limits.VanillaMaxSlots && b < Limits.VanillaMaxSlots)
                    {
                        continue;
                    }
                    string state = Relation(a, b, out string agreements);
                    Count(counts, state);
                    foreach (string agreement in agreements.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        Count(counts, agreement);
                    }
                    if (state != "desconhecido")
                    {
                        lines.Add($"  {a,2}×{b,2}: {state}{(agreements.Length > 0 ? " — " + agreements : string.Empty)}");
                    }
                }
            }
            text.AppendLine("Pares com algum império de índice ≥ 10 (contagem): " + Summary(counts));
            foreach (string line in lines)
            {
                text.AppendLine(line);
            }
            return text.ToString();
        }

        private static void Count(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int value);
            counts[key] = value + 1;
        }

        private static string Summary(Dictionary<string, int> counts)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in counts)
            {
                parts.Add($"{pair.Key} {pair.Value}");
            }
            return parts.Count == 0 ? "-" : string.Join("; ", parts.ToArray());
        }

        private static string Relation(int a, int b, out string agreements)
        {
            agreements = string.Empty;
            try
            {
                DiplomaticRelation relation = SandboxStatic.DiplomaticAncillary.GetRelationFor(a, b);
                if (relation == null)
                {
                    return "?";
                }
                DiplomaticAbility ab = Abilities(relation, a, b) | Abilities(relation, b, a);
                var list = new List<string>();
                if ((ab & DiplomaticAbility.ShareVision) != 0) list.Add("visão compartilhada");
                else if ((ab & DiplomaticAbility.ShareMaps) != 0) list.Add("mapas compartilhados");
                if ((ab & (DiplomaticAbility.Trade | DiplomaticAbility.ExclusiveTrade)) != 0) list.Add("comércio");
                if ((ab & DiplomaticAbility.LuxuryTrade) != 0) list.Add("comércio de luxo");
                if ((ab & DiplomaticAbility.CrossTerritory) != 0) list.Add("fronteiras abertas");
                agreements = string.Join(", ", list.ToArray());
                switch (relation.CurrentState)
                {
                    case DiplomaticStateType.War: return "guerra";
                    case DiplomaticStateType.Alliance: return "aliança";
                    case DiplomaticStateType.Peace: return "paz";
                    case DiplomaticStateType.Unknown:
                    case DiplomaticStateType.PartialyKnown: return "desconhecido";
                    default: return relation.CurrentState.ToString();
                }
            }
            catch (Exception ex)
            {
                return "erro: " + ex.Message;
            }
        }

        private static DiplomaticAbility Abilities(DiplomaticRelation relation, int from, int to)
        {
            return (relation.LeftEmpireIndex == to ? relation.LeftAmbassy.Entity : relation.RightAmbassy.Entity).CurrentAbilities;
        }
    }
}
