using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace GameflowControl;

public partial class GameflowControl
{

    private void OnMapStart(string mapName)
    {
        Server.NextFrame(() =>
        {
            Server.ExecuteCommand("bot_quota 10");
            Server.ExecuteCommand("bot_quota_mode fill");
            Server.ExecuteCommand("mp_warmup_pausetimer 1");

            Console.WriteLine("[GameflowControl] Map started, commands executed in next frame.");
        });

        // Reset ready system on map change
        ResetReadySystem();
    }


    private void RegisterListeners()
    {
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerChat>(OnPlayerChat);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
    }

    private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            if (!player.IsBot)
            {
                PlayerReady[player.Slot] = false;
                Console.WriteLine($"[GameflowControl] Player {player.PlayerName} connected and marked as not ready.");
            }
            else
            {
                Console.WriteLine($"[GameflowControl] Bot {player.PlayerName} connected (excluded from ready system).");
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            PlayerReady.Remove(player.Slot);
            Console.WriteLine($"[GameflowControl] Player {player.PlayerName} disconnected and removed from ready list.");
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsValidPlayer(player)) return HookResult.Continue;

        Server.NextFrame(() =>
        {
            // Only handle real players, not bots
            if (player.IsBot) return;

            if (!PlayerReady.ContainsKey(player.Slot) || PlayerReady[player.Slot])
                return;

            // Mark player as ready when they spawn
            PlayerReady[player.Slot] = true;
            player.PrintToChat(" \x04[Gameflow]\x01 You have been automatically marked as READY.");
            Console.WriteLine($"[GameflowControl] Player {player.PlayerName} auto-marked as ready on spawn.");
        });

