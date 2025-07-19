using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using System.Linq;
using System.Collections.Generic;


namespace GameflowControl;

[MinimumApiVersion(320)]
public partial class GameflowControl : BasePlugin
{
    public override string ModuleName => "CS2 Gameflow Control";
    public override string ModuleAuthor => "Naranbat";
    public override string ModuleVersion => "1.0";
    private readonly Dictionary<string, Dictionary<string, int>> DamageMatrix = new();
    private readonly Dictionary<string, Dictionary<string, int>> HitMatrix = new();

    private readonly Dictionary<int, bool> PlayerReady = new();
    private bool _readySystemEnabled = true;

    private bool knifeRoundInProgress = false;
    private bool waitingForSideChoice = false;
    private int knifeRoundWinner = 0; // 2 = T, 3 = CT
    public override void Load(bool hotReload)
    {
        try
        {
            RegisterListeners();
            // Start ready status timer
            AddTimer(10.0f, () => CheckReadyStatus(), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            Console.WriteLine("[GameflowControl] Plugin loaded successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameflowControl] ERROR during Load: {ex.Message}");
            Console.WriteLine($"[GameflowControl] Stack trace: {ex.StackTrace}");
        }
    }

    private void ResetReadySystem()
    {
        _readySystemEnabled = true;
        PlayerReady.Clear();
    }

    private void CheckReadyStatus()
    {
        try
        {
            if (!_readySystemEnabled) return;

            var realPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot).ToList();
            int total = realPlayers.Count;
            int ready = PlayerReady.Values.Count(v => v);

            Console.WriteLine($"[GameflowControl] {ready}/{total} real players are ready (bots excluded).");

            if (total > 0 && ready == total)
            {
                Console.WriteLine("[GameflowControl] All players ready! Starting match in 10 seconds...");
                _readySystemEnabled = false;
                StartKnifeRound();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameflowControl] ERROR in CheckReadyStatus: {ex.Message}");
            Console.WriteLine($"[GameflowControl] Stack trace: {ex.StackTrace}");
        }
    }

