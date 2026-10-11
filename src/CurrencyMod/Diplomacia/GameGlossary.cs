using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Framework;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Simulation;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Official game names, in the game's language, to anchor what the AIs say (design §9.1): resources and peoples.
    /// Read on the main thread, on demand, and cached: it does not change during the match.
    /// </summary>
    internal static class GameGlossary
    {
        internal sealed class ResourceEntry
        {
            public int Type;
            public string Name;
            public bool Luxury;
            public bool Strategic;
            /// <summary>Era in which the resource appears (-1 = from the start).</summary>
            public int Era;
        }

        private static ResourceEntry[] resources;
        private static List<string> cultures;

        /// <summary>Resources by ResourceType (empty slot = type with no resource).</summary>
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
            // Without the translation loaded we would get the internal name: only cache when every name came translated.
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

        /// <summary>Names of every people in the game: used to recognize each nation's former cultures in the letters.</summary>
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
                Plugin.Log?.LogWarning($"[IA] People names not read: {ex.Message}");
            }
            if (names.Count > 0)
            {
                cultures = names;
            }
            return names;
        }

        /// <summary>Translated name of a game definition (civic, civic choice...); null if there is none.</summary>
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
