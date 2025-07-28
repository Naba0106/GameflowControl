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

public partial class GameflowControl
{

    private string GetPlayerKey(CCSPlayerController player)
    {
        return player.IsBot ? $"BOT_{player.PlayerName}" : player.SteamID.ToString();
    }

    private static bool IsValidPlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid && !player.IsBot;// && player.PawnIsAlive;
    }

    private void ShowDebugInfo(CCSPlayerController requestingPlayer)
    {
        PlayerToChat(requestingPlayer, " \x04[DEBUG] Damage Matrix Info\x01");
        PlayerToChat(requestingPlayer, " \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");

        PlayerToChat(requestingPlayer, $" \x01 DamageMatrix entries: {Config.Instance.damageMatrix.Count}");
        PlayerToChat(requestingPlayer, $" \x01 HitMatrix entries: {Config.Instance.hitMatrix.Count}");

        PlayerToChat(requestingPlayer, " \x01--- Damage Matrix ---");
        foreach (var attackerEntry in Config.Instance.damageMatrix)
        {
            string attackerName = "Unknown";
            var attacker = Utilities.GetPlayers().FirstOrDefault(p => GetPlayerKey(p) == attackerEntry.Key);
            if (attacker != null)
                attackerName = attacker.PlayerName + (attacker.IsBot ? " [BOT]" : "");

            PlayerToChat(requestingPlayer, $" \x01{attackerName} ({attackerEntry.Key}):");

            foreach (var victimEntry in attackerEntry.Value)
            {
                string victimName = "Unknown";
                var victim = Utilities.GetPlayers().FirstOrDefault(p => GetPlayerKey(p) == victimEntry.Key);
                if (victim != null)
                    victimName = victim.PlayerName + (victim.IsBot ? " [BOT]" : "");

                PlayerToChat(requestingPlayer, $" \x01  → {victimName}: {victimEntry.Value} damage");
            }
        }

        PlayerToChat(requestingPlayer, " \x02━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\x01");
    }

    private static void PlayerToChat(CCSPlayerController player, string message)
    {
        player.PrintToChat(message);
    }
}