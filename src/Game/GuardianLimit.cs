using System;
using System.Collections.Generic;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// [Vanilla] GuardianAngelUses (v0.5.2, 2026-09-14 request "守護天使が無限につけれる"): how many times each Guardian Angel
    /// may protect per game. In a registered lobby every protect goes through the host (the client sends CheckProtect, the
    /// host answers with ProtectPlayer); past the limit the host simply does not answer, so the protection never happens
    /// and nothing out of the ordinary reaches any client. An unregistered lobby has no host authority (the Guardian Angel
    /// broadcasts ProtectPlayer itself), so there the option does nothing — raise the cooldown instead (/vset gacd 600).
    /// </summary>
    public static class GuardianLimit
    {
        private static readonly Dictionary<byte, int> _uses = new Dictionary<byte, int>();

        /// <summary>New game (RoleAssignment.BeginVanillaSelection).</summary>
        internal static void Reset() => _uses.Clear();

        /// <summary>Host-side CheckProtect: true = let vanilla answer (protect), false = swallow the request.</summary>
        internal static bool Allow(PlayerControl ga)
        {
            try
            {
                int limit = Options.GuardianAngelUses;
                if (limit <= 0 || ga == null || ga.Data == null) return true;
                if (Registration.CompatMode) return true;
                byte id = ga.PlayerId;
                _uses.TryGetValue(id, out int n);
                n++;
                _uses[id] = n;
                if (n <= limit)
                {
                    PocketRolesPlugin.Logger.LogInfo($"GuardianLimit: #{id} {Core.Game.NameOf(id)} protect {n}/{limit}");
                    return true;
                }
                PocketRolesPlugin.Logger.LogInfo($"GuardianLimit: #{id} {Core.Game.NameOf(id)} protect refused ({n} > {limit})");
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GuardianLimit.Allow: {e}");
                return true;
            }
        }
    }

    /// <summary>The host's CheckProtect handler (registered lobby): the Guardian Angel's request is answered only within [Vanilla] GuardianAngelUses.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
    internal static class GuardianLimit_CheckProtectPatch
    {
        private static bool Prefix(PlayerControl __instance, PlayerControl target)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Core.Game.IsHostActive) return true;
                return GuardianLimit.Allow(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GuardianLimit_CheckProtectPatch: {e}");
                return true;
            }
        }
    }
}
