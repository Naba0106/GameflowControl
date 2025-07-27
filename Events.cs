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
            mapStartServerCommands();
            Console.WriteLine("{moduleChaPrefix} Map started, commands executed in next frame.");
        });

        // Reset ready system on map change
        ResetReadySystem();
        ResetGame();
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
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
    }

    private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            if (!player.IsBot)
            {
                PlayerReady[player.Slot] = false;
                Console.WriteLine($"{ModuleName} Player {player.PlayerName} connected and marked as not ready.");
            }
            else
            {
                Console.WriteLine($"{ModuleName} Bot {player.PlayerName} connected (excluded from ready system).");
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
            Console.WriteLine($"{ModuleName} Player {player.PlayerName} disconnected and removed from ready list.");
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsValidPlayer(player)) return HookResult.Continue;

        if (_readySystemEnabled)
        {
            Console.WriteLine($"{ModuleName} Player {player.PlayerName} spawned and money set to 16000.");
            // Set player money to 16000 when ready system is disabled
            if (player.InGameMoneyServices != null)
            {
                player.InGameMoneyServices.Account = 16000;
            }
        }

        // Server.NextFrame(() =>
        // {
        //     // Only handle real players, not bots
        //     if (player.IsBot) return;

        //     if (!PlayerReady.ContainsKey(player.Slot) || PlayerReady[player.Slot])
        //         return;

        //     // Mark player as ready when they spawn
        //     PlayerReady[player.Slot] = true;
        //     player.PrintToChat($"{moduleChaPrefix} You have been automatically marked as READY.");
        //     Console.WriteLine($"{ModuleName} Player {player.PlayerName} auto-marked as ready on spawn.");
        // });

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
            Console.WriteLine($"{ModuleName} Manual knife round start requested!");
            StartKnifeRound();
            return HookResult.Continue;
        }

        if (msg == "!pause")
        {
            Server.ExecuteCommand("mp_pause_match");
            player.PrintToChat($"{moduleChaPrefix} Match paused.");
            Console.WriteLine($"{ModuleName} Player {player.PlayerName} paused the match.");
        }
        else if (msg == "!unpause")
        {
            Server.ExecuteCommand("mp_unpause_match");
            player.PrintToChat($"{moduleChaPrefix} Match unpaused.");
            Console.WriteLine($"{ModuleName} Player {player.PlayerName} unpaused the match.");
        }
        else if (msg == "!ready" || msg == ".r" || msg == ".ready" && _readySystemEnabled)
        {
            PlayerReady[player.Slot] = true;
            player.PrintToChat($"{moduleChaPrefix} You are now marked as READY.");
            Console.WriteLine($"{ModuleName} Player {player.PlayerName} marked themselves as ready.");
            CheckReadyStatus();
        }
        else if (msg == "!notready" || msg == ".nr" || msg == ".notready" && _readySystemEnabled)
        {
            PlayerReady[player.Slot] = false;
            player.PrintToChat($"{moduleChaPrefix} You are now marked as NOT READY.");
            Console.WriteLine($"{ModuleName} Player {player.PlayerName} marked themselves as not ready.");
            CheckReadyStatus();
        }
        else if (msg == "!status")
        {
            // Count only real players, exclude bots
            var realPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot).ToList();
            int total = realPlayers.Count;
            int ready = PlayerReady.Values.Count(v => v);
            player.PrintToChat($"{moduleChaPrefix} Ready Status: {ready}/{total} real players are ready (bots excluded).");
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
        Console.WriteLine($"{ModuleName} {attacker.PlayerName} → {victim.PlayerName}: +{cappedDamage} dmg (capped)");
        Console.WriteLine($"{ModuleName} {attacker.PlayerName} → {victim.PlayerName}: raw={rawDamage}, actual={actualDamage}, existing={existingDamage}, capped={cappedDamage}");

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {

        foreach (var viewer in Utilities.GetPlayers())
        {
            if (viewer == null || !viewer.IsValid) continue;

            string viewerKey = GetPlayerKey(viewer);

            viewer.PrintToChat($"{moduleChaPrefix} Round Summary");
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
                viewer.PrintToChat($"{moduleChaPrefix} To: [{toDamage} / {toHits} hits] From: [{fromDamage} / {fromHits} hits] - {other.PlayerName}{botTag} ({currentHealth} hp)");
            }

            viewer.PrintToChat(" \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");
        }

        // Reset for next round
        DamageMatrix.Clear();
        HitMatrix.Clear();

        // Check if this is a knife round
        if (knifeRoundInProgress)
        {
            OnKnifeRoundEnd();
            return HookResult.Continue;
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (knifeRoundInProgress)
        {
            Server.PrintToChatAll($"{moduleChaPrefix} Knife-only round started! Winning team will pick side.");
        }
        return HookResult.Continue;
    }

}