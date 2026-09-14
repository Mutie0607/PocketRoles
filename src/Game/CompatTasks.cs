using System;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// Task counts beyond the vanilla range in an unregistered lobby (v0.5.1, 2026-09-14 request). The official server
    /// validates the synced game options of an unregistered lobby (3 common tasks = "Hacking" disconnect, 2026-09-08/09),
    /// but how many tasks each player actually receives is decided by the host alone in ShipStatus.Begin. So the synced
    /// counts stay inside the vanilla range and [Compat] CommonTasks / ShortTasks / LongTasks (0 = the lobby setting; set
    /// by /vset common|short|long in an unregistered lobby, or the settings tab) are applied to the host's local option
    /// object only for the duration of Begin and restored right after — nothing out of range is ever synced.
    /// </summary>
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Begin))]
    internal static class CompatTasks_BeginPatch
    {
        private static bool _applied;
        private static int _common, _short, _long;

        public static bool Active => Registration.CompatMode && (Options.CompatCommonTasks > 0 || Options.CompatShortTasks > 0 || Options.CompatLongTasks > 0);

        private static void Prefix()
        {
            _applied = false;
            try
            {
                if (!Core.Game.IsHostActive || !Active) return;
                var o = GameOptionsManager.Instance?.CurrentGameOptions;
                if (o == null) return;
                _common = o.GetInt(Int32OptionNames.NumCommonTasks);
                _short = o.GetInt(Int32OptionNames.NumShortTasks);
                _long = o.GetInt(Int32OptionNames.NumLongTasks);
                int c = Options.CompatCommonTasks > 0 ? Options.CompatCommonTasks : _common;
                int s = Options.CompatShortTasks > 0 ? Options.CompatShortTasks : _short;
                int l = Options.CompatLongTasks > 0 ? Options.CompatLongTasks : _long;
                o.SetInt(Int32OptionNames.NumCommonTasks, c);
                o.SetInt(Int32OptionNames.NumShortTasks, s);
                o.SetInt(Int32OptionNames.NumLongTasks, l);
                _applied = true;
                PocketRolesPlugin.Logger.LogInfo($"CompatTasks: handing out {c}/{s}/{l} tasks (common/short/long; lobby setting {_common}/{_short}/{_long}, unregistered lobby)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CompatTasks prefix: {e}");
            }
        }

        private static void Postfix()
        {
            if (!_applied) return;
            _applied = false;
            try
            {
                var o = GameOptionsManager.Instance?.CurrentGameOptions;
                if (o == null) return;
                o.SetInt(Int32OptionNames.NumCommonTasks, _common);
                o.SetInt(Int32OptionNames.NumShortTasks, _short);
                o.SetInt(Int32OptionNames.NumLongTasks, _long);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CompatTasks postfix: {e}");
            }
        }
    }
}
