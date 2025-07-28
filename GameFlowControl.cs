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
    public string moduleChaPrefix = " \x04[Gameflow Control]\x01";
    public override string ModuleName => "CS2 Gameflow Control";
    public override string ModuleAuthor => "Naranbat";
    public override string ModuleVersion => "1.0";


    public override void Load(bool hotReload)
    {
        try
        {
            RegisterListeners();
            // Start ready status timer
            AddTimer(10.0f, () => CheckReadyStatus(), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            Console.WriteLine($"{ModuleName} Plugin loaded successfully!");
            // Server.NextFrame(() =>
            // {
            //     try
            //     {
            //         SpawnBanner();
            //     }
            //     catch (Exception ex)
            //     {
            //         Console.WriteLine($"[MapBanner] Алдаа: {ex.Message}");
            //     }
            // });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ModuleName} ERROR during Load: {ex.Message}");
            Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
        }
    }

    private void ResetGame()
    {
        // Config.Instance.MaxPlayers = 15;
        Config.Instance.knifeRoundInProgress = false;
        Config.Instance.waitingForSideChoice = false;
        Config.Instance.knifeRoundWinner = CsTeam.None;
        Config.Instance.sideChoiceTimer?.Kill();
        Config.Instance.sideChoiceTimer = null;
    }

    private void ResetReadySystem()
    {
        Config.Instance.readySystemEnabled = true;
        Config.Instance.playerReady.Clear();
    }

    private void CheckReadyStatus()
    {
        try
        {
            if (!Config.Instance.readySystemEnabled) return;

            var realPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot).ToList();
            int total = realPlayers.Count;
            int ready = Config.Instance.playerReady.Values.Count(v => v);

            Console.WriteLine($"{ModuleName} {ready}/{total} real players are ready (bots excluded).");

            if (total > 0 && ready == total)
            {
                Console.WriteLine($"{ModuleName} All players ready! Starting match in 10 seconds...");
                Config.Instance.readySystemEnabled = false;
                StartKnifeRound();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ModuleName} ERROR in CheckReadyStatus: {ex.Message}");
            Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
        }
    }

    private void StartKnifeRound()
    {
        try
        {
            Console.WriteLine($"{ModuleName} Starting knife round...");
            Config.Instance.knifeRoundInProgress = true;

            // 🧠 Prevent pistol on spawn
            Server.ExecuteCommand("mp_ct_default_secondary none");
            Server.ExecuteCommand("mp_t_default_secondary none");
            Server.ExecuteCommand("mp_warmuptime 12");
            Server.ExecuteCommand("mp_warmup_pausetimer 0");

            Server.PrintToChatAll($"{moduleChaPrefix} All players ready! Match starting in 10 seconds...");

            AddTimer(10.0f, () =>
            {
                try
                {
                    Server.ExecuteCommand("mp_roundtime 2");
                    Server.ExecuteCommand("mp_halftime 0");
                    // Server.ExecuteCommand("mp_overtime_enable 0");

                    // Money and economy settings
                    Server.ExecuteCommand("mp_startmoney 0");

                    // Disable equipment
                    Server.ExecuteCommand("mp_give_player_c4 0");
                    Server.ExecuteCommand("mp_weapons_allow_map_placed 0");
                    Server.ExecuteCommand("mp_weapons_allow_zeus 0");

                    // Disable defuse kit
                    // Server.ExecuteCommand("mp_defuser_allocation 0");
                    Console.WriteLine($"{ModuleName} Knife round started successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ModuleName} ERROR in knife round timer: {ex.Message}");
                    Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ModuleName} ERROR in StartKnifeRound: {ex.Message}");
            Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
        }
    }

    private void OnKnifeRoundEnd()
    {
        try
        {
            Console.WriteLine($"{ModuleName} Knife round ended, determining winner...");

            if (!Config.Instance.knifeRoundInProgress) return;

            // Determine winner based on team scores
            var tScore = Utilities.GetPlayers().Count(p => p.Team == CsTeam.Terrorist && p.PawnIsAlive);
            var ctScore = Utilities.GetPlayers().Count(p => p.Team == CsTeam.CounterTerrorist && p.PawnIsAlive);

            Console.WriteLine($"{ModuleName} T score: {tScore}, CT score: {ctScore}");

            if (tScore > ctScore)
            {
                Config.Instance.knifeRoundWinner = CsTeam.Terrorist; // T team won
                Server.PrintToChatAll($"{moduleChaPrefix} Terrorists won! Use !switch or !stay to choose side.");
                Console.WriteLine($"{ModuleName} Terrorists won knife round");
            }
            else if (ctScore > tScore)
            {
                Config.Instance.knifeRoundWinner = CsTeam.CounterTerrorist; // CT team won
                Server.PrintToChatAll($"{moduleChaPrefix} Counter-Terrorists won! Use !switch or !stay to choose side.");
                Console.WriteLine($"{ModuleName} Counter-Terrorists won knife round");
            }
            else
            {
                // Tie - random winner
                var random = new Random();
                Config.Instance.knifeRoundWinner = random.Next(2, 4) == 2 ? CsTeam.Terrorist : CsTeam.CounterTerrorist; // 2 or 3
                string winnerTeam = Config.Instance.knifeRoundWinner == CsTeam.Terrorist ? "Terrorists" : "Counter-Terrorists";
                Server.PrintToChatAll($"{moduleChaPrefix} Tie! {winnerTeam} randomly selected. Use !switch or !stay to choose side.");
                Console.WriteLine($"{ModuleName} Tie in knife round, {winnerTeam} randomly selected");
            }

            Config.Instance.waitingForSideChoice = true;
            Config.Instance.knifeRoundInProgress = false;

            knifeRoundEndCommands();
            Console.WriteLine($"{ModuleName} 60-second warmup started, waiting for side choice...");
            StartSideChoiceTimer();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ModuleName} ERROR in OnKnifeRoundEnd: {ex.Message}");
            Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
        }
    }

    private void StartSideChoiceTimer()
    {
        Config.Instance.sideChoiceTimer = AddTimer(60.0f, () =>
        {
            if (Config.Instance.waitingForSideChoice)
            {
                Console.WriteLine($"{ModuleName} 60 seconds passed, no side choice made. Auto-starting match...");
                Server.PrintToChatAll($"{moduleChaPrefix} No side choice made in 60 seconds. Auto-starting match...");
                StartMatch();
                Console.WriteLine($"{ModuleName} Match auto-started after 60 seconds");
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void mapStartServerCommands()
    {
        Server.ExecuteCommand("bot_quota 10");
        Server.ExecuteCommand("bot_quota_mode fill");
        Server.ExecuteCommand("mp_warmup_pausetimer 1");

        Server.ExecuteCommand("mp_death_drop_gun 0");
        Server.ExecuteCommand("mp_buy_allow_grenades 0");
        // mp_death_drop_gun 0 baih uyd doorh 2 command hereggui
        // Server.ExecuteCommand("mp_death_drop_defuser 0"); 
        // Server.ExecuteCommand("mp_death_drop_taser 0");
    }

    private void knifeRoundEndCommands()
    {
        Console.WriteLine($"{ModuleName} DEBUG: Set waitingForSideChoice = {Config.Instance.waitingForSideChoice}");
        Console.WriteLine($"{ModuleName} Starting 60-second warmup for side choice...");

        Server.ExecuteCommand("mp_ct_default_secondary weapon_hkp2000");
        Server.ExecuteCommand("mp_t_default_secondary weapon_glock");

        // Reset match settings for actual match
        Server.ExecuteCommand("mp_freezetime 15");

        Server.ExecuteCommand("mp_death_drop_gun 1");
        Server.ExecuteCommand("mp_buy_allow_grenades 1");

        Server.ExecuteCommand("mp_warmuptime 60");
        Server.ExecuteCommand("mp_warmup_start");
    }

    private void StartMatch()
    {
        Config.Instance.waitingForSideChoice = false;

        Server.ExecuteCommand("mp_startmoney 800");
        Server.ExecuteCommand("mp_buytime 20");
        Server.ExecuteCommand("mp_give_player_c4 1");
        Server.ExecuteCommand("mp_weapons_allow_map_placed 1");
        Server.ExecuteCommand("mp_weapons_allow_zeus 1");
        // Server.ExecuteCommand("mp_defuser_allocation 1");

        Server.ExecuteCommand("mp_halftime 1");
        Server.ExecuteCommand("mp_warmup_end");
    }

    private void ShowDamageStats(CCSPlayerController requestingPlayer)
    {
        string viewerKey = GetPlayerKey(requestingPlayer);
        bool hasDamage = false;

        requestingPlayer.PrintToChat($"{moduleChaPrefix} Current Round Damage");
        requestingPlayer.PrintToChat(" \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");

        foreach (var other in Utilities.GetPlayers())
        {
            if (other == null || !other.IsValid || other == requestingPlayer) continue;
            if (other.Team == requestingPlayer.Team) continue; // Skip teammates

            string otherKey = GetPlayerKey(other);

            int toDamage = Config.Instance.damageMatrix.GetValueOrDefault(viewerKey)?.GetValueOrDefault(otherKey) ?? 0;
            int fromDamage = Config.Instance.damageMatrix.GetValueOrDefault(otherKey)?.GetValueOrDefault(viewerKey) ?? 0;

            int toHits = Config.Instance.hitMatrix.GetValueOrDefault(viewerKey)?.GetValueOrDefault(otherKey) ?? 0;
            int fromHits = Config.Instance.hitMatrix.GetValueOrDefault(otherKey)?.GetValueOrDefault(viewerKey) ?? 0;

            if (toDamage > 0 || fromDamage > 0)
            {
                hasDamage = true;
                int currentHealth = 0;
                if (other.PawnIsAlive && other.Pawn?.Value != null)
                    currentHealth = Math.Max(0, (int)other.Pawn.Value.Health);

                string botTag = other.IsBot ? " [BOT]" : "";
                requestingPlayer.PrintToChat($"{moduleChaPrefix} To: [{toDamage} / {toHits} hits] From: [{fromDamage} / {fromHits} hits] - {other.PlayerName}{botTag} ({currentHealth} hp)");
            }
        }

        if (!hasDamage)
        {
            requestingPlayer.PrintToChat($"{moduleChaPrefix} No damage dealt this round yet.");
        }

        requestingPlayer.PrintToChat(" \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");
    }

    private void HandleSideChoice(string choice, CCSPlayerController player)
    {
        try
        {
            if (!Config.Instance.waitingForSideChoice) return;
            if (player.Team == CsTeam.Spectator) return;
            if (player.Team == CsTeam.None) return;
            if (player.Team != Config.Instance.knifeRoundWinner) return;


            Console.WriteLine($"{ModuleName} Player {player.PlayerName} chose: {choice}");

            if (Config.Instance.StayCommands.Contains(choice))
            {
                // Keep current sides
                Server.PrintToChatAll($"{moduleChaPrefix} {player.PlayerName} chose to STAY. Starting match...");
                Console.WriteLine($"{ModuleName} Side choice: STAY");
            }
            else if (Config.Instance.SwitchCommands.Contains(choice))
            {
                // Switch sides
                Server.PrintToChatAll($"{moduleChaPrefix} {player.PlayerName} chose to SWITCH sides. Starting match...");
                Console.WriteLine($"{ModuleName} Side choice: SWITCH");

                // Switch teams
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid)
                    {
                        if (p.Team == CsTeam.Terrorist)
                            p.SwitchTeam(CsTeam.CounterTerrorist);
                        else if (p.Team == CsTeam.CounterTerrorist)
                            p.SwitchTeam(CsTeam.Terrorist);
                    }
                }
            }

            // End warmup and start match
            Config.Instance.waitingForSideChoice = false;
            StartMatch();

            Console.WriteLine($"{ModuleName} Match started after side choice");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ModuleName} ERROR in HandleSideChoice: {ex.Message}");
            Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
        }
    }

}
