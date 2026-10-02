namespace GraveyardKeeper2Accessibility;

/// <summary>
/// Speaks every gain and loss of the player's own numbers: "3 energy used", "got 2 red points",
/// "got 5 silver". The GK1 mod did this for health and energy, and it was one of the things players
/// missed most when it was not there.
///
/// <para>
/// <b>Why poll rather than hook.</b> Every one of these is a <c>GK2GameResSystem</c>, and each has
/// an <c>onValueDeltaChanged</c> event - but the subclasses override <c>Add</c> and <c>Set</c>
/// inconsistently, some paths skip the systems entirely (<c>SetResWithoutSystemsCheck</c>,
/// <c>AddResWithoutSystemsCheck</c>), and the systems are rebuilt with every save that loads, so a
/// subscription would have to be renewed and would still miss changes. Reading seven floats a
/// frame catches every route in and costs nothing. This is the same trade the GK1 mod made, for the
/// same reason.
/// </para>
///
/// <para>
/// <b>Changes are gathered before they are spoken.</b> One swing of a tool, one bite of food or one
/// sale can move a value several times in a few frames. The announcer waits for the numbers to
/// stop moving and speaks the net result once. A value that never settles - energy ticking up under
/// a regeneration potion - is still spoken every few seconds rather than held back indefinitely.
/// </para>
///
/// <para>
/// Only whole units are spoken, and the fraction left over is carried into the next change rather
/// than thrown away, so half a point of energy per swing still adds up to "1 energy used" every
/// second swing instead of never being mentioned at all.
/// </para>
/// </summary>
internal static class ResourceAnnouncer
{
    private sealed class Watched
    {
        public readonly string Res;
        public readonly Func<int, string> Gained;
        public readonly Func<int, string> Lost;
        public float Baseline;
        public float Last;

        public Watched(string res, Func<int, string> gained, Func<int, string> lost)
        {
            Res = res;
            Gained = gained;
            Lost = lost;
        }
    }

    private static ManualLogSource _log;
    private static ConfigEntry<bool> _enabled;

    private static readonly List<Watched> Resources = new();

    /// <summary>
    /// The save the baselines were read from. A different one means a load or a new game, and its
    /// numbers are a starting point, not a change - announcing the whole energy bar as a gain on
    /// every load would be noise.
    /// </summary>
    private static PlayerData _baselineFor;

    private static float _lastChangeAt;
    private static float _pendingSince = -1f;

    /// <summary>Quiet time after the last change before the net result is spoken.</summary>
    private const float SettleDelay = 0.4f;

    /// <summary>A value that keeps moving is still spoken at least this often.</summary>
    private const float MaxHold = 3f;

    internal static void Init(ManualLogSource log, ConfigFile config)
    {
        _log = log;
        _enabled = config.Bind("Announcements", "ResourceChanges", true,
            "Says when your energy, insanity, tech points, money or happiness go up or down.");

        Resources.Add(new Watched("energy",
            n => Loc.Fmt("res.energy_gained", n),
            n => Loc.Fmt("res.energy_lost", n)));
        Resources.Add(new Watched("insanity",
            n => Loc.Fmt("res.insanity_gained", n),
            n => Loc.Fmt("res.insanity_lost", n)));
        Resources.Add(new Watched("tech_red",
            n => Loc.Fmt("res.got", Loc.Plural("points.red", n, n)),
            n => Loc.Fmt("res.spent", Loc.Plural("points.red", n, n))));
        Resources.Add(new Watched("tech_green",
            n => Loc.Fmt("res.got", Loc.Plural("points.green", n, n)),
            n => Loc.Fmt("res.spent", Loc.Plural("points.green", n, n))));
        Resources.Add(new Watched("tech_blue",
            n => Loc.Fmt("res.got", Loc.Plural("points.blue", n, n)),
            n => Loc.Fmt("res.spent", Loc.Plural("points.blue", n, n))));
        Resources.Add(new Watched("money",
            n => Loc.Fmt("res.got", Money.ToSpeech(n)),
            n => Loc.Fmt("res.spent", Money.ToSpeech(n))));
        Resources.Add(new Watched("happiness",
            n => Loc.Fmt("res.happiness_gained", n),
            n => Loc.Fmt("res.happiness_lost", n)));

        // The game's "not enough energy" popup. Without it, a tool swing that does nothing sounds
        // exactly like a swing that worked. These are static events, so one subscription lasts the
        // whole session.
        PlayerHPActivity.OnNotEnoughResOccurred += OnNotEnoughRes;
        PlayerCraftActivity.OnNotEnoughResOccurred += OnNotEnoughRes;
    }