    private void StartKnifeRound()
    {
        try
        {
            Console.WriteLine("[GameflowControl] Starting FACEIT-style knife round...");
            knifeRoundInProgress = true;

            // 🧠 Prevent pistol on spawn
            Server.ExecuteCommand("mp_ct_default_secondary none");
            Server.ExecuteCommand("mp_t_default_secondary none");

            Server.PrintToChatAll(" \x04[Gameflow]\x01 All players ready! Match starting in 10 seconds...");
            Server.ExecuteCommand("mp_warmuptime 12");
            Server.ExecuteCommand("mp_warmup_pausetimer 0");

            // Delay 2s to ensure round starts cleanly
            AddTimer(10.0f, () =>
            {
                try
                {
                    Server.ExecuteCommand("mp_roundtime 2");
                    // Server.ExecuteCommand("mp_freezetime 2");
                    Server.ExecuteCommand("mp_halftime 0");
                    Server.ExecuteCommand("mp_overtime_enable 0");

                    // Money and economy settings
                    Server.ExecuteCommand("mp_startmoney 0");

                    // Disable buy system completely
                    Server.ExecuteCommand("mp_buy_anywhere 0");
                    Server.ExecuteCommand("mp_buytime 0");

                    // Disable equipment
                    Server.ExecuteCommand("mp_give_player_c4 0");
                    Server.ExecuteCommand("mp_weapons_allow_map_placed 0");
                    Server.ExecuteCommand("mp_weapons_allow_zeus 0");

                    // Disable defuse kit
                    Server.ExecuteCommand("mp_defuser_allocation 0");

                    Server.PrintToChatAll(" \x04[FACEIT Knife Round]\x01 Knife-only round started! Winning team will pick side.");
                    Console.WriteLine("[GameflowControl] FACEIT-style knife round started successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GameflowControl] ERROR in knife round timer: {ex.Message}");
                    Console.WriteLine($"[GameflowControl] Stack trace: {ex.StackTrace}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameflowControl] ERROR in StartKnifeRound: {ex.Message}");
            Console.WriteLine($"[GameflowControl] Stack trace: {ex.StackTrace}");
        }
    }

    private void OnKnifeRoundEnd()
    {
        try
        {
            Console.WriteLine("[GameflowControl] FACEIT-style knife round ended, determining winner...");

            if (!knifeRoundInProgress) return;

            // Determine winner based on team scores
            var tScore = Utilities.GetPlayers().Count(p => p.Team == CsTeam.Terrorist && p.PawnIsAlive);
            var ctScore = Utilities.GetPlayers().Count(p => p.Team == CsTeam.CounterTerrorist && p.PawnIsAlive);

            Console.WriteLine($"[GameflowControl] T score: {tScore}, CT score: {ctScore}");

            if (tScore > ctScore)
            {
                knifeRoundWinner = 2; // T team won
                Server.PrintToChatAll(" \x04[FACEIT Knife Round]\x01 Terrorists won! Use !switch or !stay to choose side.");
                Console.WriteLine("[GameflowControl] Terrorists won knife round");
            }
            else if (ctScore > tScore)
            {
                knifeRoundWinner = 3; // CT team won
                Server.PrintToChatAll(" \x04[FACEIT Knife Round]\x01 Counter-Terrorists won! Use !switch or !stay to choose side.");
                Console.WriteLine("[GameflowControl] Counter-Terrorists won knife round");
            }
            else
            {
                // Tie - random winner
                var random = new Random();
                knifeRoundWinner = random.Next(2, 4); // 2 or 3
                string winnerTeam = knifeRoundWinner == 2 ? "Terrorists" : "Counter-Terrorists";
                Server.PrintToChatAll($" \x04[FACEIT Knife Round]\x01 Tie! {winnerTeam} randomly selected. Use !switch or !stay to choose side.");
                Console.WriteLine($"[GameflowControl] Tie in knife round, {winnerTeam} randomly selected");
            }

            waitingForSideChoice = true;
            knifeRoundInProgress = false;

            Console.WriteLine($"[GameflowControl] DEBUG: Set waitingForSideChoice = {waitingForSideChoice}");

            // Start 60-second warmup for side choice (FACEIT style)
            Console.WriteLine("[GameflowControl] Starting 60-second warmup for side choice...");
            Server.ExecuteCommand("mp_warmuptime 60");
            Server.ExecuteCommand("mp_warmup_pausetimer 0");
            Server.ExecuteCommand("mp_warmup_start");

            // Reset match settings for actual match
            Server.ExecuteCommand("mp_freezetime 15");
            Console.WriteLine("[GameflowControl] 60-second warmup started, waiting for side choice...");

            // Auto-start match after 60 seconds if no choice made
            AddTimer(60.0f, () =>
            {
                if (waitingForSideChoice)
                {
                    Console.WriteLine("[GameflowControl] 60 seconds passed, no side choice made. Auto-starting match...");
                    Server.PrintToChatAll(" \x04[FACEIT]\x01 No side choice made in 60 seconds. Auto-starting match...");
                   
                    Server.ExecuteCommand("mp_ct_default_secondary weapon_hkp2000");
                    Server.ExecuteCommand("mp_t_default_secondary weapon_glock");
                    Server.ExecuteCommand("mp_startmoney 800");
                    Server.ExecuteCommand("mp_buytime 20");
                    Server.ExecuteCommand("mp_give_player_c4 1");
                    Server.ExecuteCommand("mp_weapons_allow_map_placed 1");
                    Server.ExecuteCommand("mp_weapons_allow_zeus 1");
                    Server.ExecuteCommand("mp_defuser_allocation 2");

                    // Auto-choose stay (keep current sides)
                    waitingForSideChoice = false;
                    Server.ExecuteCommand("mp_halftime 1");
                    Server.ExecuteCommand("mp_warmup_end");

                    Console.WriteLine("[GameflowControl] Match auto-started after 60 seconds");
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameflowControl] ERROR in OnKnifeRoundEnd: {ex.Message}");
            Console.WriteLine($"[GameflowControl] Stack trace: {ex.StackTrace}");
        }
    }

}
