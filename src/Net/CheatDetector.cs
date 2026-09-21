using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using InnerNet;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.3 host-side cheat detection (2026-09-21 request: "anti-cheat like Vanguard; cheaters should leave the room
    /// fast"). Nothing runs on the players' devices (they play vanilla); the host watches the broadcasts and requests it
    /// receives anyway and flags behaviour a vanilla 2026.8.18 client never produces.
    ///
    /// Unregistered (compat) lobbies only for the game rules: there every kill / vent / ability / task / report is a client
    /// message the host merely observes (no host authority), and only vanilla roles exist, so "may this player do that"
    /// has one answer. Registered lobbies already route kills / vents / sabotage through the host (Kills.cs) and use
    /// per-client role views, so these rules stay off there. The unknown-RPC rule works in both modes.
    ///
    /// Roles: a CERTAIN verdict needs two sources to agree — <see cref="Roles"/>, a snapshot of the host's own table at
    /// the end of vanilla's SelectRoles (the host's CoSetRole applies roles synchronously, verified in the 2026-09-08
    /// traces), and the live NetworkedPlayerInfo.RoleType (ghost roles mapped to their team). A forged mid-game SetRole
    /// can therefore neither frame an innocent impostor nor hide a cheater; a disagreement only yields a host notice.
    /// Every payload peek restores the reader position in a finally block (a shifted reader desyncs the host — the
    /// v0.4.4 lesson), and every patch only observes (void prefixes, vanilla always runs).
    ///
    /// Levels: Certain = impossible for a vanilla client (auto-kick with a room ban on the first hit, [AntiCheat]
    /// AutoKick, default on); Repeat = kicked on the second separate hit within the same game; Notice = host screen only
    /// (lag / spoofing possible). A GameData RPC carries no sender id, so the owner of the addressed object is the suspect.
    /// </summary>
    public static class CheatDetector
    {
        internal enum Rule
        {
            KillRole, VentRole, AbilityRole, TaskImpostor, ChatAlive,
            KillDead, SabotageCrew, KillCooldown, ProtectAlive, TaskUnknown, KillDistance, RpcUnknown,
            TaskBurst, ReportForge, Teleport, KillPhase,
            Callout, CalloutRepeat,   // CalloutWatch: meeting chat naming impostors nobody could know yet (notice only)
        }
        internal enum Level { Certain, Repeat, Notice }

        internal static Level LevelOf(Rule r)
        {
            switch (r)
            {
                case Rule.KillRole: case Rule.VentRole: case Rule.AbilityRole: case Rule.TaskImpostor: return Level.Certain;
                case Rule.ChatAlive: return Level.Repeat;
                default: return Level.Notice;
            }
        }

        internal static string Text(Rule r)
        {
            switch (r)
            {
                case Rule.KillRole: return Lang.T("ac.rule.killrole", "キルできない役職なのにキルした", "killed without a killing role", "没有击杀能力却击杀了");
                case Rule.VentRole: return Lang.T("ac.rule.vent", "ベントを使えない役職なのにベントに入った", "entered a vent without a venting role", "不能使用通风管却进入了通风管");
                case Rule.AbilityRole: return Lang.T("ac.rule.ability", "持っていない能力(変身や透明化)を使った", "used an ability their role does not have", "使用了自己职业没有的能力");
                case Rule.TaskImpostor: return Lang.T("ac.rule.task", "インポスターなのにタスクを完了した", "completed a task as an impostor", "内鬼却完成了任务");
                case Rule.ChatAlive: return Lang.T("ac.rule.chat", "生きているのに会議の外でチャットした", "chatted outside a meeting while alive", "存活时在会议外聊天");
                case Rule.KillDead: return Lang.T("ac.rule.killdead", "死んでいるのにキルした", "killed while dead", "死亡后仍然击杀");
                case Rule.SabotageCrew: return Lang.T("ac.rule.sabotage", "クルーなのにサボタージュした", "sabotaged as a crewmate", "船员却发动了破坏");
                case Rule.KillCooldown: return Lang.T("ac.rule.killcd", "クールダウンより明らかに早くキルした", "killed far faster than the kill cooldown", "远快于冷却时间的击杀");
                case Rule.ProtectAlive: return Lang.T("ac.rule.protect", "生きているのに守護天使の守護を使った", "used a Guardian Angel protect while alive", "存活时使用了守护天使的守护");
                case Rule.TaskUnknown: return Lang.T("ac.rule.taskid", "持っていないタスクを完了した", "completed a task they do not have", "完成了自己没有的任务");
                case Rule.KillDistance: return Lang.T("ac.rule.distance", "遠すぎる位置からキルした(ラグの可能性あり)", "killed from too far away (could be lag)", "从过远的位置击杀(可能是延迟)");
                case Rule.RpcUnknown: return Lang.T("ac.rule.rpc", "普通のAmong Usにない通信を送った(改造版やチートツールの可能性)", "sent a message vanilla Among Us never sends (modded client or cheat tool)", "发送了原版Among Us没有的通信(可能是修改版或作弊工具)");
                case Rule.TaskBurst: return Lang.T("ac.rule.taskburst", "ありえない速さでタスクを完了した", "completed tasks impossibly fast", "以不可能的速度完成了任务");
                case Rule.ReportForge: return Lang.T("ac.rule.report", "ありえない通報をした(生きている人の死体、死んだ後の通報)", "made an impossible report (a living player's body, or while dead)", "发出了不可能的举报(活人的尸体或死后举报)");
                case Rule.Teleport: return Lang.T("ac.rule.teleport", "試合中に瞬間移動した", "teleported during the game", "在对局中瞬间移动");
                case Rule.KillPhase: return Lang.T("ac.rule.killphase", "会議中や追放画面でキルした", "killed during a meeting or the exile screen", "在会议或放逐画面中击杀");
                case Rule.Callout: return Lang.T("ac.rule.callout", "まだ何もしていないインポスターを会議で言い当てた(インポスターが見えるチートの可能性)", "named impostors nobody could know yet (possible role-seeing cheat)", "在会议中点中了尚未行动的内鬼(可能是能看到内鬼的作弊)");
                case Rule.CalloutRepeat: return Lang.T("ac.rule.calloutrepeat", "何もしていないインポスターを何試合も言い当てている(インポスターが見えるチートの可能性)", "keeps naming impostors nobody could know yet (possible role-seeing cheat)", "多局点中尚未行动的内鬼(可能是能看到内鬼的作弊)");
            }
            return r.ToString();
        }

        private sealed class Suspect
        {
            public string Name = "";
            public byte PlayerId = 255;
            public bool Left, Kicked, KickQueued, Exempt;
            public readonly Dictionary<Rule, int> Hits = new Dictionary<Rule, int>();
            public readonly HashSet<Rule> NoticedThisGame = new HashSet<Rule>();
            public readonly Dictionary<Rule, float> LastLogAt = new Dictionary<Rule, float>();
            public readonly Dictionary<Rule, int> Suppressed = new Dictionary<Rule, int>();
            // Repeat-level counters: per GAME (cleared in ResetGame) — two late lines in unrelated games never add up
            public readonly Dictionary<Rule, float> LastEventAt = new Dictionary<Rule, float>();
            public readonly Dictionary<Rule, int> Events = new Dictionary<Rule, int>();
        }

        private static readonly Dictionary<int, Suspect> Suspects = new Dictionary<int, Suspect>();
        private static readonly HashSet<long> UnknownRpcLogged = new HashSet<long>();

        /// <summary>Vanilla roles of this game, from the host's own table at the end of SelectRoles (frozen; never from incoming SetRole).</summary>
        internal static readonly Dictionary<byte, RoleTypes> Roles = new Dictionary<byte, RoleTypes>();

        // per game timing (Time.time)
        private static bool _prevStarted, _prevIntro, _prevMeeting, _prevExile;
        private static float _introEndAt = -1f, _lastPauseEnd = -100f, _roundStartAt = -1f, _lastExileEndAt = -100f;
        private static float _meetingStartAt = -100f, _exileStartAt = -100f, _gameEndAt = -100f;
        private static bool _roundStartIsExile;
        private static int _meetingCount;
        private static readonly Dictionary<int, float> LastKillAt = new Dictionary<int, float>();
        private static readonly Dictionary<byte, float> DeathAt = new Dictionary<byte, float>();
        private static readonly Dictionary<int, List<float>> TaskTimes = new Dictionary<int, List<float>>();
        private static readonly HashSet<int> TaskBurstThisRound = new HashSet<int>();
        private static readonly Dictionary<int, float> LastMoveRpcAt = new Dictionary<int, float>();   // ladder / platform / zipline

        private struct PendingChat { public byte PlayerId; public float At; public int Meetings; }
        private static readonly Dictionary<int, PendingChat> Pending = new Dictionary<int, PendingChat>();
        private struct PendingReport { public int Owner; public byte Reporter; public byte Target; public bool ReporterDead; public float At; }
        private static readonly List<PendingReport> Reports = new List<PendingReport>();
        private static readonly List<KeyValuePair<int, Rule>> KickQueue = new List<KeyValuePair<int, Rule>>();
        private static readonly Queue<string> NoticeBacklog = new Queue<string>();
        /// <summary>Public removal lines held while the host is dead in a compat game (living players cannot see a dead sender's chat).</summary>
        private static readonly List<string> PublicBacklog = new List<string>();
        /// <summary>Callout notices held while the host is a living crewmate (they name impostors: a spoiler for the host).</summary>
        private static readonly List<string> SpoilerBacklog = new List<string>();
        private static float _lastNoticeAt = -100f;
        private const float NoticeSpacing = 1.2f;

        /// <summary>RPC ids of RpcCalls in 2026.8.18 (the deprecated 9/10/17/36/37 are not sent by current clients).</summary>
        private static readonly HashSet<byte> VanillaRpcIds = new HashSet<byte>
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 29, 31, 32, 33, 34, 35,
            38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 60, 61, 62, 63, 64, 65, 66,
        };

        // ------------------------------------------------------------------ gates

        private static bool HostWatching()
        {
            var c = AmongUsClient.Instance;
            return c != null && c.AmHost && Options.CheatDetect && Core.Game.IsHostActive;
        }

        /// <summary>The game rules: an unregistered lobby, a real (non-haison) game running, roles known.</summary>
        internal static bool IsActive() => Active();

        private static bool Active()
        {
            if (!HostWatching() || !Registration.CompatMode) return false;
            var c = AmongUsClient.Instance;
            if (!c.IsGameStarted || ShipStatus.Instance == null) return false;
            if (Core.Game.HaisonActive || Core.Game.Ending) return false;
            return Roles.Count > 0;
        }

        internal static bool IsImpostorTeam(RoleTypes r) => r == RoleTypes.Impostor || r == RoleTypes.Shapeshifter || r == RoleTypes.Phantom || r == RoleTypes.Viper;
        private static bool CanVent(RoleTypes r) => IsImpostorTeam(r) || r == RoleTypes.Engineer;

        private static bool Paused()
        {
            try { return MeetingHud.Instance != null || ExileController.Instance != null || IntroCutscene.Instance != null; }
            catch (Exception) { return false; }
        }

        private static bool Suspectable(PlayerControl pc)
        {
            if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) return false;
            if (Core.Game.GameMasterActive && Core.Game.IsHost(pc.PlayerId)) return false;
            return true;
        }

        private static PlayerControl ByNetId(uint netId)
        {
            foreach (var pc in Core.Game.AllPlayers())
                if (pc != null && pc.NetId == netId) return pc;
            return null;
        }

        // ------------------------------------------------------------------ roles

        internal static void OnSelectRolesBegin() { CalloutWatch.OnRolesBegin(); Roles.Clear(); }

        /// <summary>RoleManager.SelectRoles postfix (after the mod's own postfix: plain roles, impostor fill, HostWish, Designate).</summary>
        internal static void OnSelectRolesEnd()
        {
            int n = 0;
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc == null || pc.Data == null || pc.Data.Role == null) continue;
                var r = pc.Data.Role.Role;
                if (r == RoleTypes.CrewmateGhost || r == RoleTypes.ImpostorGhost || r == RoleTypes.GuardianAngel) continue;
                Roles[pc.PlayerId] = r;
                n++;
            }
            PocketRolesPlugin.Logger.LogInfo($"CheatDetector: role snapshot of {n} player(s) taken at SelectRoles end");
            CalloutWatch.OnRolesEnd();
        }

        /// <summary>Intro end: players whose role was not visible at SelectRoles end are added once (never overwritten).</summary>
        private static void FillMissingRoles()
        {
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc == null || pc.Data == null || pc.Data.Role == null || Roles.ContainsKey(pc.PlayerId)) continue;
                var r = pc.Data.Role.Role;
                if (r == RoleTypes.CrewmateGhost || r == RoleTypes.ImpostorGhost || r == RoleTypes.GuardianAngel) continue;
                Roles[pc.PlayerId] = r;
                PocketRolesPlugin.Logger.LogInfo($"CheatDetector: role of #{pc.PlayerId} {Core.Game.NameOf(pc.PlayerId)} taken at intro end: {r}");
            }
            CalloutWatch.OnRolesEnd();
        }

        private static bool RoleOf(PlayerControl pc, out RoleTypes role)
        {
            role = RoleTypes.Crewmate;
            return pc != null && Roles.TryGetValue(pc.PlayerId, out role);
        }

        /// <summary>The live role on the host (ghost roles mapped to their team: ImpostorGhost → Impostor, others → Crewmate).</summary>
        private static RoleTypes Live(PlayerControl pc)
        {
            try
            {
                var r = pc.Data.RoleType;
                if (r == RoleTypes.ImpostorGhost) return RoleTypes.Impostor;
                if (r == RoleTypes.CrewmateGhost || r == RoleTypes.GuardianAngel) return RoleTypes.Crewmate;
                return r;
            }
            catch (Exception) { return RoleTypes.Crewmate; }
        }

        internal static RoleTypes LiveRole(PlayerControl pc) => Live(pc);

        // ------------------------------------------------------------------ RPC observers (called from the patches)

        internal static void OnPlayerRpc(PlayerControl pc, byte callId, MessageReader reader)
        {
            if (!HostWatching() || !Suspectable(pc)) return;
            if (!VanillaRpcIds.Contains(callId))
            {
                long key = ((long)pc.OwnerId << 8) | callId;
                if (UnknownRpcLogged.Add(key)) Report(Rule.RpcUnknown, pc, "RPC " + callId, false, false);
                return;
            }
            if (!Active()) return;
            switch (callId)
            {
                case 12: CalloutWatch.OnVisibleAction(pc); OnMurder(pc, reader); break;
                case 1: OnCompleteTask(pc, reader); break;
                case 11: OnReport(pc, reader); break;
                case 13: OnChat(pc); CalloutWatch.OnChat(pc, reader); break;
                case 33: OnChat(pc); break;
                case 46: case 55:
                    if (RoleOf(pc, out var ss) && ss != RoleTypes.Shapeshifter)
                        Report(Rule.AbilityRole, pc, $"RPC {callId}, role {ss} (live {Live(pc)})", false, false, Live(pc) != RoleTypes.Shapeshifter);
                    if (callId == 55) CalloutWatch.OnShapeshiftCheck(pc, reader);
                    break;
                case 62: case 63: case 64: case 65:
                    if (RoleOf(pc, out var ph) && ph != RoleTypes.Phantom)
                        Report(Rule.AbilityRole, pc, $"RPC {callId}, role {ph} (live {Live(pc)})", false, false, Live(pc) != RoleTypes.Phantom);
                    CalloutWatch.OnVisibleAction(pc);
                    break;
                case 45:
                    if (!pc.Data.IsDead) Report(Rule.ProtectAlive, pc, "ProtectPlayer while alive", false, false);
                    break;
                case 32: case 51: case 52: LastMoveRpcAt[pc.OwnerId] = Time.time; break;   // platform / zipline
            }
        }

        private static void OnMurder(PlayerControl killer, MessageReader reader)
        {
            if (!RoleOf(killer, out var role)) return;
            PlayerControl target = null;
            int flags = 0;
            if (reader != null)
            {
                int pos = reader.Position;
                try
                {
                    target = ByNetId(reader.ReadPackedUInt32());
                    if (reader.BytesRemaining >= 4) flags = reader.ReadInt32();
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"CheatDetector: MurderPlayer peek failed: {e.Message}"); }
                finally { reader.Position = pos; }
            }
            float now = Time.time;
            if (target != null && (flags & 1) != 0) DeathAt[target.PlayerId] = now;
            if (!IsImpostorTeam(role))
            {
                // certain only when the live role agrees (a stale or poisoned table must never kick an innocent impostor)
                Report(Rule.KillRole, killer, $"role {role} (live {Live(killer)})", false, false, !IsImpostorTeam(Live(killer)));
                return;
            }
            if (target != null && target.Data != null && !target.Data.IsDead && RoleOf(target, out var tr) && IsImpostorTeam(tr))
            {
                Report(Rule.KillRole, killer, $"killed impostor-team {Core.Game.NameOf(target.PlayerId)} ({tr})", false, false, IsImpostorTeam(Live(target)));
                return;
            }
            if (killer.Data.IsDead && now - _lastExileEndAt > 5f) Report(Rule.KillDead, killer, "killer is dead", false, false);
            try
            {
                bool meeting = MeetingHud.Instance != null, exile = ExileController.Instance != null;
                if ((meeting && now - _meetingStartAt > 4f) || (exile && now - _exileStartAt > 2f))
                    Report(Rule.KillPhase, killer, meeting ? $"{now - _meetingStartAt:0.0}s into a meeting" : "during the exile screen", false, false);
            }
            catch (Exception) { }
            // cooldown: only against a reference vanilla also resets (a previous kill of this round, or the end of an exile)
            try
            {
                float cd = global::PocketRoles.Game.Kills.LobbyKillCooldown();
                float limit = cd * 0.5f - 2f;
                LastKillAt.TryGetValue(killer.OwnerId, out float last);
                float reference = last > 0f && last >= _roundStartAt ? last : (_roundStartIsExile ? _roundStartAt : -1f);
                if (limit > 1f && reference > 0f && now - reference < limit)
                    Report(Rule.KillCooldown, killer, $"{now - reference:0.0}s after the last reset (cooldown {cd:0.#}s)", false, false);
                if ((flags & 1) != 0) LastKillAt[killer.OwnerId] = now;   // MurderResultFlags.Succeeded (a protected attempt does not count)
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"CheatDetector: cooldown check: {e.Message}"); }
            // distance (host notice only: interpolated positions lag behind on phones)
            try
            {
                if (target != null)
                {
                    float kd = 1.8f;
                    var gm = GameManager.Instance;
                    if (gm != null && gm.LogicOptions != null) kd = gm.LogicOptions.GetKillDistance();
                    float d = Vector2.Distance(killer.GetTruePosition(), target.GetTruePosition());
                    if (d > kd * 2f + 3f) Report(Rule.KillDistance, killer, $"distance {d:0.0} (kill distance {kd:0.#})", false, false);
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"CheatDetector: distance check: {e.Message}"); }
        }

        private static void OnCompleteTask(PlayerControl pc, MessageReader reader)
        {
            float now = Time.time;
            if (RoleOf(pc, out var role) && IsImpostorTeam(role))
            {
                Report(Rule.TaskImpostor, pc, $"role {role} (live {Live(pc)})", false, false, IsImpostorTeam(Live(pc)));
                return;
            }
            // "complete all tasks": several completions far faster than any task can be done
            try
            {
                if (!TaskTimes.TryGetValue(pc.OwnerId, out var times)) { times = new List<float>(); TaskTimes[pc.OwnerId] = times; }
                times.Add(now);
                int n = times.Count;
                bool burst = (n >= 4 && now - times[n - 4] < 2f) || (n >= 3 && _roundStartAt > 0f && now - _roundStartAt < 1.5f * n);
                if (burst && TaskBurstThisRound.Add(pc.OwnerId))
                    Report(Rule.TaskBurst, pc, $"{n} task(s) this round, the last 4 within {(n >= 4 ? now - times[n - 4] : now - times[0]):0.0}s", false, false);
            }
            catch (Exception) { }
            if (reader == null) return;
            uint idx = 0;
            int pos = reader.Position;
            try { idx = reader.ReadPackedUInt32(); }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            try
            {
                var tasks = pc.Data.Tasks;
                if (tasks != null && tasks.Count > 0 && pc.Data.FindTaskById(idx) == null)
                    Report(Rule.TaskUnknown, pc, "task id " + idx, false, false);
            }
            catch (Exception) { }
        }

        /// <summary>ReportDeadBody (11): the reporter's own client sends it to the host (target PlayerId, 255 = emergency button).</summary>
        private static void OnReport(PlayerControl pc, MessageReader reader)
        {
            if (reader == null || Paused() || Time.time - _lastPauseEnd < 3f) return;
            byte target = 255;
            int pos = reader.Position;
            try { if (reader.BytesRemaining > 0) target = reader.ReadByte(); }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            bool dead = false;
            if (pc.Data.IsDead)
            {
                // dead for a while (not a report in flight when the reporter was killed)
                if (DeathAt.TryGetValue(pc.PlayerId, out float died)) dead = Time.time - died > 3f;
                else dead = Time.time - _lastExileEndAt > 5f;
            }
            if (Reports.Count < 16)
                Reports.Add(new PendingReport { Owner = pc.OwnerId, Reporter = pc.PlayerId, Target = target, ReporterDead = dead, At = Time.time });
        }

        private static void OnChat(PlayerControl pc)
        {
            if (pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId)) return;
            if (Paused() || _introEndAt < 0f) return;
            float now = Time.time;
            if (now - _lastPauseEnd < 8f) return;   // a meeting line delivered late by a stalled phone
            if (Pending.ContainsKey(pc.OwnerId)) return;
            Pending[pc.OwnerId] = new PendingChat { PlayerId = pc.PlayerId, At = now, Meetings = _meetingCount };
        }

        internal static void OnPhysicsRpc(PlayerPhysics physics, byte callId)
        {
            if (physics == null) return;
            var pc = physics.myPlayer;
            if (!HostWatching() || !Suspectable(pc) || !Active()) return;
            if (callId == 31 || callId == 32) { LastMoveRpcAt[pc.OwnerId] = Time.time; return; }   // ladder / platform
            if (callId == 19 || callId == 20) CalloutWatch.OnVisibleAction(pc);   // a vent jump can be seen
            if (callId != 19) return;   // EnterVent
            if (pc.Data.IsDead) return;   // a vent press in flight when the player was killed
            if (RoleOf(pc, out var role) && !CanVent(role))
                Report(Rule.VentRole, pc, $"role {role} (live {Live(pc)})", false, false, !CanVent(Live(pc)));
        }

        /// <summary>SnapTo (21) on a player's network transform: vanilla sends it at the intro, at meeting start, for the Airship spawn picker and for vent moves.</summary>
        internal static void OnSnapTo(CustomNetworkTransform cnt)
        {
            if (cnt == null) return;
            var pc = cnt.myPlayer;
            if (!HostWatching() || !Suspectable(pc) || !Active()) return;
            if (pc.Data.IsDead || Paused() || _introEndAt < 0f) return;
            float now = Time.time;
            if (now - _lastPauseEnd < 15f) return;   // spawn picker (Airship) and any snap tied to a meeting / exile / intro
            try { if (pc.onLadder || pc.inMovingPlat || pc.walkingToVent) return; } catch (Exception) { }
            if (LastMoveRpcAt.TryGetValue(pc.OwnerId, out float mv) && now - mv < 10f) return;
            RoleOf(pc, out var role);
            bool inVent = false;
            try { inVent = pc.inVent; } catch (Exception) { }
            if (CanVent(role) && inVent) return;   // vent-to-vent move
            Report(Rule.Teleport, pc, $"SnapTo mid-round (role {role}, inVent {inVent})", false, false);
        }

        internal static void OnUpdateSystem(SystemTypes systemType, PlayerControl player, MessageReader reader)
        {
            if (systemType != SystemTypes.Sabotage || reader == null) return;
            if (!HostWatching() || !Suspectable(player) || !Active()) return;
            if (Paused() || Time.time - _lastPauseEnd < 3f) return;
            byte target = 0;
            int pos = reader.Position;
            try { if (reader.BytesRemaining > 0) target = reader.ReadByte(); }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            var t = (SystemTypes)target;
            bool sabotage = t == SystemTypes.Reactor || t == SystemTypes.Electrical || t == SystemTypes.LifeSupp || t == SystemTypes.Comms
                            || t == SystemTypes.Laboratory || t == SystemTypes.MushroomMixupSabotage || t == SystemTypes.HeliSabotage;
            if (!sabotage) return;
            if (RoleOf(player, out var role) && !IsImpostorTeam(role)) Report(Rule.SabotageCrew, player, $"{t} by role {role} (the actor is payload data: easy to spoof)", false, false);
        }

        // ------------------------------------------------------------------ reporting

        private static Suspect Get(int clientId, PlayerControl pc)
        {
            if (!Suspects.TryGetValue(clientId, out var s)) { s = new Suspect(); Suspects[clientId] = s; }
            if (pc != null)
            {
                s.PlayerId = pc.PlayerId;
                string n = Lang.StripTags(Core.Game.NameOf(pc.PlayerId) ?? "").Trim();
                if (n.Length > 0) s.Name = n;
            }
            if (s.Name.Length == 0) s.Name = "#" + s.PlayerId;
            return s;
        }

        /// <summary>
        /// One detection. Counts it, logs it (at most once per 5 s per player and rule, with a suppressed count), shows
        /// the host one notice per player and rule per game, and queues the auto-kick when the rule's level calls for it.
        /// <paramref name="mayKick"/> false turns a Certain / Repeat hit into a notice (the two role sources disagree).
        /// <paramref name="simulated"/> (/ac test): notice and log only; the real counters are never touched, and the kick
        /// flow runs only when <paramref name="simKick"/> is set. Returns true when a kick was queued.
        /// </summary>
        internal static bool Report(Rule rule, PlayerControl pc, string detail, bool simulated, bool simKick, bool mayKick = true, string extra = null)
        {
            try
            {
                if (pc == null) return false;
                int clientId = pc.OwnerId;
                var s = Get(clientId, pc);
                float now = Time.time;
                var level = LevelOf(rule);
                if (!mayKick && level != Level.Notice) level = Level.Notice;
                string tag = simulated ? "[test] " : "";
                if (!simulated)
                {
                    s.Hits.TryGetValue(rule, out int h);
                    s.Hits[rule] = h + 1;
                }
                if (!s.LastLogAt.TryGetValue(rule, out float lastLog) || now - lastLog >= 5f || simulated)
                {
                    s.Suppressed.TryGetValue(rule, out int sup);
                    PocketRolesPlugin.Logger.LogWarning($"CheatDetector: {tag}{rule} ({level}) #{s.PlayerId} {s.Name} (client {clientId}): {detail}{(sup > 0 ? $" (+{sup} more since the last line)" : "")}");
                    s.LastLogAt[rule] = now;
                    s.Suppressed[rule] = 0;
                }
                else
                {
                    s.Suppressed.TryGetValue(rule, out int sup);
                    s.Suppressed[rule] = sup + 1;
                }
                bool autoKicks = Options.CheatAutoKick && (level == Level.Certain || level == Level.Repeat) && !s.Exempt;
                if (simulated || s.NoticedThisGame.Add(rule))
                {
                    string text = string.Format(Lang.T("ac.notice",
                        "チートの疑い: {0} - {1}",
                        "Cheat suspected: {0} - {1}",
                        "疑似作弊: {0} - {1}"), s.Name, Text(rule));
                    bool callout = rule == Rule.Callout || rule == Rule.CalloutRepeat;
                    if (!string.IsNullOrEmpty(extra)) text += extra;
                    if (callout)
                        text += Lang.T("ac.notice.callout", "（推理や勘が当たっただけのこともあります。退出させるならホストが /kick）", " (could be a good read; /kick to remove)", "（也可能只是推理或猜中了。需要移出请用 /kick）");
                    else if (!autoKicks)
                        text += Lang.T("ac.notice.manual", "（なりすましやラグの可能性もあります。退出させるならホストが /kick）", " (could be spoofing or lag; /kick to remove)", "（也可能是冒充或延迟。需要移出请用 /kick）");
                    text = (simulated ? Lang.T("ac.test.tag", "[テスト] ", "[test] ", "[测试] ") : "") + text;
                    if (callout && !simulated && CalloutWatch.HoldNow())
                    {
                        // it names impostors: the host, a living crewmate, sees it once dead or after the game
                        if (SpoilerBacklog.Count < 12) SpoilerBacklog.Add(Lang.T("ac.callout.held", "[試合中の記録] ", "[from the game] ", "[对局中的记录] ") + text);
                        PocketRolesPlugin.Logger.LogInfo("CheatDetector: callout notice held until the host is dead or the game is over");
                    }
                    else Notice(text);
                }
                bool due;
                if (simulated) due = simKick;
                else if (level == Level.Certain) due = true;
                else if (level == Level.Repeat)
                {
                    due = false;
                    s.LastEventAt.TryGetValue(rule, out float lastEvent);
                    if (lastEvent <= 0f || now - lastEvent >= 10f)
                    {
                        s.Events.TryGetValue(rule, out int ev);
                        s.Events[rule] = ev + 1;
                        s.LastEventAt[rule] = now;
                        due = ev + 1 >= 2;
                    }
                }
                else due = false;
                if (due && Options.CheatAutoKick && !s.Kicked && !s.KickQueued && !s.Exempt)
                {
                    s.KickQueued = true;
                    KickQueue.Add(new KeyValuePair<int, Rule>(clientId, rule));
                    return true;
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector.Report: {e}"); }
            return false;
        }

        private static void Notice(string text)
        {
            if (Time.time - _lastNoticeAt >= NoticeSpacing && NoticeBacklog.Count == 0)
            {
                _lastNoticeAt = Time.time;
                Chat.Chat.Local(Chat.Chat.Title, text);
            }
            else if (NoticeBacklog.Count < 20) NoticeBacklog.Enqueue(text);
        }

        private static bool HostDeadInGame()
        {
            try
            {
                var c = AmongUsClient.Instance;
                var lp = PlayerControl.LocalPlayer;
                return c != null && c.IsGameStarted && lp != null && lp.Data != null && lp.Data.IsDead;
            }
            catch (Exception) { return false; }
        }

        private static void RunKicks()
        {
            if (KickQueue.Count == 0) return;
            var item = KickQueue[0];   // one per frame
            KickQueue.RemoveAt(0);
            int clientId = item.Key;
            if (!Suspects.TryGetValue(clientId, out var s)) return;
            s.KickQueued = false;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || clientId == client.ClientId) return;
            PlayerControl pc = null;
            foreach (var p in Core.Game.AllPlayers())
                if (p != null && p.OwnerId == clientId) { pc = p; break; }
            if (pc == null || pc.Data == null || pc.Data.Disconnected) return;
            string reason = Text(item.Value);
            if (Permissions.LevelOf(pc) != PermLevel.Player)
            {
                s.Exempt = true;   // no further attempt this lobby; /ac shows it
                Notice(string.Format(Lang.T("ac.exempt", "{0} はVIP以上なので自動では退出させませんでした（{1}）", "{0} is VIP or above: not removed automatically ({1})", "{0} 是VIP以上，未自动移出（{1}）"), s.Name, reason));
                return;
            }
            try
            {
                s.Kicked = true;
                PocketRolesPlugin.Logger.LogWarning($"CheatDetector: removing #{s.PlayerId} {s.Name} (client {clientId}) with a room ban: {item.Value}");
                client.KickPlayer(clientId, true);
                string local = string.Format(Lang.T("ac.kicked", "{0} を自動で退出させました（{1}）。記録は /ac", "Removed {0} automatically ({1}). Records: /ac", "已自动移出 {0}（{1}）。记录: /ac"), s.Name, reason);
                if (Options.CheatAnnounceKick)
                {
                    string pub;
                    using (Lang.Scope(Lang.Default))
                        pub = string.Format(Lang.T("ac.kicked.public", "{0} はありえない操作({1})をしたので退出になりました", "{0} removed: impossible action ({1})", "{0} 因不可能的操作({1})被移出"), s.Name, Text(item.Value));
                    if (Registration.CompatMode && HostDeadInGame())
                    {
                        // a dead sender's chat is hidden from the living: show it when everyone is back in the lobby
                        if (PublicBacklog.Count < 8) PublicBacklog.Add(pub);
                        local += Lang.T("ac.kicked.later", " 全員へのお知らせは、ホストが死亡中なので試合後にロビーで出します。", " The public line waits for the lobby (the host is dead).", " 主持已死亡，公告将在赛后大厅发出。");
                    }
                    else Chat.Chat.All(Chat.Chat.Title, pub);
                }
                Chat.Chat.Local(Chat.Chat.Title, local);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector kick: {e}"); }
        }

        // ------------------------------------------------------------------ per frame

        internal static void Tick()
        {
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost) { _prevStarted = false; return; }
            float now = Time.time;
            bool started = c.IsGameStarted;
            if (started && !_prevStarted) ResetGame(now);
            if (!started && _prevStarted) _gameEndAt = now;
            _prevStarted = started;
            bool intro = false, meeting = false, exile = false;
            try { intro = IntroCutscene.Instance != null; meeting = MeetingHud.Instance != null; exile = ExileController.Instance != null; } catch (Exception) { }
            if (_prevIntro && !intro) { _introEndAt = now; _lastPauseEnd = now; StartRound(now, false); FillMissingRoles(); }
            if (!_prevMeeting && meeting) { _meetingCount++; _meetingStartAt = now; }
            if (_prevMeeting && !meeting) _lastPauseEnd = now;
            if (!_prevExile && exile) _exileStartAt = now;
            if (_prevExile && !exile) { _lastPauseEnd = now; _lastExileEndAt = now; StartRound(now, true); }
            _prevIntro = intro; _prevMeeting = meeting; _prevExile = exile;

            if (Pending.Count > 0)
            {
                List<int> done = null;
                foreach (var kv in Pending)
                {
                    if (now - kv.Value.At < 3f) continue;
                    (done ??= new List<int>()).Add(kv.Key);
                    // re-check 3 s later: still alive and connected, no meeting in between (a death / meeting message that arrived after the chat)
                    if (!Active() || _meetingCount != kv.Value.Meetings) continue;
                    var pc = Core.Game.Player(kv.Value.PlayerId);
                    if (pc == null || pc.OwnerId != kv.Key || !Suspectable(pc) || pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId)) continue;
                    Report(Rule.ChatAlive, pc, "SendChat outside a meeting (re-checked 3 s later)", false, false);
                }
                if (done != null) foreach (var k in done) Pending.Remove(k);
            }
            if (Reports.Count > 0)
            {
                for (int i = Reports.Count - 1; i >= 0; i--)
                {
                    var r = Reports[i];
                    if (now - r.At < 0.7f) continue;   // a kill that raced the report has landed by now
                    Reports.RemoveAt(i);
                    if (!HostWatching() || !Registration.CompatMode) continue;
                    var pc = Core.Game.Player(r.Reporter);
                    if (pc == null || pc.OwnerId != r.Owner || !Suspectable(pc)) continue;
                    if (r.ReporterDead) { Report(Rule.ReportForge, pc, "report / emergency while dead", false, false); continue; }
                    if (r.Target == 255) continue;
                    var t = Core.Game.Player(r.Target);
                    if (t != null && t.Data != null && !t.Data.Disconnected && !t.Data.IsDead)
                        Report(Rule.ReportForge, pc, $"reported the body of living #{r.Target} {Core.Game.NameOf(r.Target)}", false, false);
                }
            }
            RunKicks();
            if (SpoilerBacklog.Count > 0 && SpoilerFlushReady(started, now))
            {
                foreach (var line in SpoilerBacklog) Notice(line);
                SpoilerBacklog.Clear();
            }
            if (NoticeBacklog.Count > 0 && now - _lastNoticeAt >= NoticeSpacing)
            {
                _lastNoticeAt = now;
                Chat.Chat.Local(Chat.Chat.Title, NoticeBacklog.Dequeue());
            }
            if (PublicBacklog.Count > 0 && !started && now - _gameEndAt > 4f)
            {
                bool inLobby = false;
                try { inLobby = LobbyBehaviour.Instance != null; } catch (Exception) { }
                if (inLobby)
                {
                    foreach (var line in PublicBacklog) Chat.Chat.All(Chat.Chat.Title, line);
                    PublicBacklog.Clear();
                }
            }
        }

        /// <summary>
        /// Held callout notices: during the game once the host is dead (or not a living crewmate); after the game only
        /// back in the lobby with a chat to show them in (the end-of-game fade destroys the ship chat — review 2026-09-21).
        /// </summary>
        private static bool SpoilerFlushReady(bool started, float now)
        {
            if (started) return !CalloutWatch.HoldNow();
            if (now - _gameEndAt <= 4f) return false;
            try { return LobbyBehaviour.Instance != null && PlayerControl.LocalPlayer != null; }
            catch (Exception) { return false; }
        }

        private static void StartRound(float now, bool afterExile)
        {
            _roundStartAt = now;
            _roundStartIsExile = afterExile;
            TaskTimes.Clear();
            TaskBurstThisRound.Clear();
        }

        private static void ResetGame(float now)
        {
            _introEndAt = -1f; _lastPauseEnd = now; _roundStartAt = -1f; _roundStartIsExile = false; _lastExileEndAt = -100f;
            _meetingStartAt = -100f; _exileStartAt = -100f;
            _prevIntro = _prevMeeting = _prevExile = false;
            LastKillAt.Clear();
            DeathAt.Clear();
            TaskTimes.Clear();
            TaskBurstThisRound.Clear();
            LastMoveRpcAt.Clear();
            Pending.Clear();
            Reports.Clear();
            CalloutWatch.OnGameStart();
            foreach (var s in Suspects.Values)
            {
                s.NoticedThisGame.Clear();
                s.Events.Clear();        // Repeat rules count within one game only
                s.LastEventAt.Clear();
            }
        }

        internal static void OnLobbyJoined()
        {
            bool same = false;
            try { same = Core.Game.JoinedSameLobby(); } catch (Exception) { }
            if (same) return;   // "play again": the records of this lobby are kept
            Suspects.Clear();
            UnknownRpcLogged.Clear();
            KickQueue.Clear();
            NoticeBacklog.Clear();
            PublicBacklog.Clear();
            SpoilerBacklog.Clear();
            Pending.Clear();
            Reports.Clear();
            CalloutWatch.OnLobbyChanged();
        }

        internal static void OnPlayerLeft(int clientId)
        {
            Pending.Remove(clientId);
            if (Suspects.TryGetValue(clientId, out var s)) s.Left = true;
        }

        // ------------------------------------------------------------------ /ac

        internal static string Command(string[] tokens)
        {
            string a = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "";
            switch (a)
            {
                case "": case "list": case "一覧": return ListText();
                case "clear":
                    Suspects.Clear(); UnknownRpcLogged.Clear(); CalloutWatch.ClearTallies();
                    return Lang.T("ac.cleared", "チート検知の記録を消しました。", "Anti-cheat records cleared.", "已清除作弊检测记录。");
                case "on": case "off":
                    Options.CheatDetect = a == "on";
                    return "anticheat = " + a;
                case "kick":
                    if (tokens.Length > 2 && (tokens[2] == "on" || tokens[2] == "off")) { Options.CheatAutoKick = tokens[2] == "on"; return "anticheat.kick = " + tokens[2]; }
                    return "anticheat.kick = " + (Options.CheatAutoKick ? "on" : "off");
                case "test":
                    return TestCommand(tokens);
            }
            return Usage();
        }

        private static string Usage() => Lang.T("ac.usage",
            "使い方: /ac（記録の一覧）, /ac clear, /ac on|off, /ac kick on|off, /ac test <kill|vent|ability|task|chat|sabotage|killcd|protect|distance|rpc|taskburst|report|teleport|killphase|callout> <#番号|名前> [kick]",
            "Usage: /ac (records), /ac clear, /ac on|off, /ac kick on|off, /ac test <kill|vent|ability|task|chat|sabotage|killcd|protect|distance|rpc|taskburst|report|teleport|killphase|callout> <#id|name> [kick]",
            "用法: /ac（记录）, /ac clear, /ac on|off, /ac kick on|off, /ac test <kill|vent|ability|task|chat|sabotage|killcd|protect|distance|rpc|taskburst|report|teleport|killphase|callout> <#编号|名字> [kick]");

        private static string TestCommand(string[] tokens)
        {
            if (tokens.Length < 4) return Usage();
            Rule rule;
            switch (tokens[2].ToLowerInvariant())
            {
                case "kill": rule = Rule.KillRole; break;
                case "vent": rule = Rule.VentRole; break;
                case "ability": rule = Rule.AbilityRole; break;
                case "task": rule = Rule.TaskImpostor; break;
                case "chat": rule = Rule.ChatAlive; break;
                case "sabotage": rule = Rule.SabotageCrew; break;
                case "killcd": rule = Rule.KillCooldown; break;
                case "protect": rule = Rule.ProtectAlive; break;
                case "distance": rule = Rule.KillDistance; break;
                case "rpc": rule = Rule.RpcUnknown; break;
                case "taskburst": rule = Rule.TaskBurst; break;
                case "report": rule = Rule.ReportForge; break;
                case "teleport": rule = Rule.Teleport; break;
                case "killphase": rule = Rule.KillPhase; break;
                case "callout": rule = Rule.Callout; break;
                default: return Usage();
            }
            bool kick = tokens[tokens.Length - 1].ToLowerInvariant() == "kick";
            var sb = new StringBuilder();
            for (int i = 3; i < tokens.Length - (kick ? 1 : 0); i++) { if (sb.Length > 0) sb.Append(' '); sb.Append(tokens[i]); }
            var pc = Permissions.FindPlayer(sb.ToString());
            if (pc == null) return Lang.T("ac.test.noplayer", "その参加者が見つかりません（#番号か名前の一部）。", "No such player (#id or part of the name).", "找不到该玩家（#编号或名字的一部分）。");
            if (pc.AmOwner) return Lang.T("ac.test.self", "ホスト自身は対象にできません。", "The host cannot be tested.", "不能对主持测试。");
            bool queued = Report(rule, pc, "simulated by /ac test", true, kick);
            string outcome = queued ? "kick"
                : kick ? (Options.CheatAutoKick ? "no kick (already removed, queued or VIP+)" : "no kick: anticheat.kick off")
                : "notice only";
            return string.Format(Lang.T("ac.test.done", "[テスト] {0} に「{1}」を入力しました（{2}）", "[test] fed '{1}' for {0} ({2})", "[测试] 已对 {0} 输入“{1}”（{2}）"),
                Core.Game.NameOf(pc.PlayerId), Text(rule), outcome);
        }

        private static string ListText()
        {
            var sb = new StringBuilder(Lang.T("ac.list.header", "チート検知（この部屋）:", "Anti-cheat (this lobby):", "作弊检测（本房间）:"));
            int rows = 0;
            bool hideCallouts = CalloutWatch.HoldNow();   // a callout names impostors: a spoiler for a living-crewmate host
            foreach (var kv in Suspects)
            {
                var s = kv.Value;
                bool any = false;
                foreach (var h in s.Hits) if (!hideCallouts || (h.Key != Rule.Callout && h.Key != Rule.CalloutRepeat)) { any = true; break; }
                if (!any) continue;
                rows++;
                sb.Append('\n').Append(s.Name).Append(" #").Append(s.PlayerId).Append(": ");
                bool first = true;
                foreach (var h in s.Hits)
                {
                    if (hideCallouts && (h.Key == Rule.Callout || h.Key == Rule.CalloutRepeat)) continue;
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(Text(h.Key)).Append('×').Append(h.Value);
                }
                if (s.Kicked) sb.Append(Lang.T("ac.list.kicked", " (退出済み)", " (removed)", " (已移出)"));
                else if (s.Exempt) sb.Append(Lang.T("ac.list.exempt", " (VIP以上のため退出させず)", " (VIP+: not removed)", " (VIP以上，未移出)"));
                else if (s.Left) sb.Append(Lang.T("ac.list.left", " (退出)", " (left)", " (已离开)"));
            }
            if (rows == 0) return Lang.T("ac.list.none", "チート検知の記録はありません。", "No anti-cheat records.", "没有作弊检测记录。");
            return sb.ToString();
        }
    }

    // ====================================================================== patches (observation only: void prefixes, vanilla always runs)

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_PlayerControlHandleRpcPatch
    {
        private static void Prefix(PlayerControl __instance, byte callId, MessageReader reader)
        {
            try { CheatDetector.OnPlayerRpc(__instance, callId, reader); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_PlayerControlHandleRpcPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_PlayerPhysicsHandleRpcPatch
    {
        private static void Prefix(PlayerPhysics __instance, byte callId)
        {
            try { CheatDetector.OnPhysicsRpc(__instance, callId); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_PlayerPhysicsHandleRpcPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(CustomNetworkTransform), nameof(CustomNetworkTransform.HandleRpc))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_NetworkTransformHandleRpcPatch
    {
        private static void Prefix(CustomNetworkTransform __instance, byte callId)
        {
            try { if (callId == 21) CheatDetector.OnSnapTo(__instance); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_NetworkTransformHandleRpcPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(MessageReader))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_UpdateSystemPatch
    {
        private static void Prefix(SystemTypes systemType, PlayerControl player, MessageReader msgReader)
        {
            try { CheatDetector.OnUpdateSystem(systemType, player, msgReader); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_UpdateSystemPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    internal static class CheatDetector_SelectRolesPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            try { CheatDetector.OnSelectRolesBegin(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_SelectRolesPatch prefix: {e}"); }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            try { CheatDetector.OnSelectRolesEnd(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_SelectRolesPatch postfix: {e}"); }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class CheatDetector_TickPatch
    {
        private static void Postfix()
        {
            try { CheatDetector.Tick(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_TickPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class CheatDetector_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try { CheatDetector.OnLobbyJoined(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_OnGameJoinedPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    internal static class CheatDetector_OnPlayerLeftPatch
    {
        private static void Postfix(ClientData data)
        {
            try { if (data != null) CheatDetector.OnPlayerLeft(data.Id); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_OnPlayerLeftPatch: {e}"); }
        }
    }
}
