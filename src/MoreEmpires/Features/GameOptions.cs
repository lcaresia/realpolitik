using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Amplitude;
using Amplitude.Framework;
using Amplitude.Framework.Options;
using Amplitude.Mercury.Data.GameOptions;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Options;
using Amplitude.Mercury.Presentation;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.Terrain;
using Amplitude.Mercury.UI.Helpers;
using HarmonyLib;
using OptionKeyValuePair = Amplitude.Framework.Options.KeyValuePair;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefas 1 e 2 — Opção GameOption_SlotCount e limite de jogadores por tamanho de mapa.
    //
    // Dados reais (lidos do bundle MercuryDatabases.AvatarPresentation, ver docs\pesquisa-30-jogadores.md):
    //   GameOption_SlotCount: estados "2".."10" (KV SlotCount=N, NumberOfMajorFactions=N), padrão "6", sem constraints.
    //   GameOption_WorldSize: 2 constraints  → Tiny inválido com SlotCount 5..10 (máx. 4), Small inválido com 7..10 (máx. 6).
    //   GameOption_PercentageOfLandmassOverWater: OR SlotCount 4..10 → 10% e 20% inválidos (liga jogadores a % de terra,
    //   não ao tamanho; fica, mas a lista ganha 11..MaxImperios para continuar valendo "4 ou mais jogadores").
    //
    // Os constraints são lidos por Option.cs (98-102, 350-353), GameOptionsManager.cs (745-790) e GameOptionUtils.cs (35-145).
    // Por isso mexemos na definição ANTES de o Option ser criado: prefixo em GameOptionsManager.CreateOption, chamado por
    // OptionsManager.Load (OptionsManager.cs:114-117) para cada definição, antes de qualquer Option.Load.
    // ===================================================================================================

    [HarmonyPatch(typeof(GameOptionsManager), "CreateOption")]
    internal static class GameOptionsManager_CreateOption_Patch
    {
        private static void Prefix(GameOptionDefinition definition)
        {
            try
            {
                OptionTweaks.CreateOptionCalls++;
                OptionTweaks.Apply(definition);
            }
            catch (Exception ex)
            {
                Log.Exception("GameOptionsManager.CreateOption", ex);
            }
        }
    }

    internal static class OptionTweaks
    {
        internal static int CreateOptionCalls;

        /// <summary>Máximo de jogadores que cada tamanho aceitava no jogo original (só os tamanhos que tinham limite).</summary>
        internal static readonly Dictionary<string, int> OriginalMaxPlayersBySize = new Dictionary<string, int>();

        /// <summary>Passo 1: constraints originais que citam SlotCount ou WorldSize (antes de qualquer mudança).</summary>
        internal static readonly List<string> OriginalDump = new List<string>();

        /// <summary>O que o mod mudou nas definições.</summary>
        internal static readonly List<string> Changes = new List<string>();

        private static readonly HashSet<GameOptionDefinition> applied = new HashSet<GameOptionDefinition>();

        internal static string NameOf(OptionDefinition definition)
        {
            if (definition == null)
            {
                return string.Empty;
            }
            try
            {
                string name = definition.Name.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            catch
            {
            }
            return definition.name;
        }

        internal static string OtherName(OptionConstraint.Condition condition)
        {
            string name = condition.serializableOtherOptionName;
            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    name = condition.OtherOptionName.ToString();
                }
                catch
                {
                }
            }
            return name ?? string.Empty;
        }

        private static bool TryNumber(string value, out int number)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        internal static void Apply(GameOptionDefinition definition)
        {
            if (definition == null)
            {
                return;
            }
            lock (applied)
            {
                if (!applied.Add(definition))
                {
                    return;
                }
            }
            string name = NameOf(definition);
            DumpIfRelevant(definition, name);
            if (!Limits.Active)
            {
                return;
            }
            if (name == Limits.SlotCountOption)
            {
                AdjustSlotCountStates(definition);
                RemoveConstraintsLinkedTo(definition, name, Limits.WorldSizeOption);
            }
            else if (name == Limits.WorldSizeOption)
            {
                RemoveConstraintsLinkedTo(definition, name, Limits.SlotCountOption);
            }
            ExtendSlotCountOrLists(definition, name);
        }

        /// <summary>Acrescenta os estados "11".."MaxImperios" copiando o formato do "10" (ou remove os que passam do máximo).</summary>
        private static void AdjustSlotCountStates(GameOptionDefinition definition)
        {
            OptionState[] states = definition.States ?? new OptionState[0];
            int maxValue = int.MinValue;
            int templateIndex = -1;
            for (int i = 0; i < states.Length; i++)
            {
                if (TryNumber(states[i].Value, out int n) && n > maxValue)
                {
                    maxValue = n;
                    templateIndex = i;
                }
            }
            if (templateIndex < 0)
            {
                Log.Error("GameOption_SlotCount sem estados numéricos; nada foi alterado.");
                return;
            }
            int max = Limits.MaxImperios;
            if (max > maxValue)
            {
                OptionState template = states[templateIndex];
                var list = new List<OptionState>(states);
                for (int n = maxValue + 1; n <= max; n++)
                {
                    OptionState state = template;
                    state.Value = n.ToString(CultureInfo.InvariantCulture);
                    if (template.KeyValuePairs != null)
                    {
                        state.KeyValuePairs = template.KeyValuePairs
                            .Select(kv => new OptionKeyValuePair { Key = kv.Key, Value = kv.Value == template.Value ? state.Value : kv.Value })
                            .ToArray();
                    }
                    list.Add(state);
                }
                definition.States = list.ToArray();
                Changes.Add($"GameOption_SlotCount: estados {maxValue + 1}..{max} acrescentados (cópia do \"{template.Value}\").");
            }
            else if (max < maxValue)
            {
                definition.States = states.Where(s => !TryNumber(s.Value, out int n) || n <= max).ToArray();
                if (TryNumber(definition.Default, out int def) && def > max)
                {
                    definition.Default = max.ToString(CultureInfo.InvariantCulture);
                }
                Changes.Add($"GameOption_SlotCount: estados acima de {max} removidos (MaxImperios = {max}).");
            }
        }

        /// <summary>
        /// Remove só os constraints de <paramref name="ownerName"/> que dependem de <paramref name="otherName"/>
        /// (jogadores × tamanho do mapa). Os demais ficam intactos.
        /// </summary>
        private static void RemoveConstraintsLinkedTo(GameOptionDefinition definition, string ownerName, string otherName)
        {
            OptionConstraint[] constraints = definition.Constraints;
            if (constraints == null || constraints.Length == 0)
            {
                return;
            }
            var kept = new List<OptionConstraint>();
            foreach (OptionConstraint constraint in constraints)
            {
                bool linked = constraint.Conditions != null && constraint.Conditions.Any(c => OtherName(c) == otherName);
                if (!linked)
                {
                    kept.Add(constraint);
                    continue;
                }
                Changes.Add($"{ownerName}: constraint removido → {Describe(constraint)}");
                if (ownerName == Limits.WorldSizeOption)
                {
                    RecordOriginalMax(constraint);
                }
            }
            if (kept.Count != constraints.Length)
            {
                definition.Constraints = kept.ToArray();
            }
        }

        private static void RecordOriginalMax(OptionConstraint constraint)
        {
            int minInvalid = int.MaxValue;
            foreach (OptionConstraint.Condition condition in constraint.Conditions)
            {
                if (OtherName(condition) == Limits.SlotCountOption && !condition.Not && TryNumber(condition.Value, out int n))
                {
                    minInvalid = System.Math.Min(minInvalid, n);
                }
            }
            if (minInvalid == int.MaxValue || constraint.Consequences == null)
            {
                return;
            }
            foreach (OptionConstraint.Consequence consequence in constraint.Consequences)
            {
                if (string.IsNullOrEmpty(consequence.Value) || VisibilityHelper.IsValid(consequence.Visibility))
                {
                    continue;
                }
                int max = minInvalid - 1;
                if (!OriginalMaxPlayersBySize.TryGetValue(consequence.Value, out int current) || max < current)
                {
                    OriginalMaxPlayersBySize[consequence.Value] = max;
                }
            }
        }

        /// <summary>
        /// Constraints do tipo "OR SlotCount=k .. SlotCount=10" significam "k ou mais jogadores". Para continuarem valendo com
        /// 11..16 jogadores, a lista ganha as condições que faltam (mesmo formato).
        /// </summary>
        private static void ExtendSlotCountOrLists(GameOptionDefinition definition, string ownerName)
        {
            OptionConstraint[] constraints = definition.Constraints;
            if (constraints == null)
            {
                return;
            }
            int max = Limits.MaxImperios;
            for (int i = 0; i < constraints.Length; i++)
            {
                OptionConstraint constraint = constraints[i];
                if (constraint.Operator != OptionConstraint.Operators.OR || constraint.Conditions == null)
                {
                    continue;
                }
                var slotConditions = constraint.Conditions
                    .Where(c => OtherName(c) == Limits.SlotCountOption && !c.Not && TryNumber(c.Value, out _))
                    .ToList();
                if (slotConditions.Count == 0)
                {
                    continue;
                }
                int listedMax = slotConditions.Max(c => int.Parse(c.Value, CultureInfo.InvariantCulture));
                if (listedMax != Limits.VanillaMaxSlots || max <= listedMax)
                {
                    continue;
                }
                OptionConstraint.Condition template = slotConditions.First(c => c.Value == listedMax.ToString(CultureInfo.InvariantCulture));
                var conditions = new List<OptionConstraint.Condition>(constraint.Conditions);
                for (int n = listedMax + 1; n <= max; n++)
                {
                    OptionConstraint.Condition condition = template;
                    condition.Value = n.ToString(CultureInfo.InvariantCulture);
                    conditions.Add(condition);
                }
                constraints[i].Conditions = conditions.ToArray();
                Changes.Add($"{ownerName}: condição \"SlotCount ≥ {slotConditions.Min(c => int.Parse(c.Value, CultureInfo.InvariantCulture))}\" estendida até {max} → {Describe(constraints[i])}");
            }
        }

        private static void DumpIfRelevant(GameOptionDefinition definition, string name)
        {
            bool relevant = name == Limits.SlotCountOption || name == Limits.WorldSizeOption;
            if (definition.Constraints != null)
            {
                foreach (OptionConstraint constraint in definition.Constraints)
                {
                    if (constraint.Conditions != null && constraint.Conditions.Any(c => OtherName(c) == Limits.SlotCountOption || OtherName(c) == Limits.WorldSizeOption))
                    {
                        relevant = true;
                    }
                }
            }
            if (!relevant)
            {
                return;
            }
            string text = DescribeDefinition(definition, name);
            OriginalDump.Add(text);
            Log.Info("[Opções] Original: " + text.Replace("\n", "\n    "));
        }

        internal static string DescribeDefinition(GameOptionDefinition definition, string name)
        {
            var text = new StringBuilder();
            text.Append($"{name} (padrão '{definition.Default}', aleatório={definition.CanBeRandomized}/'{definition.RandomState}')");
            if (definition.States != null)
            {
                text.Append("\n  estados: ");
                text.Append(string.Join(", ", definition.States.Select(s =>
                    s.Value + (s.KeyValuePairs != null && s.KeyValuePairs.Length > 0 ? "[" + string.Join(" ", s.KeyValuePairs.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "]" : string.Empty)).ToArray()));
            }
            if (definition.Constraints != null)
            {
                foreach (OptionConstraint constraint in definition.Constraints)
                {
                    text.Append("\n  constraint: ").Append(Describe(constraint));
                }
            }
            return text.ToString();
        }

        internal static string Describe(OptionConstraint constraint)
        {
            string conditions = constraint.Conditions == null ? string.Empty
                : string.Join(constraint.Operator == OptionConstraint.Operators.OR ? " OU " : " E ",
                    constraint.Conditions.Select(c => (c.Not ? "NÃO " : string.Empty) + OtherName(c).Replace("GameOption_", string.Empty) + "=" + c.Value).ToArray());
            string consequences = constraint.Consequences == null ? string.Empty
                : string.Join(", ", constraint.Consequences.Select(c => (string.IsNullOrEmpty(c.Value) ? "<opção>" : c.Value) + ":" + c.Visibility).ToArray());
            return $"SE {conditions} ENTÃO {consequences}";
        }

        /// <summary>
        /// Caminho de segurança: se o plugin carregou depois de as opções serem criadas (não deveria acontecer: o BepInEx
        /// carrega antes do menu), aplica as mudanças e reconstrói as tabelas internas de cada Option.
        /// </summary>
        internal static void ApplyLate(GameOptionsManager manager)
        {
            var options = manager.AllOptions.ToList();
            if (options.Count == 0)
            {
                return;
            }
            foreach (Option<GameOptionDefinition> option in options)
            {
                Apply(option.OptionDefinition);
            }
            foreach (Option<GameOptionDefinition> option in options)
            {
                option.BuildVisibilityData();
            }
            foreach (Option<GameOptionDefinition> option in options)
            {
                option.BuildImpactedOptionsList();
            }
            manager.RefreshAllOptions();
            Log.Warn($"[Opções] Ajustes aplicados tarde em {options.Count} opções (o plugin carregou depois do jogo criar as opções).");
        }

        internal static string Report()
        {
            var text = new StringBuilder();
            text.AppendLine($"MaxImperios = {Limits.MaxImperios} ({(Limits.Active ? "ativo" : "original")}); CreateOption chamado {CreateOptionCalls} vez(es).");
            text.AppendLine("== Passo 1: definições originais que citam SlotCount/WorldSize ==");
            foreach (string line in OriginalDump)
            {
                text.AppendLine(line);
            }
            text.AppendLine("== Mudanças feitas ==");
            foreach (string line in Changes)
            {
                text.AppendLine(line);
            }
            text.AppendLine("== Máximo original por tamanho (constraints removidos) ==");
            foreach (KeyValuePair<string, int> pair in OriginalMaxPlayersBySize)
            {
                text.AppendLine($"{pair.Key}: {pair.Value}");
            }
            try
            {
                if (Services.GetService<IGameOptionsService>() is GameOptionsManager manager)
                {
                    text.AppendLine("== Estado atual no jogo ==");
                    foreach (Option<GameOptionDefinition> option in manager.AllOptions)
                    {
                        string name = NameOf(option.OptionDefinition);
                        if (name != Limits.SlotCountOption && name != Limits.WorldSizeOption && name != "GameOption_PercentageOfLandmassOverWater")
                        {
                            continue;
                        }
                        var states = new List<string>();
                        for (int i = 0; i < option.StatesCount; i++)
                        {
                            states.Add(option.GetState(i).Value + ":" + option.GetStateVisibility(i));
                        }
                        text.AppendLine($"{name} = '{option.CurrentValue}' → {string.Join(", ", states.ToArray())}");
                    }
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("(estado atual indisponível: " + ex.Message + ")");
            }
            return text.ToString();
        }
    }

    // ===================================================================================================
    // Mapas customizados: OutgameUtils.cs:377-379 marcava empiresCount > 10 como TooManyStartingPoint.
    // Agora o limite é MaxImperios. (WorldMapProviderHelper.GetMaxPlayablePlayerCount, no assembly Terrain, já vai até 16.)
    // ===================================================================================================

    [HarmonyPatch(typeof(OutgameUtils.Maps), nameof(OutgameUtils.Maps.FillMapValidationFailureFlags))]
    internal static class OutgameUtils_FillMapValidationFailureFlags_Patch
    {
        private static void Postfix(ref TerrainSaveDescriptor terrainSaveDescriptor, ref MapValidationFailureFlags __result)
        {
            try
            {
                if (!Limits.Active || terrainSaveDescriptor.IsNull)
                {
                    return;
                }
                __result &= ~MapValidationFailureFlags.TooManyStartingPoint;
                if (terrainSaveDescriptor.EmpiresCount > Limits.MaxImperios)
                {
                    __result |= MapValidationFailureFlags.TooManyStartingPoint;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("OutgameUtils.FillMapValidationFailureFlags", ex);
            }
        }
    }

    // ===================================================================================================
    // Estrelas de era: EraStarLevelParameters tem páginas por GameOption_SlotCount com entradas só de "2" a "10"
    // (Expansionista, Diplomata e o catch-up). DepartmentOfDevelopment.cs:1446-1485 procura o valor da sessão; se não
    // acha, loga um aviso A CADA chamada e usa o DefaultValue (5 ou 6 jogadores). Estendemos as páginas até MaxImperios
    // continuando a série (diferenças de 2ª ordem: exata para as séries lineares e quadráticas que o jogo usa).
    // ===================================================================================================

    [HarmonyPatch(typeof(DepartmentOfDevelopment), "FillEraStarThresholdParams")]
    internal static class DepartmentOfDevelopment_FillEraStarThresholdParams_Patch
    {
        private static void Prefix(EraStarLevelParameters parametersDefinition)
        {
            try
            {
                if (Limits.Active)
                {
                    EraStarPages.Extend(parametersDefinition);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("FillEraStarThresholdParams", ex);
            }
        }
    }

    internal static class EraStarPages
    {
        internal static readonly List<string> Changes = new List<string>();

        private static string maxName;

        internal static void Extend(EraStarLevelParameters parameters)
        {
            EraStarLevelParameters.Page[] pages = parameters.Pages;
            if (pages == null)
            {
                return;
            }
            if (maxName == null)
            {
                maxName = Limits.DataTablesMax.ToString(CultureInfo.InvariantCulture);
            }
            for (int p = 0; p < pages.Length; p++)
            {
                if (pages[p].GameOptionName != Limits.SlotCountOption || pages[p].Entries == null)
                {
                    continue;
                }
                EraStarLevelParameters.Entry[] entries = pages[p].Entries;
                bool alreadyExtended = false;
                for (int e = entries.Length - 1; e >= 0; e--)
                {
                    if (entries[e].ValueName == maxName)
                    {
                        alreadyExtended = true;
                        break;
                    }
                }
                if (alreadyExtended)
                {
                    continue;
                }
                var numeric = new SortedDictionary<int, int>();
                foreach (EraStarLevelParameters.Entry entry in entries)
                {
                    if (int.TryParse(entry.ValueName, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    {
                        numeric[n] = entry.Parameter.RawValue;
                    }
                }
                if (numeric.Count < 3)
                {
                    continue;
                }
                int last = numeric.Keys.Max();
                if (last >= Limits.DataTablesMax)
                {
                    continue;
                }
                int[] keys = numeric.Keys.ToArray();
                int c = numeric[keys[keys.Length - 1]];
                int b = numeric[keys[keys.Length - 2]];
                int a = numeric[keys[keys.Length - 3]];
                if (keys[keys.Length - 1] - keys[keys.Length - 2] != 1 || keys[keys.Length - 2] - keys[keys.Length - 3] != 1)
                {
                    continue;
                }
                long d1 = c - b;
                long d2 = (c - b) - (b - a);
                var list = new List<EraStarLevelParameters.Entry>(entries);
                long value = c;
                var added = new List<string>();
                for (int n = last + 1; n <= Limits.DataTablesMax; n++)
                {
                    d1 += d2;
                    value += d1;
                    var entry = new EraStarLevelParameters.Entry
                    {
                        ValueName = n.ToString(CultureInfo.InvariantCulture),
                        Parameter = default(FixedPoint),
                    };
                    entry.Parameter.RawValue = (int)System.Math.Max(int.MinValue, System.Math.Min(int.MaxValue, value));
                    list.Add(entry);
                    added.Add($"{n}={(float)entry.Parameter:0.###}");
                }
                pages[p].Entries = list.ToArray();
                string line = $"Estrelas de era: página {Limits.SlotCountOption} estendida ({string.Join(", ", added.ToArray())}).";
                lock (Changes)
                {
                    Changes.Add(line);
                }
                Log.Info(line);
            }
        }
    }
}
