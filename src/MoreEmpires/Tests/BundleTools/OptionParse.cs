using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Heuristic parser for serialized GameOptionDefinition objects inside an uncompressed Unity CAB file.
public static class OptionParse
{
    static byte[] data;
    static int pos;

    static int I32() { int v = BitConverter.ToInt32(data, pos); pos += 4; return v; }
    static void Align() { pos = (pos + 3) & ~3; }
    static string Str()
    {
        int len = I32();
        if (len < 0 || len > 4096 || pos + len > data.Length) throw new Exception("bad string len " + len);
        string s = Encoding.UTF8.GetString(data, pos, len);
        pos += len; Align();
        return s;
    }
    static bool Bool() { bool b = data[pos] != 0; pos += 1; return b; }
    static int Count() { int c = I32(); if (c < 0 || c > 2000) throw new Exception("bad count " + c); return c; }

    public static string Run(string binPath, string prefix)
    {
        data = File.ReadAllBytes(binPath);
        var sb = new StringBuilder();
        byte[] pat = Encoding.ASCII.GetBytes(prefix);
        var seen = new HashSet<string>();
        for (int i = 4; i < data.Length - pat.Length; i++)
        {
            bool match = true;
            for (int k = 0; k < pat.Length; k++) if (data[i + k] != pat[k]) { match = false; break; }
            if (!match) continue;
            int len = BitConverter.ToInt32(data, i - 4);
            if (len < pat.Length || len > 200 || i + len > data.Length) continue;
            string name = Encoding.ASCII.GetString(data, i, len);
            bool ok = true;
            foreach (char ch in name) if (ch < 32 || ch > 126) { ok = false; break; }
            if (!ok) continue;
            pos = i + len; Align();
            int start = i - 4;
            try
            {
                var rec = new StringBuilder();
                string def = Str();
                string presetFallback = Str();
                int nStates = Count();
                var states = new List<string>();
                for (int s = 0; s < nStates; s++)
                {
                    string val = Str();
                    int nKv = Count();
                    var kvs = new List<string>();
                    for (int k = 0; k < nKv; k++) { string kk = Str(); string vv = Str(); kvs.Add(kk + "=" + vv); }
                    int nItems = Count();
                    var items = new List<string>();
                    for (int k = 0; k < nItems; k++) { string vv = Str(); string other = Str(); items.Add(other + "=" + vv); }
                    states.Add("    State '" + val + "'" + (kvs.Count > 0 ? " KV[" + string.Join(", ", kvs) + "]" : "") + (items.Count > 0 ? " Preset[" + string.Join(", ", items) + "]" : ""));
                }
                int nCons = Count();
                var cons = new List<string>();
                for (int c = 0; c < nCons; c++)
                {
                    int op = I32();
                    int nCond = Count();
                    var conds = new List<string>();
                    for (int k = 0; k < nCond; k++)
                    {
                        string v = Str(); bool not = Bool(); Align(); string other = Str();
                        conds.Add((not ? "NOT " : "") + other + "=" + v);
                    }
                    int nThen = Count();
                    var thens = new List<string>();
                    for (int k = 0; k < nThen; k++) { string v = Str(); int vis = I32(); thens.Add((string.IsNullOrEmpty(v) ? "<option>" : v) + ":" + vis); }
                    cons.Add("    Constraint " + (op == 0 ? "AND" : "OR") + " If[" + string.Join(" ; ", conds) + "] Then[" + string.Join(" ; ", thens) + "]");
                }
                bool canRandom = Bool(); Align();
                string randomState = Str();
                bool editable = Bool();
                byte key = data[pos];
                string sig = name + "|" + def + "|" + nStates + "|" + nCons;
                if (!seen.Add(sig)) continue;
                rec.AppendLine("== " + name + " @" + start + "  Default='" + def + "' PresetFallback='" + presetFallback + "' States=" + nStates + " Constraints=" + nCons + " CanBeRandomized=" + canRandom + " RandomState='" + randomState + "' Editable=" + editable + " Key=" + key);
                foreach (var s in states) rec.AppendLine(s);
                foreach (var c in cons) rec.AppendLine(c);
                sb.Append(rec);
            }
            catch (Exception)
            {
            }
        }
        return sb.ToString();
    }
}