        return HookResult.Continue;
    }

    private HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        var player = Utilities.GetPlayerFromUserid(@event.Userid);
        var msg = @event.Text?.Trim().ToLower();

        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        // debug start knife round
        if (msg == "!qw")
        {
            Console.WriteLine("[GameflowControl] Manual knife round start requested!");
            StartKnifeRound();
            return HookResult.Continue;
        }

        if (msg == "!pause")
        {
            Server.ExecuteCommand("mp_pause_match");
            player.PrintToChat("[Gameflow] Match paused.");
            Console.WriteLine($"[GameflowControl] Player {player.PlayerName} paused the match.");
        }
        else if (msg == "!unpause")
        {
            Server.ExecuteCommand("mp_unpause_match");
            player.PrintToChat("[Gameflow] Match unpaused.");
            Console.WriteLine($"[GameflowControl] Player {player.PlayerName} unpaused the match.");
        }
        else if (msg == "!ready" || msg == ".r" || msg == ".ready" && _readySystemEnabled)
        {
            PlayerReady[player.Slot] = true;
            player.PrintToChat("[Gameflow] You are now marked as READY.");
            Console.WriteLine($"[GameflowControl] Player {player.PlayerName} marked themselves as ready.");
            CheckReadyStatus();
        }
        else if (msg == "!notready" || msg == ".nr" || msg == ".notready" && _readySystemEnabled)
        {
            PlayerReady[player.Slot] = false;
            player.PrintToChat("[Gameflow] You are now marked as NOT READY.");
            Console.WriteLine($"[GameflowControl] Player {player.PlayerName} marked themselves as not ready.");
            CheckReadyStatus();
        }
        else if (msg == "!status")
        {
            // Count only real players, exclude bots
            var realPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot).ToList();
            int total = realPlayers.Count;
            int ready = PlayerReady.Values.Count(v => v);
            player.PrintToChat($"[Gameflow] Ready Status: {ready}/{total} real players are ready (bots excluded).");
        }
        else if (msg == "!damage")
        {
            ShowDamageStats(player);
        }
        else if (msg == "!debug")
        {
            ShowDebugInfo(player);
        }
        else if (waitingForSideChoice && (msg == "!switch" || msg == ".switch" || msg == "!stay" || msg == ".stay"))
        {
            HandleSideChoice(msg, player);
            return HookResult.Continue;
        }


        return HookResult.Continue;
    }

    private string GetPlayerKey(CCSPlayerController player)
    {
        return player.IsBot ? $"BOT_{player.PlayerName}" : player.SteamID.ToString();
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        int rawDamage = @event.DmgHealth;

        // Defensive check
        if (rawDamage <= 0 || attacker == null || victim == null || !attacker.IsValid || !victim.IsValid || attacker == victim)
            return HookResult.Continue;

        // Don't track friendly fire
        if (attacker.Team == victim.Team)
            return HookResult.Continue;

        string attackerKey = GetPlayerKey(attacker);
        string victimKey = GetPlayerKey(victim);

        // Actual damage = min(raw, HP before), calculated from Pawn
        int actualDamage = rawDamage;

        if (victim.Pawn?.Value != null)
        {
            int victimHpBefore = (int)victim.Pawn.Value.Health + rawDamage;
            actualDamage = Math.Min(rawDamage, victimHpBefore);
        }

        // Init dictionaries
        if (!DamageMatrix.ContainsKey(attackerKey))
            DamageMatrix[attackerKey] = new Dictionary<string, int>();
        if (!HitMatrix.ContainsKey(attackerKey))
            HitMatrix[attackerKey] = new Dictionary<string, int>();

        // Cap to 100 per target
        int existingDamage = DamageMatrix[attackerKey].GetValueOrDefault(victimKey);
        int cappedDamage = Math.Max(0, Math.Min(actualDamage, 100 - existingDamage));

        // Store
        DamageMatrix[attackerKey][victimKey] = existingDamage + cappedDamage;
        HitMatrix[attackerKey][victimKey] = HitMatrix[attackerKey].GetValueOrDefault(victimKey) + 1;

        // Debug log
        Console.WriteLine($"[Gameflow] {attacker.PlayerName} → {victim.PlayerName}: +{cappedDamage} dmg (capped)");
        Console.WriteLine($"[DEBUG] {attacker.PlayerName} → {victim.PlayerName}: raw={rawDamage}, actual={actualDamage}, existing={existingDamage}, capped={cappedDamage}");

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {

        foreach (var viewer in Utilities.GetPlayers())
        {
            if (viewer == null || !viewer.IsValid) continue;

            string viewerKey = GetPlayerKey(viewer);

            viewer.PrintToChat(" \x04[FACEIT^]\x01 Round Summary");
            viewer.PrintToChat(" \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");

            foreach (var other in Utilities.GetPlayers())
            {
                if (other == null || !other.IsValid || other == viewer) continue;
                if (other.Team == viewer.Team) continue;

                string otherKey = GetPlayerKey(other);

                int toDamage = DamageMatrix.GetValueOrDefault(viewerKey)?.GetValueOrDefault(otherKey) ?? 0;
                int fromDamage = DamageMatrix.GetValueOrDefault(otherKey)?.GetValueOrDefault(viewerKey) ?? 0;

                int toHits = HitMatrix.GetValueOrDefault(viewerKey)?.GetValueOrDefault(otherKey) ?? 0;
                int fromHits = HitMatrix.GetValueOrDefault(otherKey)?.GetValueOrDefault(viewerKey) ?? 0;

                int currentHealth = 0;
                if (other.PawnIsAlive && other.Pawn?.Value != null)
                    currentHealth = Math.Max(0, (int)other.Pawn.Value.Health);

                string botTag = other.IsBot ? " [BOT]" : "";
                viewer.PrintToChat($" \x04[FACEIT^]\x01 To: [{toDamage} / {toHits} hits] From: [{fromDamage} / {fromHits} hits] - {other.PlayerName}{botTag} ({currentHealth} hp)");
            }

            viewer.PrintToChat(" \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");
        }

        // Reset for next round
        DamageMatrix.Clear();
        HitMatrix.Clear();

        // Check if this is a knife round
        if (knifeRoundInProgress)
        {
            Server.ExecuteCommand("mp_ct_default_secondary weapon_hkp2000");
            Server.ExecuteCommand("mp_t_default_secondary weapon_glock");
            OnKnifeRoundEnd();
            return HookResult.Continue;
        }

        return HookResult.Continue;
    }

    private static bool IsValidPlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid && !player.IsBot && player.PawnIsAlive;
    }

    private void ShowDamageStats(CCSPlayerController requestingPlayer)
    {
        string viewerKey = GetPlayerKey(requestingPlayer);
        bool hasDamage = false;

        requestingPlayer.PrintToChat(" \x04[FACEIT^]\x01 Current Round Damage");
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
                requestingPlayer.PrintToChat($" \x04[FACEIT^]\x01 To: [{toDamage} / {toHits} hits] From: [{fromDamage} / {fromHits} hits] - {other.PlayerName}{botTag} ({currentHealth} hp)");
            }
        }

        if (!hasDamage)
        {
            requestingPlayer.PrintToChat(" \x04[FACEIT^]\x01 No damage dealt this round yet.");
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

            Console.WriteLine($"[GameflowControl] Player {player.PlayerName} chose: {choice}");

            if (choice == "!stay" || choice == ".stay")
            {
                // Keep current sides
                Server.PrintToChatAll($" \x04[FACEIT]\x01 {player.PlayerName} chose to STAY. Starting match...");
                Console.WriteLine("[GameflowControl] Side choice: STAY");
            }
            else if (choice == "!switch" || choice == ".switch")
            {
                // Switch sides
                Server.PrintToChatAll($" \x04[FACEIT]\x01 {player.PlayerName} chose to SWITCH sides. Starting match...");
                Console.WriteLine("[GameflowControl] Side choice: SWITCH");

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

            Console.WriteLine("[GameflowControl] Match started after side choice");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameflowControl] ERROR in HandleSideChoice: {ex.Message}");
            Console.WriteLine($"[GameflowControl] Stack trace: {ex.StackTrace}");
        }
    }
}