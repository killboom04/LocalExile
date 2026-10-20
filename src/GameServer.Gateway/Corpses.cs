using GameServer.Core.Protocol.Instance;

/// <summary>
/// The area's monster corpses (issue #90). A dead monster's object stays in the area for
/// <see cref="LifetimeSeconds"/> so a corpse-targeting skill can still use it, and the variety, level and rarity it died
/// with are what Raise Spectre needs to put a minion back. The book holds no live monster state: only the identity (the
/// 0x2b9 object id), what died, where it fell and when it goes.
/// </summary>
sealed class CorpseBook
{
    /// <summary>How long a corpse object stays: long enough to click, short enough not to pile up.</summary>
    public const double LifetimeSeconds = 30;

    public sealed record Corpse(uint Id, MonsterCatalog.Variety Variety, byte Level, MonsterRarity Rarity, string Label,
        int X, int Y, double DiedAt)
    {
        public double ExpiresAt => DiedAt + LifetimeSeconds;
        public bool Expired(double now) => now >= ExpiresAt;
        public override string ToString() => $"{Label} 0x{Id:x}";
    }

    readonly Dictionary<uint, Corpse> _byId = [];

    public int Count => _byId.Count;
    public IReadOnlyCollection<Corpse> All => _byId.Values;

    public void Add(Corpse corpse) => _byId[corpse.Id] = corpse;

    /// <summary>The corpse as it is now, or null once its time ran out: a stale id must never raise.</summary>
    public Corpse? Get(uint id, double now)
    {
        Prune(now);
        return _byId.GetValueOrDefault(id);
    }

    /// <summary>Removes and returns the corpse: a raise consumes it (one corpse, one minion).</summary>
    public Corpse? Take(uint id, double now)
    {
        Prune(now);
        return _byId.Remove(id, out var corpse) ? corpse : null;
    }

    /// <summary>Forgets a corpse whose object was removed (or whose monster was re-raised).</summary>
    public bool Remove(uint id) => _byId.Remove(id);

    /// <summary>Drops the corpses whose time ran out; returns how many went.</summary>
    public int Prune(double now)
    {
        List<uint>? expired = null;
        foreach (var corpse in _byId.Values)
            if (corpse.Expired(now)) (expired ??= []).Add(corpse.Id);
        if (expired is null) return 0;
        foreach (uint id in expired) _byId.Remove(id);
        return expired.Count;
    }

    /// <summary>The nearest corpse to a point (/raise with no id), or null with none.</summary>
    public Corpse? Nearest(int x, int y, double now)
    {
        Prune(now);
        Corpse? best = null;
        double bestDistance = double.MaxValue;
        foreach (var corpse in _byId.Values)
        {
            double dx = corpse.X - x, dy = corpse.Y - y, distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = corpse;
        }
        return best;
    }
}

/// <summary>
/// Raise Spectre's limits (issue #90, wiki: Raise Spectre). The gem itself allows one spectre at level 1 and two from
/// level 13 (<c>base_number_of_spectres_allowed</c>), and the passive tree's <c>number_of_spectres_allowed</c> adds to it.
/// </summary>
static class Spectres
{
    /// <summary>How far a spectre will leave the player before it follows (the game's minion AI is not modelled).</summary>
    public const int FollowRange = 20;
    /// <summary>How far a spectre looks for a hostile monster to fight.</summary>
    public const int AggroRange = 40;
    /// <summary>How far from the point a cast was aimed a corpse still counts: the client sends the point, not always
    /// the corpse's id.</summary>
    public const int RaiseReach = 6;

    public static int Limit(int fromGem, int fromPassives) => Math.Max(1, Math.Max(fromGem, 0) + fromPassives);

    /// <summary>The limit from the two sources by stat name: the gem's and the character's.</summary>
    public static int Limit(Func<string, int> gem, Func<string, int> player) =>
        Limit(gem("base_number_of_spectres_allowed"), player("number_of_spectres_allowed"));

    /// <summary>
    /// The level a Raise Spectre raises at, from the gem's <c>raised_spectre_level</c> (28 at gem level 1): the corpse's
    /// own level is irrelevant. A gem without the stat (or with 0) raises level 1, and the level is a byte the monster
    /// tables accept, so it is clamped to 1..100.
    /// </summary>
    public static byte Level(Func<string, int> gem)
    {
        int level = gem("raised_spectre_level");
        return (byte)Math.Clamp(level <= 0 ? 1 : level, 1, 100);
    }
}
