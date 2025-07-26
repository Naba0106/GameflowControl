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
    private readonly Dictionary<string, Dictionary<string, int>> DamageMatrix = new();
    private readonly Dictionary<string, Dictionary<string, int>> HitMatrix = new();

    private readonly Dictionary<int, bool> PlayerReady = new();
    private bool _readySystemEnabled = true;

    private bool knifeRoundInProgress = false;
    private bool waitingForSideChoice = false;
    private CsTeam knifeRoundWinner = CsTeam.None; // 2 = T, 3 = CT
    private CounterStrikeSharp.API.Modules.Timers.Timer? sideChoiceTimer;

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
        knifeRoundInProgress = false;
        waitingForSideChoice = false;
        knifeRoundWinner = CsTeam.None;
        sideChoiceTimer?.Kill();
        sideChoiceTimer = null;
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

            Console.WriteLine($"{ModuleName} {ready}/{total} real players are ready (bots excluded).");

            if (total > 0 && ready == total)
            {
                Console.WriteLine($"{ModuleName} All players ready! Starting match in 10 seconds...");
                _readySystemEnabled = false;
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
            knifeRoundInProgress = true;

            // 🧠 Prevent pistol on spawn
            Server.ExecuteCommand("mp_ct_default_secondary none");
            Server.ExecuteCommand("mp_t_default_secondary none");

            Server.PrintToChatAll($"{moduleChaPrefix} All players ready! Match starting in 10 seconds...");
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

                    Server.PrintToChatAll($"{moduleChaPrefix} Knife-only round started! Winning team will pick side.");
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

            if (!knifeRoundInProgress) return;

            // Determine winner based on team scores
            var tScore = Utilities.GetPlayers().Count(p => p.Team == CsTeam.Terrorist && p.PawnIsAlive);
            var ctScore = Utilities.GetPlayers().Count(p => p.Team == CsTeam.CounterTerrorist && p.PawnIsAlive);

            Console.WriteLine($"{ModuleName} T score: {tScore}, CT score: {ctScore}");

            if (tScore > ctScore)
            {
                knifeRoundWinner = CsTeam.Terrorist; // T team won
                Server.PrintToChatAll($"{moduleChaPrefix} Terrorists won! Use !switch or !stay to choose side.");
                Console.WriteLine($"{ModuleName} Terrorists won knife round");
            }
            else if (ctScore > tScore)
            {
                knifeRoundWinner = CsTeam.CounterTerrorist; // CT team won
                Server.PrintToChatAll($"{moduleChaPrefix} Counter-Terrorists won! Use !switch or !stay to choose side.");
                Console.WriteLine($"{ModuleName} Counter-Terrorists won knife round");
            }
            else
            {
                // Tie - random winner
                var random = new Random();
                knifeRoundWinner = random.Next(2, 4) == 2 ? CsTeam.Terrorist : CsTeam.CounterTerrorist; // 2 or 3
                string winnerTeam = knifeRoundWinner == CsTeam.Terrorist ? "Terrorists" : "Counter-Terrorists";
                Server.PrintToChatAll($"{moduleChaPrefix} Tie! {winnerTeam} randomly selected. Use !switch or !stay to choose side.");
                Console.WriteLine($"{ModuleName} Tie in knife round, {winnerTeam} randomly selected");
            }

            waitingForSideChoice = true;
            knifeRoundInProgress = false;

            Console.WriteLine($"{ModuleName} DEBUG: Set waitingForSideChoice = {waitingForSideChoice}");

            // Start 60-second warmup for side choice (FACEIT style)
            Console.WriteLine($"{ModuleName} Starting 60-second warmup for side choice...");
            Server.ExecuteCommand("mp_warmuptime 60");
            Server.ExecuteCommand("mp_warmup_pausetimer 0");
            Server.ExecuteCommand("mp_warmup_start");

            // Reset match settings for actual match
            Server.ExecuteCommand("mp_freezetime 15");

            Server.ExecuteCommand("mp_death_drop_gun 1");
            Server.ExecuteCommand("mp_death_drop_defuser 1");
            Server.ExecuteCommand("mp_death_drop_taser 1");
            Server.ExecuteCommand("mp_buy_allow_grenades 1");
            Console.WriteLine($"{ModuleName} 60-second warmup started, waiting for side choice...");
            StartSideChoiceTimer();
            // Auto-start match after 60 seconds if no choice made
            // AddTimer(60.0f, () =>
            // {
            //     if (waitingForSideChoice)
            //     {
            //         Console.WriteLine($"{ModuleName} 60 seconds passed, no side choice made. Auto-starting match...");
            //         Server.PrintToChatAll($"{moduleChaPrefix} No side choice made in 60 seconds. Auto-starting match...");

            //         Server.ExecuteCommand("mp_ct_default_secondary weapon_hkp2000");
            //         Server.ExecuteCommand("mp_t_default_secondary weapon_glock");
            //         Server.ExecuteCommand("mp_startmoney 800");
            //         Server.ExecuteCommand("mp_buytime 20");
            //         Server.ExecuteCommand("mp_give_player_c4 1");
            //         Server.ExecuteCommand("mp_weapons_allow_map_placed 1");
            //         Server.ExecuteCommand("mp_weapons_allow_zeus 1");
            //         Server.ExecuteCommand("mp_defuser_allocation 2");

            //         // Auto-choose stay (keep current sides)
            //         waitingForSideChoice = false;
            //         Server.ExecuteCommand("mp_halftime 1");
            //         Server.ExecuteCommand("mp_warmup_end");

            //         Console.WriteLine($"{ModuleName} Match auto-started after 60 seconds");
            //     }
            // }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ModuleName} ERROR in OnKnifeRoundEnd: {ex.Message}");
            Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
        }
    }

    private void StartSideChoiceTimer()
    {
        sideChoiceTimer = AddTimer(60.0f, () =>
        {
            if (waitingForSideChoice)
            {
                AutoStartMatch();
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void AutoStartMatch()
    {
        Console.WriteLine($"{ModuleName} 60 seconds passed, no side choice made. Auto-starting match...");
        Server.PrintToChatAll($"{moduleChaPrefix} No side choice made in 60 seconds. Auto-starting match...");

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

        Console.WriteLine($"{ModuleName} Match auto-started after 60 seconds");
    }

    private string GetPlayerKey(CCSPlayerController player)
    {
        return player.IsBot ? $"BOT_{player.PlayerName}" : player.SteamID.ToString();
    }

    private static bool IsValidPlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid && !player.IsBot;// && player.PawnIsAlive;
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

            int toDamage = DamageMatrix.GetValueOrDefault(viewerKey)?.GetValueOrDefault(otherKey) ?? 0;
            int fromDamage = DamageMatrix.GetValueOrDefault(otherKey)?.GetValueOrDefault(viewerKey) ?? 0;

            int toHits = HitMatrix.GetValueOrDefault(viewerKey)?.GetValueOrDefault(otherKey) ?? 0;
            int fromHits = HitMatrix.GetValueOrDefault(otherKey)?.GetValueOrDefault(viewerKey) ?? 0;

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

    private void ShowDebugInfo(CCSPlayerController requestingPlayer)
    {
        requestingPlayer.PrintToChat(" \x04[DEBUG] Damage Matrix Info\x01");
        requestingPlayer.PrintToChat(" \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");

        requestingPlayer.PrintToChat($" \x01 DamageMatrix entries: {DamageMatrix.Count}");
        requestingPlayer.PrintToChat($" \x01 HitMatrix entries: {HitMatrix.Count}");

        requestingPlayer.PrintToChat(" \x01--- Damage Matrix ---");
        foreach (var attackerEntry in DamageMatrix)
        {
            string attackerName = "Unknown";
            var attacker = Utilities.GetPlayers().FirstOrDefault(p => GetPlayerKey(p) == attackerEntry.Key);
            if (attacker != null)
                attackerName = attacker.PlayerName + (attacker.IsBot ? " [BOT]" : "");

            requestingPlayer.PrintToChat($" \x01{attackerName} ({attackerEntry.Key}):");

            foreach (var victimEntry in attackerEntry.Value)
            {
                string victimName = "Unknown";
                var victim = Utilities.GetPlayers().FirstOrDefault(p => GetPlayerKey(p) == victimEntry.Key);
                if (victim != null)
                    victimName = victim.PlayerName + (victim.IsBot ? " [BOT]" : "");

                requestingPlayer.PrintToChat($" \x01  → {victimName}: {victimEntry.Value} damage");
            }
        }

        requestingPlayer.PrintToChat(" \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");
    }

    private void HandleSideChoice(string choice, CCSPlayerController player)
    {
        try
        {
            if (!waitingForSideChoice) return;
            if (player.Team == CsTeam.Spectator) return;
            if (player.Team == CsTeam.None) return;
            if (player.Team != knifeRoundWinner) return;


            Console.WriteLine($"{ModuleName} Player {player.PlayerName} chose: {choice}");

            if (choice == "!stay" || choice == ".stay")
            {
                // Keep current sides
                Server.PrintToChatAll($"{moduleChaPrefix} {player.PlayerName} chose to STAY. Starting match...");
                Console.WriteLine($"{ModuleName} Side choice: STAY");
            }
            else if (choice == "!switch" || choice == ".switch")
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
            waitingForSideChoice = false;
            Server.ExecuteCommand("mp_halftime 1");
            Server.ExecuteCommand("mp_warmup_end");

            Console.WriteLine($"{ModuleName} Match started after side choice");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ModuleName} ERROR in HandleSideChoice: {ex.Message}");
            Console.WriteLine($"{ModuleName} Stack trace: {ex.StackTrace}");
        }
    }

}
