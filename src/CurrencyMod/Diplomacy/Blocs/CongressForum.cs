using System.Linq;
using CurrencyMod.Diplomacy.Capture;

namespace CurrencyMod.Diplomacy.Blocs
{
    /// <summary>
    /// The World Congress's free agenda (melhorias item 40). Once the Congress exists, every living major nation sits in
    /// it and can propose free motions, exactly like in a bloc: 3 active motions each, a 2-turn vote, resolutions for 10
    /// turns and a book of accusations. Approved motions are ONLY recorded (Lucas, 2026-10-13): the mod applies no effect;
    /// each nation keeps its word or not, and the others judge. The game's own Congress laws stay as they are. It presides
    /// whoever presides the current (or the last) law session.
    /// </summary>
    internal static class CongressForum
    {
        internal const string Name = "Congresso Mundial";

        /// <summary>Creates the forum when the Congress appears and keeps its members and president in step with the game.</summary>
        internal static bool Sync(AiWorld world, WorldCapture capture, int turn)
        {
            CapturedCongress congress = capture?.Congress;
            if (world == null || congress == null || !(congress.CivicUnlocked || congress.CrisisUnlocked || congress.ConsensusUnlocked))
            {
                return false;
            }
            Bloc forum = world.CongressForum;
            bool changed = false;
            if (forum == null)
            {
                forum = new Bloc
                {
                    Id = 0,
                    Name = Name,
                    Founder = congress.Unlocker,
                    CreatedTurn = turn,
                    FormedTurn = turn,
                    TermStart = turn,
                    Migrated = true,
                };
                world.CongressForum = forum;
                forum.Log(turn, "formado");
                changed = true;
            }
            var members = capture.Empires.Where(e => e != null && e.Alive).Select(e => e.Index).OrderBy(e => e).ToList();
            if (!members.SequenceEqual(forum.Members))
            {
                forum.Members = members;
                changed = true;
            }
            int president = congress.LawVote != null && congress.LawVote.Leader >= 0 ? congress.LawVote.Leader
                : congress.LawHistory.Count > 0 && congress.LawHistory[0].Leader >= 0 ? congress.LawHistory[0].Leader
                : congress.Unlocker >= 0 ? congress.Unlocker : members.FirstOrDefault();
            if (president != forum.President && members.Contains(president))
            {
                forum.President = president;
                forum.TermStart = turn;
                forum.Log(turn, "presidente", president);
                changed = true;
            }
            return changed;
        }
    }
}
