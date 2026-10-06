using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Framework;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Nomes oficiais do jogo, na língua do jogo, para ancorar as falas das IAs (design §9.1): recursos e povos.
    /// Lido na thread principal, sob demanda, e guardado: não muda durante a partida.
    /// </summary>
    internal static class GameGlossary
    {
        internal sealed class ResourceEntry
        {
            public int Type;
            public string Name;
            public bool Luxury;
            public bool Strategic;
            /// <summary>Era em que o recurso aparece (-1 = desde o começo).</summary>
            public int Era;
        }

        private static ResourceEntry[] resources;
        private static List<string> cultures;

        /// <summary>Recursos por ResourceType (posição vazia = tipo sem recurso).</summary>
        internal static ResourceEntry[] Resources()
        {
            if (resources != null)
            {
                return resources;
            }
            ResourceDefinition[] definitions = DepartmentOfResources.ResourceDefinitionByResourceType;
            if (definitions == null)
            {
                return new ResourceEntry[0];
            }
            var table = new ResourceEntry[definitions.Length];
            bool complete = true;
            for (int i = 0; i < definitions.Length; i++)
            {
                ResourceDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }
                string name = LocalizedTitle(definition.Name);
                complete &= name != null;
                table[i] = new ResourceEntry
                {
                    Type = i,
                    Name = name ?? definition.Name.ToString(),
                    Luxury = definition is LuxuryResourceDefinition,
                    Strategic = definition is StrategicResourceDefinition,
                    Era = definition.EraOfApparition,
                };
            }
            // Sem a tradução carregada ficaria o nome interno: só guarda quando todos os nomes vieram traduzidos.
            if (complete)
            {
                resources = table;
            }
            return table;
        }

        internal static ResourceEntry Resource(int type)
        {
            ResourceEntry[] table = Resources();
            return type >= 0 && type < table.Length ? table[type] : null;
        }

        /// <summary>Nomes de todos os povos do jogo: reconhecem nas cartas as culturas antigas de cada nação.</summary>
        internal static List<string> Cultures()
        {
            if (cultures != null)
            {
                return cultures;
            }
            var names = new List<string>();
            try
            {
                foreach (FactionDefinition faction in Databases.GetDatabase<FactionDefinition>().GetValues())
                {
                    string name = faction != null ? LocalizedTitle(faction.Name) : null;
                    if (name != null)
                    {
                        names.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[IA] Nomes dos povos não lidos: {ex.Message}");
            }
            if (names.Count > 0)
            {
                cultures = names;
            }
            return names;
        }

        /// <summary>Nome traduzido de uma definição do jogo (cívico, escolha de cívico...); null se não houver.</summary>
        internal static string Title(string definitionName)
        {
            return string.IsNullOrEmpty(definitionName) ? null : LocalizedTitle(new StaticString(definitionName));
        }

        private static string LocalizedTitle(StaticString elementName)
        {
            try
            {
                if (Amplitude.Mercury.UI.Utils.DataUtils.TryGetLocalizedTitle(elementName, out string title))
                {
                    title = GameAccess.Clean(title);
                    if (!string.IsNullOrWhiteSpace(title) && !title.StartsWith("%"))
                    {
                        return title.Trim();
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
