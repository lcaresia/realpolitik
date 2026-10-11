using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// "O que isso significa": fatos do jogo por trás de uma carta da IA, sem chamada à API. O que o remetente fez com o
    /// leitor no turno em que escreveu (acordo proposto, presente, exigência, guerra, paz, pedágio) e as regras do mod que
    /// a carta costuma citar em metáfora (a "praça fechada" = declaração pública só a cada N turnos).
    /// </summary>
    internal static class LetterContext
    {
        private static readonly string[] PublicHints =
        {
            "praça", "público", "pública", "publico", "publica", "mundo", "proclam", "anunci", "grit", "declara", "todos",
            "square", "public", "world", "proclaim", "announce", "plaza", "monde", "place publique", "welt", "öffentlich",
        };

        /// <summary>Linhas de contexto (no idioma da interface) para uma carta recebida pelo leitor; vazio se não há nada.</summary>
        internal static List<string> For(IaWorld world, Letter letter, int reader)
        {
            var lines = new List<string>();
            if (world == null || letter == null || letter.From == reader)
            {
                return lines;
            }
            IaNation sender = world.Nations.FirstOrDefault(n => n.EmpireIndex == letter.From);
            if (sender == null)
            {
                return lines;
            }
            string target = "E" + reader;
            foreach (ActionRecord action in sender.Actions.Where(a => a.Turn == letter.SentTurn))
            {
                string line = Describe(action, target);
                if (line != null && !lines.Contains(line))
                {
                    lines.Add(line);
                }
            }

            // Declaração pública: a regra é 1 a cada N turnos. Quando a carta fala de praça/mundo e o remetente estava
            // bloqueado no turno em que escreveu, diz até quando.
            int interval = Math.Max(1, IaConfig.PublicDeclarationInterval.Value);
            int lastPublic = world.Letters.Where(l => l.From == letter.From && l.IsPublic && l.SentTurn <= letter.SentTurn)
                .Select(l => l.SentTurn).DefaultIfEmpty(-999).Max();
            string lower = ((letter.Subject ?? string.Empty) + " " + (letter.Text ?? string.Empty)).ToLowerInvariant();
            if (!letter.IsPublic && letter.SentTurn - lastPublic < interval && PublicHints.Any(h => lower.Contains(h)))
            {
                lines.Add(L.F("Declaração pública: a última dele foi no turno {0}; pela regra (uma a cada {1} turnos), a próxima só a partir do turno {2}.",
                    lastPublic, interval, lastPublic + interval));
            }
            return lines;
        }

        private static string Describe(ActionRecord action, string target)
        {
            JObject json;
            try
            {
                json = JObject.Parse(action.Json ?? "{}");
            }
            catch (Exception)
            {
                return null;
            }
            if (!string.Equals((string)json["nacao"], target, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string failed = action.Status != null && (action.Status.StartsWith("não ") || action.Status.Contains("falh") || action.Status.Contains("recus") || action.Status.Contains("inválid"))
                ? " " + L.T("(o jogo não aceitou)") : string.Empty;
            switch (action.Name)
            {
                case "propor_acordo":
                    return L.F("No mesmo turno, propôs a você um acordo {0} (veja na diplomacia).", AgreementUi((string)json["acordo"])) + failed;
                case "romper_acordo":
                    return L.F("No mesmo turno, rompeu o acordo {0} com você.", AgreementUi((string)json["acordo"])) + failed;
                case "presentear":
                    return L.T("No mesmo turno, mandou um presente a você (veja na diplomacia).") + failed;
                case "declarar_guerra":
                    return L.T("No mesmo turno, declarou guerra a você.") + failed;
                case "propor_paz":
                    return L.T("No mesmo turno, propôs paz a você.") + failed;
                case "exigir":
                    return L.T("No mesmo turno, transformou queixas contra você em exigências formais.") + failed;
                case "responder_acordo":
                case "responder_tratado":
                    return ((string)json["resposta"]) == "aceitar"
                        ? L.T("No mesmo turno, aceitou a sua proposta.") + failed
                        : L.T("No mesmo turno, deixou a sua proposta sem resposta.");
                case "responder_exigencias":
                    string answer = (string)json["resposta"];
                    return (answer == "aceitar" ? L.T("No mesmo turno, cedeu às suas exigências.") : answer == "recusar" ? L.T("No mesmo turno, recusou as suas exigências.") : L.T("No mesmo turno, enrolou as suas exigências."))
                        + failed;
                case "politica_comercial":
                    string mode = (string)json["modo"];
                    return (mode == "bloqueio" ? L.T("No mesmo turno, bloqueou as suas rotas nos postos comerciais dele.")
                        : mode == "pedagio" ? L.T("No mesmo turno, passou a cobrar pedágio das suas rotas nos postos dele.")
                        : L.T("No mesmo turno, liberou as suas rotas nos postos dele.")) + failed;
                default:
                    return null;
            }
        }

        private static string AgreementUi(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "economico": return L.T("econômico");
                case "informacao": return L.T("de informação");
                case "cultural": return L.T("cultural");
                case "militar": return L.T("militar");
                case "alianca": return L.T("de aliança");
                default: return kind ?? "?";
            }
        }
    }
}
