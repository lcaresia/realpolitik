using System;
using System.Collections.Generic;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Dados só para o visualizador e o log (não vão para o save): últimos dossiês, prompts, respostas
    /// cruas e chamadas de cada nação, mais a lista de eventos. Só a thread principal mexe aqui.
    /// </summary>
    internal sealed class IaRuntime
    {
        public List<string> Events = new List<string>();
        public Dictionary<int, NationDetail> Details = new Dictionary<int, NationDetail>();

        public NationDetail Detail(int empireIndex)
        {
            if (!Details.TryGetValue(empireIndex, out NationDetail detail))
            {
                detail = new NationDetail();
                Details[empireIndex] = detail;
            }
            return detail;
        }

        public void Event(string text)
        {
            Events.Add($"{DateTime.Now:HH:mm:ss}  {text}");
            if (Events.Count > 400)
            {
                Events.RemoveRange(0, Events.Count - 400);
            }
        }
    }

    internal sealed class NationDetail
    {
        public int Version;
        public int Turn = -1;
        public string State = "sem dados";
        public string LastError;
        public string Persona;
        public string Dossier;
        public string System;
        public string User;
        public string Raw;
        public string Reasoning;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<CallRecord> Calls = new List<CallRecord>();
    }

    internal sealed class CallRecord
    {
        public int Turn;
        public int Attempt;
        public bool Ok;
        public string Error;
        public string Errors;
        public double Seconds;
        public int Prompt;
        public int Hit;
        public int Miss;
        public int Completion;
        public int Reasoning;
        public double Cost;
        public string FinishReason;
        /// <summary>Provedor e modelo que responderam (ou o último que falhou).</summary>
        public string Provider;
    }
}