    internal static void Update()
    {
        try
        {
            var player = CurrentPlayer();
            if (player == null)
            {
                _baselineFor = null;
                return;
            }

            if (!ReferenceEquals(player, _baselineFor))
            {
                Rebaseline(player);
                return;
            }

            var now = Time.unscaledTime;
            var moved = false;
            foreach (var r in Resources)
            {
                var value = player.GetRes(r.Res);
                if (value == r.Last) continue;

                r.Last = value;
                moved = true;
            }

            if (moved)
            {
                _lastChangeAt = now;
                if (_pendingSince < 0f) _pendingSince = now;
                if (now - _pendingSince < MaxHold) return;
            }
            else if (_pendingSince < 0f || now - _lastChangeAt < SettleDelay)
            {
                return;
            }

            _pendingSince = -1f;
            Flush();
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Res] Update failed: {ex.GetType().Name}: {ex.Message}");
            _baselineFor = null;
        }
    }

    private static void Flush()
    {
        var parts = new List<string>();

        foreach (var r in Resources)
        {
            var delta = (int)(r.Last - r.Baseline);
            if (delta == 0) continue;

            // Advance by what is spoken, not to the current value, so the fraction carries over.
            r.Baseline += delta;
            parts.Add(delta > 0 ? r.Gained(delta) : r.Lost(-delta));
        }

        if (parts.Count == 0) return;

        var line = string.Join(", ", parts);
        _log?.LogInfo($"[Res] {line}");

        if (_enabled == null || _enabled.Value)
            ScreenReader.Say(line, interrupt: false);
    }

    private static void Rebaseline(PlayerData player)
    {
        _baselineFor = player;
        _pendingSince = -1f;
        foreach (var r in Resources)
            r.Baseline = r.Last = player.GetRes(r.Res);
    }

    private static string _lastNotEnoughKey;
    private static float _lastNotEnoughAt = -100f;
    private const float NotEnoughRepeat = 4f;

    private static void OnNotEnoughRes(string resId)
    {
        try
        {
            var key = resId switch
            {
                "energy" => "res.no_energy",
                "insanity" => "res.no_insanity",
                _ => null,
            };
            if (key == null) return;

            // Holding the work key with no energy raises this every swing - sixty in a row in the
            // 2026-09-26 log, each cutting off the last. Once per few seconds says the same thing.
            var now = Time.unscaledTime;
            if (key == _lastNotEnoughKey && now - _lastNotEnoughAt < NotEnoughRepeat) return;
            _lastNotEnoughKey = key;
            _lastNotEnoughAt = now;

            _log?.LogInfo($"[Res] Not enough {resId}.");
            ScreenReader.Say(Loc.Get(key));
        }
        catch (Exception ex)
        {
            _log?.LogError($"[Res] Not-enough announce failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The live player, or null outside a running game.</summary>
    internal static PlayerData CurrentPlayer()
    {
        var game = MainGame.Instance;
        if (game == null || game.gameState != MainGame.GameState.InGame) return null;
        return MainGame.PlayerData;
    }
}

/// <summary>
/// Money is kept in bronze: 100 bronze to a silver, 100 silver to a gold, exactly as
/// <c>Trading.FormatMoney</c> splits it. On screen that is three coin icons with numbers after them.
/// </summary>
internal static class Money
{
    internal static string ToSpeech(float bronzeTotal)
    {
        var value = Mathf.Abs(Mathf.RoundToInt(bronzeTotal));
        var gold = value / 10000;
        var silver = value % 10000 / 100;
        var bronze = value % 100;

        var parts = new List<string>(3);
        if (gold > 0) parts.Add(Loc.Fmt("money.gold", gold));
        if (silver > 0) parts.Add(Loc.Fmt("money.silver", silver));
        if (bronze > 0) parts.Add(Loc.Fmt("money.bronze", bronze));

        return parts.Count == 0 ? Loc.Get("money.none") : string.Join(" ", parts);
    }
}
