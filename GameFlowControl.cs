using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using System.Linq;

namespace GameflowControl;

[MinimumApiVersion(320)]
public partial class GameflowControl : BasePlugin
{
    public override string ModuleName => "CS2 Gameflow Control";
    public override string ModuleAuthor => "Naranbat";
    public override string ModuleVersion => "1.0";

    // private readonly Dictionary<string, int> DamageDealt = new();
    // private readonly Dictionary<string, int> DamageTaken = new();
    // private readonly Dictionary<string, int> HitsDealt = new();
    // private readonly Dictionary<string, int> HitsTaken = new();
    private readonly Dictionary<int, bool> PlayerReady = new();
    private readonly Dictionary<string, Dictionary<string, int>> DamageMatrix = new();
    private readonly Dictionary<string, Dictionary<string, int>> HitMatrix = new();

    private bool knifeRoundInProgress = false;
    private bool waitingForSideChoice = false;
    private int knifeRoundWinner = 0; // 2 = T, 3 = C

    private bool _readySystemEnabled = true;
    public override void Load(bool hotReload)
    {
        RegisterListeners();
        // Start ready status timer
        AddTimer(10.0f, () => CheckReadyStatus(), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        Console.WriteLine("[GameflowControl] Plugin loaded successfully!");
    }

    private void ResetReadySystem()
    {
        _readySystemEnabled = true;
        PlayerReady.Clear();
    }

    private void CheckReadyStatus()
    {
        // Only check ready status during warmup
        if (!_readySystemEnabled) return;

        // Count only real players, exclude bots
        var realPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot).ToList();
        int total = realPlayers.Count;
        int ready = PlayerReady.Values.Count(v => v);

        Server.PrintToConsole($"[Gameflow] {ready}/{total} real players are ready (bots excluded).");

        // Broadcast to all players
        foreach (var player in Utilities.GetPlayers())
        {
            if (IsValidPlayer(player))
            {
                player.PrintToChat($" \x04[Gameflow]\x01 Ready Status: {ready}/{total} real players are ready (bots excluded).");
            }
        }

        if (total >= 9 && ready == total)
        {
            Server.PrintToChatAll(" \x04[Gameflow]\x01 All players are ready! Starting match...");
            // set warmup to 3 seconds
            Server.ExecuteCommand("mp_warmuptime 3");
            _readySystemEnabled = false;
            Console.WriteLine("[GameflowControl] All players ready - warmup ended");
        }
    }
}
