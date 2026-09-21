using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using Hazel;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.3 callout notice ("言い当て通知", 2026-09-21 request after a public room where a player named the impostors).
    /// A role-seeing cheat (ESP) only reads the role data vanilla already sends to every client, so it sends nothing
    /// unusual; its one trace is what the cheater says. This watches living crewmates' meeting chat and tells the host
    /// (screen only, never a kick: a good read or a lucky guess looks the same) when someone keeps naming impostors
    /// nobody could know about yet.
    ///
    /// A named impostor counts (1 point) only when HIDDEN: alive, not the host (the host's impostor wish makes it
    /// guessable), no visible action yet this game (kill, vent, shapeshift or vanish/appear animation), not named first
    /// by anybody else this game (the host's own lines and impostors' lines included), and not "known" from earlier
    /// games of this lobby (an impostor in either of the last two real games, or in two or more of them: the public
    /// result line lists every game's impostors, and a designated player is an impostor game after game).
    /// A crewmate named in an accusing line is a wrong name, unless an impostor disguised as that colour this game, the
    /// crewmate is dead, or it is the speaker.
    ///
    /// Notice: in one game, 2 or more hidden impostors named with no wrong name; or over this lobby, 3 or more hidden
    /// impostors from 2 or more games with wrong names ≤ a third of them (re-armed after 2 more). Precision over recall:
    /// with the host an impostor every game and repeat impostors, most games hold at most one hidden impostor, so the
    /// lobby total is what catches a cheater who calls them game after game.
    /// The 2026-09-21 case ("インポ赤　青　コーラル": blue = the host, coral = last game's impostor, red = a crewmate —
    /// the speaker had seen a Maroon-disguised kill) scores 0 hidden with 1 wrong: no notice.
    /// Review 2026-09-21 (lifecycle / scoring / parser, 25 confirmed findings) shaped these rules.
    /// </summary>
    internal static class CalloutWatch
    {
        private sealed class Tally
        {
            public string Name = "";
            // over this lobby's finished games
            public int LobbyHits, LobbyWrong, LobbyGames;
            public int LobbyNoticedAt = -1;   // total hidden names at the last lobby notice
            // this game
            public readonly HashSet<string> Hidden = new HashSet<string>();
            public readonly Dictionary<string, string> Labels = new Dictionary<string, string>();   // every impostor named -> label
            public readonly HashSet<string> Wrong = new HashSet<string>();
            public bool NoticedGame;
        }

        private static readonly Dictionary<string, Tally> Tallies = new Dictionary<string, Tally>();
        private static readonly HashSet<string> ImpThisGame = new HashSet<string>();
        /// <summary>Impostors of the last two real games (newest first) and how many real games each key was an impostor in.</summary>
        private static readonly List<HashSet<string>> Recent = new List<HashSet<string>>();
        private static readonly Dictionary<string, int> ImpGames = new Dictionary<string, int>();
        private static bool _haisonGame;

        private static readonly HashSet<string> Acted = new HashSet<string>();
        private static readonly Dictionary<string, string> Named = new Dictionary<string, string>();   // impostor key -> first speaker
        private static readonly HashSet<int> DisguiseColors = new HashSet<int>();

        // ------------------------------------------------------------------ identity / lifecycle

        /// <summary>Identity across games of one lobby (a player id and a client id change when someone rejoins).</summary>
        internal static string Key(PlayerControl pc)
        {
            try
            {
                var d = pc.Data;
                if (d != null && !string.IsNullOrEmpty(d.FriendCode)) return "f:" + d.FriendCode;
                if (d != null && !string.IsNullOrEmpty(d.Puid)) return "p:" + d.Puid;
            }
            catch (Exception) { }
            return "c:" + pc.OwnerId;
        }

        /// <summary>
        /// SelectRoles begin: the previous real game's impostors join the history. A 廃村 game (lobby timer, /haison,
        /// F7) is skipped: its impostors were never announced, and it must not push the last real game out.
        /// </summary>
        internal static void OnRolesBegin()
        {
            _haisonGame = Core.Game.HaisonActive;
            if (_haisonGame) return;
            if (ImpThisGame.Count > 0)
            {
                Recent.Insert(0, new HashSet<string>(ImpThisGame));
                while (Recent.Count > 2) Recent.RemoveAt(Recent.Count - 1);
                foreach (var k in ImpThisGame) { ImpGames.TryGetValue(k, out int n); ImpGames[k] = n + 1; }
            }
            ImpThisGame.Clear();
        }

        /// <summary>SelectRoles end and intro end (idempotent: rebuilt from the role snapshot).</summary>
        internal static void OnRolesEnd()
        {
            if (_haisonGame || Core.Game.HaisonActive) return;
            ImpThisGame.Clear();
            foreach (var pc in Core.Game.AllPlayers())
                if (pc != null && pc.Data != null && CheatDetector.Roles.TryGetValue(pc.PlayerId, out var r) && CheatDetector.IsImpostorTeam(r))
                    ImpThisGame.Add(Key(pc));
        }

        /// <summary>Game start: fold the finished game into the lobby totals, clear the per-game state.</summary>
        internal static void OnGameStart()
        {
            foreach (var t in Tallies.Values)
            {
                t.LobbyHits += t.Hidden.Count;
                t.LobbyWrong += t.Wrong.Count;
                if (t.Hidden.Count > 0) t.LobbyGames++;
                t.Hidden.Clear(); t.Labels.Clear(); t.Wrong.Clear();
                t.NoticedGame = false;
            }
            Acted.Clear();
            Named.Clear();
            DisguiseColors.Clear();
        }

        /// <summary>
        /// /ac clear: the players' lobby totals. During a game this game's names and who named whom first stay (they are
        /// facts of the running game); in the lobby the finished game (folded in only at the next start) goes too.
        /// </summary>
        internal static void ClearTallies()
        {
            bool inGame = false;
            try { inGame = AmongUsClient.Instance != null && AmongUsClient.Instance.IsGameStarted; } catch (Exception) { }
            foreach (var t in Tallies.Values)
            {
                t.LobbyHits = 0; t.LobbyWrong = 0; t.LobbyGames = 0; t.LobbyNoticedAt = -1;
                if (!inGame) { t.Hidden.Clear(); t.Labels.Clear(); t.Wrong.Clear(); t.NoticedGame = false; }
            }
        }

        /// <summary>A different lobby: nothing carries over.</summary>
        internal static void OnLobbyChanged()
        {
            Tallies.Clear();
            ImpThisGame.Clear();
            Recent.Clear();
            ImpGames.Clear();
            _haisonGame = false;
            Acted.Clear();
            Named.Clear();
            DisguiseColors.Clear();
        }

        // ------------------------------------------------------------------ visible impostor actions

        /// <summary>A kill, vent, or vanish/appear by an impostor-team player: someone may have seen it.</summary>
        internal static void OnVisibleAction(PlayerControl pc)
        {
            if (pc == null || pc.Data == null) return;
            if (CheatDetector.Roles.TryGetValue(pc.PlayerId, out var r) && CheatDetector.IsImpostorTeam(r)) Acted.Add(Key(pc));
        }

        /// <summary>CheckShapeshift (55) from the shapeshifter's client: target (packed net id) + animate.</summary>
        internal static void OnShapeshiftCheck(PlayerControl pc, MessageReader reader)
        {
            if (pc == null || reader == null) return;
            uint target = 0;
            bool animate = false;
            int pos = reader.Position;
            try
            {
                target = reader.ReadPackedUInt32();
                if (reader.BytesRemaining > 0) animate = reader.ReadBoolean();
            }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            if (!animate) return;   // the silent reset every client sends when a round starts
            OnVisibleAction(pc);
            if (target == pc.NetId) return;   // shifting back to itself
            foreach (var p in Core.Game.AllPlayers())
            {
                if (p == null || p.NetId != target || p.Data == null) continue;
                try { DisguiseColors.Add(p.Data.DefaultOutfit.ColorId); } catch (Exception) { }
                break;
            }
        }

        // ------------------------------------------------------------------ meeting chat

        private static bool InMeeting()
        {
            try { return MeetingHud.Instance != null; }
            catch (Exception) { return false; }
        }

        /// <summary>RPC 33 (quick chat) just arrived from this player: the AddChat that follows carries real player names.</summary>
        internal static void MarkQuickChat(PlayerControl pc)
        {
            if (pc == null) return;
            _quickFrom = pc.PlayerId;
            _quickFrame = Time.frameCount;
        }
        private static byte _quickFrom = 255;
        private static int _quickFrame = -1;

        /// <summary>
        /// Every line another player's chat puts on the host's screen (Chat_AddChatPatch): typed chat and quick chat
        /// alike (vanilla renders a quick-chat phrase into text before AddChat). Impostors' lines are read too: an
        /// impostor naming a partner makes that partner public ("named first"). Only living senders (ghost chat is read by
        /// the dead only, and ghosts can watch kills).
        /// </summary>
        internal static void OnAddChat(PlayerControl pc, string text)
        {
            if (!Options.CheatCallout || pc == null || pc.AmOwner || string.IsNullOrEmpty(text)) return;
            if (!CheatDetector.IsActive() || !InMeeting()) return;
            if (pc.Data == null || pc.Data.Disconnected || pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId)) return;
            bool quick = _quickFrom == pc.PlayerId && _quickFrame == Time.frameCount;
            bool crew = CheatDetector.Roles.TryGetValue(pc.PlayerId, out var role) && !CheatDetector.IsImpostorTeam(role)
                        && !CheatDetector.IsImpostorTeam(CheatDetector.LiveRole(pc));
            Evaluate(pc, text, crew, quick);
        }

        /// <summary>The host's own typed meeting line (Chat_SendChatPatch): never scored, but what it names is public from now on.</summary>
        internal static void OnHostChat(string text)
        {
            if (!Options.CheatCallout || string.IsNullOrEmpty(text) || !CheatDetector.IsActive() || !InMeeting()) return;
            var lp = PlayerControl.LocalPlayer;
            if (lp != null) Evaluate(lp, text, false, false);
        }

        private static void Evaluate(PlayerControl speaker, string text, bool score, bool quick)
        {
            var players = new List<PlayerControl>();
            var cands = new List<CalloutParser.Cand>();
            foreach (var p in Core.Game.AllPlayers())
            {
                if (p == null || p.Data == null || p.Data.Disconnected) continue;
                int color = -1;
                try { color = p.Data.DefaultOutfit.ColorId; } catch (Exception) { }
                players.Add(p);
                cands.Add(new CalloutParser.Cand { Color = color, Name = Lang.StripTags(Core.Game.NameOf(p.PlayerId) ?? "") });
            }
            text = Lang.StripTags(text);
            bool zh = false;
            try { zh = Chat.Translator.Classify(text) == Chat.Translator.Script.Chinese; } catch (Exception) { }
            var parsed = CalloutParser.Parse(text, cands, zh, quick);
            if (parsed.Mentioned.Count == 0) return;
            string me = Key(speaker);
            score = score && parsed.Accusing && parsed.Targets.Count > 0;
            Tally t = null;
            if (score)
            {
                if (!Tallies.TryGetValue(me, out t)) { t = new Tally(); Tallies[me] = t; }
                t.Name = Lang.StripTags(Core.Game.NameOf(speaker.PlayerId) ?? "").Trim();
            }
            var log = new StringBuilder();
            bool counted = false, newHidden = false;
            if (score)
                foreach (int index in parsed.Targets)
                {
                    var target = players[index];
                    if (target == null || target.Data == null || target.Data.Disconnected || target.PlayerId == speaker.PlayerId) continue;
                    if (!CheatDetector.Roles.TryGetValue(target.PlayerId, out var tr)) continue;
                    string k = Key(target);
                    string label = Lang.StripTags(Core.Game.NameOf(target.PlayerId) ?? "").Trim();
                    int color = -1;
                    try { color = target.Data.DefaultOutfit.ColorId; } catch (Exception) { }
                    if (CheatDetector.IsImpostorTeam(tr))
                    {
                        string why = null;
                        if (target.AmOwner || Core.Game.IsHost(target.PlayerId)) why = "host";
                        else if (target.Data.IsDead) why = "dead";
                        else if (Acted.Contains(k)) why = "acted";
                        else if (Named.TryGetValue(k, out var first) && first != me) why = "named by another";
                        else if (Known(k)) why = "impostor in recent games";
                        else if (DisguiseColors.Contains(color)) why = "a partner disguised as this colour";   // the witness saw the disguise
                        if (why == null && t.Hidden.Add(k)) newHidden = true;
                        t.Labels[k] = label + "(" + RoleName(tr) + ")";
                        log.Append($" imp {label} {(why ?? "HIDDEN")};");
                        counted = true;
                    }
                    else
                    {
                        if (target.Data.IsDead) continue;
                        if (DisguiseColors.Contains(color)) { log.Append($" crew {label} skipped (an impostor disguised as that colour);"); continue; }
                        t.Wrong.Add(k);
                        log.Append($" crew {label} wrong;");
                        counted = true;
                    }
                }
            // everyone the line refers to is public from now on (after the scoring above: the speaker's own first mention still counts)
            foreach (int index in parsed.Mentioned)
            {
                var target = players[index];
                if (target == null || target.Data == null || target.PlayerId == speaker.PlayerId) continue;
                if (!CheatDetector.Roles.TryGetValue(target.PlayerId, out var tr) || !CheatDetector.IsImpostorTeam(tr)) continue;
                string k = Key(target);
                if (!Named.ContainsKey(k)) Named[k] = me;
            }
            if (!score || !counted) return;
            string quote = text.Length > 40 ? text.Substring(0, 40) + "…" : text;

            int hidden = t.Hidden.Count, wrong = t.Wrong.Count;
            bool all = ImpThisGame.Count >= 2;
            if (all) foreach (var k in ImpThisGame) if (!t.Labels.ContainsKey(k)) { all = false; break; }
            bool gameHit = wrong == 0 && hidden >= 2;
            int lobbyH = t.LobbyHits + hidden, lobbyW = t.LobbyWrong + wrong, lobbyG = t.LobbyGames + (hidden > 0 ? 1 : 0);
            bool lobbyHit = newHidden && lobbyH >= 3 && lobbyG >= 2 && lobbyW * 3 <= lobbyH
                            && (t.LobbyNoticedAt < 0 || lobbyH >= t.LobbyNoticedAt + 2);
            PocketRolesPlugin.Logger.LogInfo($"CheatDetector: callout #{speaker.PlayerId} {t.Name} '{quote}':{log} game hidden {hidden} wrong {wrong}{(all ? " (all impostors named)" : "")}; lobby {lobbyH} hidden / {lobbyW} wrong over {lobbyG} game(s)");

            var hiddenLabels = new List<string>();
            foreach (var k in t.Hidden) if (t.Labels.TryGetValue(k, out var l)) hiddenLabels.Add(l);
            string names = string.Join(Lang.T("ac.callout.sep", "、", ", ", "、"), hiddenLabels);
            if (gameHit && !t.NoticedGame)
            {
                t.NoticedGame = true;
                string extra = string.Format(Lang.T("ac.callout.detail",
                    " 言い当てた、まだ何もしていないインポスター: {0}{1} / 発言「{2}」",
                    " Hidden impostors named: {0}{1} / said \"{2}\"",
                    " 点中的尚未行动的内鬼: {0}{1} / 发言「{2}」"),
                    names, all ? Lang.T("ac.callout.all", "（インポスター全員の名前を挙げた）", " (named every impostor)", "（点了全部内鬼）") : "", quote);
                CheatDetector.Report(CheatDetector.Rule.Callout, speaker, $"hidden {hidden}, wrong {wrong}{(all ? ", all impostors" : "")}: '{quote}'", false, false, false, extra);
            }
            else if (lobbyHit)
            {
                t.LobbyNoticedAt = lobbyH;
                string extra = string.Format(Lang.T("ac.callout.lobby",
                    " この部屋の {0} 試合で、まだ何もしていないインポスターを計 {1} 回言い当てています（外れ {2} 回）。最新の発言「{3}」",
                    " Over {0} games here: {1} hidden impostors named, {2} wrong. Latest: \"{3}\"",
                    " 本房间 {0} 局中，共点中尚未行动的内鬼 {1} 次（错 {2} 次）。最新发言「{3}」"),
                    lobbyG, lobbyH, lobbyW, quote);
                CheatDetector.Report(CheatDetector.Rule.CalloutRepeat, speaker, $"lobby hidden {lobbyH}, wrong {lobbyW}, games {lobbyG}: '{quote}'", false, false, false, extra);
            }
        }

        /// <summary>An impostor in either of the last two real games, or in two or more of this lobby's games.</summary>
        private static bool Known(string k)
        {
            foreach (var set in Recent) if (set.Contains(k)) return true;
            return ImpGames.TryGetValue(k, out int n) && n >= 2;
        }

        private static string RoleName(RoleTypes r)
        {
            switch (r)
            {
                case RoleTypes.Shapeshifter: return Lang.T("ac.callout.role.ss", "シェイプシフター", "Shapeshifter", "变形者");
                case RoleTypes.Phantom: return Lang.T("ac.callout.role.phantom", "ファントム", "Phantom", "幻影");
                case RoleTypes.Viper: return Lang.T("ac.callout.role.viper", "ヴァイパー", "Viper", "毒蛇");
                default: return Lang.T("ac.callout.role.imp", "インポスター", "Impostor", "内鬼");
            }
        }

        // ------------------------------------------------------------------ spoiler hold

        /// <summary>The host is a living crewmate in a running game: a callout notice would spoil the game for the host.</summary>
        internal static bool HoldNow()
        {
            try
            {
                var c = AmongUsClient.Instance;
                var lp = PlayerControl.LocalPlayer;
                if (c == null || !c.IsGameStarted || lp == null || lp.Data == null || lp.Data.IsDead) return false;
                if (CheatDetector.Roles.TryGetValue(lp.PlayerId, out var r) && CheatDetector.IsImpostorTeam(r)) return false;
                return !CheatDetector.IsImpostorTeam(CheatDetector.LiveRole(lp));
            }
            catch (Exception) { return false; }
        }
    }
}
