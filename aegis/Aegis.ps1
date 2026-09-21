# Aegis Anti-Cheat — PocketRoles ホスト用のトレイアプリ（MOD 本体とは別のアプリ）
#  起動: PocketRoles Launcher がランチャーを開いた時に起動します（手動なら "Aegis.cmd"）。
#  やること: 起動時のスキャン（この PC の MOD まわりを実際に確認して 1 行ずつ表示）→ トレイに常駐 → ゲーム中は
#            BepInEx\LogOutput.log を読んで、MOD の Aegis が退出させた・検知した時に Windows の通知を出す。
#  やらないこと: ほかのプロセス（Among Us を含む）のメモリやファイルを開く、ドライバー、常駐サービス、ネット接続。
#            プロセス一覧で "Among Us" があるかを見るだけです。VALORANT など他のゲームには一切触れません。
#  PowerShell 5.1 / WinForms。C# 部分は起動時にメモリ上でコンパイルします（.exe は作りません）。
param(
    [string]$GameDir = '',
    [int]$LauncherPid = 0,
    [string]$Lang = '',
    [switch]$ScanOnly,          # スキャン画面だけ出して終了（テスト用）
    [switch]$PreLaunch          # ランチャーの「mod 付きで起動」の直前: スキャンし直し、チートにつながる異常があれば終了コード 3（起動を止める）
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$stateDir = Join-Path $env:LOCALAPPDATA 'PocketRoles\Aegis'
try { [void][IO.Directory]::CreateDirectory($stateDir) } catch { }
$errLog = Join-Path $stateDir 'aegis.log'
function Write-AegisLog([string]$m) { try { Add-Content -LiteralPath $errLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' ' + $m) -Encoding UTF8 } catch { } }
# タスクバーで PocketRoles Launcher（PowerShell）と別のアプリとして並ぶように、独自の AppUserModelID
try {
    Add-Type -Namespace AegisNative -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);' -ErrorAction Stop
    [void][AegisNative.Shell]::SetCurrentProcessExplicitAppUserModelID('wakayamachannel.Aegis.AntiCheat')
} catch { }
if (-not $GameDir) {
    $GameDir = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Among Us PocketRoles'
    if (-not (Test-Path (Join-Path $GameDir 'Among Us.exe')) -and $env:LOCALAPPDATA) {
        $alt = Join-Path $env:LOCALAPPDATA 'PocketRoles\Among Us PocketRoles'
        if (Test-Path (Join-Path $alt 'Among Us.exe')) { $GameDir = $alt }
    }
}
if (-not $Lang) {
    $ui = [Globalization.CultureInfo]::CurrentUICulture.Name
    $Lang = if ($ui -like 'ja*') { 'ja' } elseif ($ui -like 'zh*') { 'zh-CN' } else { 'en' }
}

$source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AegisApp
{
    // ------------------------------------------------------------------ strings (ja / zh-CN / en)
    public static class S
    {
        public static string Lang = "ja";
        static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            // key                ja                                                   zh-CN                                  en
            { "sub",        new[] { "PocketRoles ホスト用アンチチート", "PocketRoles 主持用反作弊", "Anti-cheat for PocketRoles hosts" } },
            { "engine",     new[] { "Aegis エンジン", "Aegis 引擎", "Aegis engine" } },
            { "engine.ok",  new[] { "検知ルール {0} 件を準備", "已加载 {0} 条检测规则", "{0} detection rules ready" } },
            { "game",       new[] { "Among Us", "Among Us", "Among Us" } },
            { "game.ok",    new[] { "{0}（対応版）", "{0}（支持的版本）", "{0} (supported)" } },
            { "game.other", new[] { "{0}（対応版は {1}）", "{0}（支持的版本为 {1}）", "{0} (supported: {1})" } },
            { "game.none",  new[] { "MOD 用のゲームフォルダが見つかりません", "找不到 MOD 用的游戏文件夹", "Modded game folder not found" } },
            { "game.nover", new[] { "Among Us.exe を確認", "已确认 Among Us.exe", "Among Us.exe found" } },
            { "bep",        new[] { "BepInEx", "BepInEx", "BepInEx" } },
            { "bep.ok",     new[] { "BepInEx 6（IL2CPP）を確認", "已确认 BepInEx 6（IL2CPP）", "BepInEx 6 (IL2CPP) found" } },
            { "bep.none",   new[] { "BepInEx が見つかりません", "找不到 BepInEx", "BepInEx not found" } },
            { "mod",        new[] { "MOD 本体の整合性", "MOD 本体完整性", "Mod integrity" } },
            { "mod.same",   new[] { "v{0}・前回から変更なし", "v{0}・与上次相同", "v{0}, unchanged since last time" } },
            { "mod.first",  new[] { "v{0}・指紋を記録しました", "v{0}・已记录指纹", "v{0}, fingerprint recorded" } },
            { "mod.update", new[] { "v{0}・更新を確認（前回 v{1}）", "v{0}・已更新（上次 v{1}）", "v{0}, updated (was v{1})" } },
            { "mod.changed",new[] { "v{0}・前回から中身が変わっています", "v{0}・内容与上次不同", "v{0}, contents changed since last time" } },
            { "mod.none",   new[] { "PocketRoles.dll が見つかりません", "找不到 PocketRoles.dll", "PocketRoles.dll not found" } },
            { "plug",       new[] { "ほかのプラグイン", "其他插件", "Other plugins" } },
            { "plug.ok",    new[] { "なし（PocketRoles だけ）", "无（只有 PocketRoles）", "none (PocketRoles only)" } },
            { "plug.warn",  new[] { "見知らぬプラグイン: {0}", "未知插件: {0}", "unknown plugin(s): {0}" } },
            { "cfg",        new[] { "Aegis の設定", "Aegis 设置", "Aegis settings" } },
            { "cfg.ok",     new[] { "検知 {0}・自動退出 {1}・お知らせ {2}・言い当て {3}", "检测 {0}・自动移出 {1}・公告 {2}・点中提示 {3}", "detect {0} · auto-remove {1} · announce {2} · callout {3}" } },
            { "cfg.off",    new[] { "検知がオフです（/opt anticheat on）", "检测已关闭（/opt anticheat on）", "detection is off (/opt anticheat on)" } },
            { "cfg.none",   new[] { "設定はまだありません（初回起動で作られます）", "尚无设置（首次启动时生成）", "no settings yet (created on first run)" } },
            { "ban",        new[] { "BAN リスト", "封禁名单", "Ban list" } },
            { "ban.ok",     new[] { "{0} 人", "{0} 人", "{0} player(s)" } },
            { "inj",        new[] { "ゲームへの注入", "游戏注入", "Game injection" } },
            { "inj.ok",     new[] { "不審な DLL なし", "没有可疑的 DLL", "no suspicious DLL" } },
            { "inj.warn",   new[] { "ゲームフォルダに不審な DLL: {0}", "游戏文件夹中有可疑 DLL: {0}", "suspicious DLL in the game folder: {0}" } },
            { "sb",         new[] { "セキュアブート", "安全启动", "Secure Boot" } },
            { "sb.on",      new[] { "有効", "已启用", "on" } },
            { "sb.off",     new[] { "無効（UEFI の設定でオンにできます）", "未启用（可在 UEFI 设置中开启）", "off (can be enabled in UEFI settings)" } },
            { "sb.unknown", new[] { "確認できません（レガシー BIOS）", "无法确认（传统 BIOS）", "unknown (legacy BIOS)" } },
            { "tpm",        new[] { "TPM", "TPM", "TPM" } },
            { "tpm.ok",     new[] { "TPM 2.0 を確認", "已确认 TPM 2.0", "TPM 2.0 found" } },
            { "tpm.none",   new[] { "TPM 2.0 が見つかりません", "找不到 TPM 2.0", "no TPM 2.0 found" } },
            { "kern",       new[] { "カーネルの保護", "内核保护", "Kernel protection" } },
            { "kern.ok",    new[] { "テスト署名・デバッグモードなし{0}", "无测试签名・调试模式{0}", "no test-signing / debug mode{0}" } },
            { "kern.hvci",  new[] { "・メモリ整合性 ON", "・内存完整性 开", " · memory integrity on" } },
            { "kern.warn",  new[] { "{0} が有効（チート用ドライバーを読み込める状態）", "{0} 已启用（可加载作弊驱动的状态）", "{0} enabled (cheat drivers could load)" } },
            { "tools",      new[] { "実行中のチートツール", "运行中的作弊工具", "Running cheat tools" } },
            { "tools.ok",   new[] { "なし", "无", "none" } },
            { "tools.warn", new[] { "{0}（チートに使えるツールが起動中）", "{0}（可用于作弊的工具正在运行）", "{0} (a tool usable for cheating is running)" } },
            { "on",         new[] { "ON", "开", "on" } },
            { "off",        new[] { "OFF", "关", "off" } },
            { "scanning",   new[] { "スキャン中… {0}/{1}", "扫描中… {0}/{1}", "Scanning… {0}/{1}" } },
            { "done",       new[] { "スキャン完了 — 保護中", "扫描完成 — 保护中", "Scan complete — protected" } },
            { "done.warn",  new[] { "スキャン完了 — 注意 {0} 件", "扫描完成 — 注意 {0} 项", "Scan complete — {0} warning(s)" } },
            { "go",         new[] { "スキャン完了 — 起動します", "扫描完成 — 正在启动", "Scan complete — starting" } },
            { "blocked",    new[] { "起動を止めました — チートにつながる異常 {0} 件（赤い項目）", "已阻止启动 — {0} 项与作弊相关的异常（红色项目）", "Start blocked — {0} cheat-related problem(s) (red rows)" } },
            { "tip.wait",   new[] { "Aegis — 待機中", "Aegis — 待机中", "Aegis — standing by" } },
            { "tip.watch",  new[] { "Aegis — 監視中 · 検知 {0} · 退出 {1}", "Aegis — 监视中 · 检测 {0} · 移出 {1}", "Aegis — watching · {0} flagged · {1} removed" } },
            { "b.ready",    new[] { "起動しました。ゲームを始めると監視します。", "已启动。开始游戏后将进行监视。", "Ready. Watching starts when the game runs." } },
            { "b.watch",    new[] { "監視を始めました（PocketRoles {0}）", "开始监视（PocketRoles {0}）", "Watching (PocketRoles {0})" } },
            { "b.watch0",   new[] { "監視を始めました", "开始监视", "Watching" } },
            { "b.stop",     new[] { "ゲームが終わりました。待機中です。", "游戏已结束。待机中。", "The game closed. Standing by." } },
            { "b.removed",  new[] { "{0} を退出させました（{1}）", "已移出 {0}（{1}）", "Removed {0} ({1})" } },
            { "b.flag",     new[] { "{0}: {1}", "{0}: {1}", "{0}: {1}" } },
            { "test",       new[] { "[テスト] ", "[测试] ", "[test] " } },
            { "m.open",     new[] { "Aegis の状態", "Aegis 状态", "Aegis status" } },
            { "m.scan",     new[] { "もう一度スキャン", "重新扫描", "Scan again" } },
            { "m.quit",     new[] { "終了", "退出", "Quit" } },
            { "st.title",   new[] { "Aegis の状態", "Aegis 状态", "Aegis status" } },
            { "st.wait",    new[] { "待機中（ゲームは起動していません）", "待机中（游戏未运行）", "Standing by (the game is not running)" } },
            { "st.watch",   new[] { "監視中 · 検知 {0} 件 · 退出 {1} 人", "监视中 · 检测 {0} 次 · 移出 {1} 人", "Watching · {0} flagged · {1} removed" } },
            { "st.none",    new[] { "この起動中の記録はまだありません。", "本次启动尚无记录。", "No records in this session yet." } },
            { "st.about",   new[] { "Aegis は PocketRoles のホスト用アンチチートです。検知と退出はゲーム内の MOD が行い、このアプリはその状態と通知を表示します。この PC のファイルとプロセス一覧を見るだけで、ほかのゲームやアプリには触れません。",
                                    "Aegis 是 PocketRoles 的主持用反作弊。检测和移出由游戏内的 MOD 执行，本应用只显示其状态和通知。它只查看本机文件和进程列表，不会接触其他游戏或应用。",
                                    "Aegis is the PocketRoles host anti-cheat. The in-game mod detects and removes; this app shows its state and notifications. It only reads files on this PC and the process list, and never touches other games or apps." } },
            { "st.close",   new[] { "閉じる", "关闭", "Close" } },
            // rule texts (the mod's CheatDetector.Rule names)
            { "r.KillRole",     new[] { "キルできない役職のキル", "不能击杀的职业击杀", "kill without a killing role" } },
            { "r.VentRole",     new[] { "ベントを使えない役職のベント", "不能用通风管却用了", "vent without a venting role" } },
            { "r.AbilityRole",  new[] { "持っていない能力の使用", "使用了没有的能力", "ability the role does not have" } },
            { "r.TaskImpostor", new[] { "インポスターのタスク完了", "内鬼完成任务", "task done as an impostor" } },
            { "r.ChatAlive",    new[] { "生存中の会議外チャット", "存活时会议外聊天", "alive chat outside a meeting" } },
            { "r.KillDead",     new[] { "死んでいるのにキル", "死亡后击杀", "kill while dead" } },
            { "r.SabotageCrew", new[] { "クルーのサボタージュ", "船员发动破坏", "sabotage as a crewmate" } },
            { "r.KillCooldown", new[] { "クールダウンより早いキル", "快于冷却的击杀", "kill faster than the cooldown" } },
            { "r.ProtectAlive", new[] { "生存中の守護", "存活时守护", "protect while alive" } },
            { "r.TaskUnknown",  new[] { "持っていないタスクの完了", "完成了没有的任务", "task they do not have" } },
            { "r.KillDistance", new[] { "遠すぎるキル", "过远的击杀", "kill from too far" } },
            { "r.RpcUnknown",   new[] { "普通にない通信", "原版没有的通信", "non-vanilla message" } },
            { "r.TaskBurst",    new[] { "ありえない速さのタスク", "不可能的任务速度", "impossibly fast tasks" } },
            { "r.ReportForge",  new[] { "ありえない通報", "不可能的举报", "impossible report" } },
            { "r.Teleport",     new[] { "瞬間移動", "瞬间移动", "teleport" } },
            { "r.KillPhase",    new[] { "会議中・追放画面のキル", "会议或放逐画面中击杀", "kill during a meeting" } },
        };
        static int Idx { get { return Lang == "ja" ? 0 : (Lang == "zh-CN" || Lang == "zh") ? 1 : 2; } }
        public static string Get(string key, params object[] args)
        {
            string[] v;
            string s = T.TryGetValue(key, out v) ? v[Idx] : key;
            return args != null && args.Length > 0 ? string.Format(s, args) : s;
        }
        public static string Rule(string name)
        {
            string[] v;
            return T.TryGetValue("r." + name, out v) ? v[Idx] : name;
        }
        public static readonly int RuleCount = 18;   // CheatDetector: 16 rules + 2 callout rules (v0.5.3)
    }

    // ------------------------------------------------------------------ art
    public static class Art
    {
        public static readonly Color Teal1 = Color.FromArgb(38, 198, 218), Teal2 = Color.FromArgb(10, 92, 122);
        public static readonly Color Green1 = Color.FromArgb(52, 211, 140), Green2 = Color.FromArgb(10, 110, 72);
        public static readonly Color Amber1 = Color.FromArgb(255, 186, 48), Amber2 = Color.FromArgb(170, 96, 0);
        public static readonly Color Red1 = Color.FromArgb(255, 88, 88), Red2 = Color.FromArgb(150, 20, 30);

        public static GraphicsPath ShieldPath(RectangleF r)
        {
            float w = r.Width, h = r.Height, x = r.X, y = r.Y;
            float l = x + w * 0.12f, rt = x + w * 0.88f, t = y + h * 0.06f, mid = x + w * 0.5f, b = y + h * 0.96f;
            var p = new GraphicsPath();
            p.AddLine(mid, t, rt, t + h * 0.13f);
            p.AddBezier(rt, t + h * 0.13f, rt, y + h * 0.56f, rt - w * 0.06f, y + h * 0.74f, mid, b);
            p.AddBezier(mid, b, l + w * 0.06f, y + h * 0.74f, l, y + h * 0.56f, l, t + h * 0.13f);
            p.CloseFigure();
            return p;
        }

        public static void DrawShield(Graphics g, RectangleF r, Color c1, Color c2)
        {
            using (var path = ShieldPath(r))
            {
                using (var br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1.2f, r.Width / 22f))) g.DrawPath(pen, path);
            }
            // inner chevron "A" mark
            float cx = r.X + r.Width / 2f, top = r.Y + r.Height * 0.26f, bot = r.Y + r.Height * 0.70f, half = r.Width * 0.20f;
            using (var pen = new Pen(Color.White, Math.Max(1.6f, r.Width / 11f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new[] { new PointF(cx - half, bot), new PointF(cx, top), new PointF(cx + half, bot) });
                g.DrawLine(pen, cx - half * 0.52f, r.Y + r.Height * 0.54f, cx + half * 0.52f, r.Y + r.Height * 0.54f);
            }
        }

        public static Icon ShieldIcon(int size, Color c1, Color c2)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    DrawShield(g, new RectangleF(0.5f, 0.5f, size - 1f, size - 1f), c1, c2);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static Font UiFont(float size, FontStyle style)
        {
            string lang = S.Lang;
            string[] names = lang == "ja" ? new[] { "Yu Gothic UI", "Meiryo UI", "Segoe UI" }
                           : (lang == "zh-CN" || lang == "zh") ? new[] { "Microsoft YaHei UI", "Segoe UI" }
                           : new[] { "Segoe UI" };
            foreach (var n in names)
            {
                try { var f = new Font(n, size, style, GraphicsUnit.Point); if (f.Name == n) return f; f.Dispose(); } catch (Exception) { }
            }
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }
    }

    // ------------------------------------------------------------------ scan (real checks of this PC's mod setup)
    public class Check
    {
        public string Title = "";
        public string Detail = "";
        public int State;   // 0 waiting, 1 running, 2 ok, 3 warning, 4 serious (stops a launch)
        public bool SeriousOnFail;   // cheat-related: an unknown plugin / injected DLL / cheat tool / test-signing / a changed mod
        public Func<Check, bool> Run;
    }

    public static class Scanner
    {
        public const string SupportedGame = "2026.8.18";

        public static List<Check> Build(string gameDir, string stateDir)
        {
            var list = new List<Check>();
            string bep = Path.Combine(gameDir, "BepInEx");
            list.Add(new Check { Title = S.Get("engine"), Run = c => { c.Detail = S.Get("engine.ok", S.RuleCount); return true; } });
            list.Add(new Check { Title = S.Get("game"), Run = c =>
            {
                if (!File.Exists(Path.Combine(gameDir, "Among Us.exe"))) { c.Detail = S.Get("game.none"); return false; }
                string v = GameVersion(gameDir);
                if (v == null) { c.Detail = S.Get("game.nover"); return true; }
                if (v == SupportedGame) { c.Detail = S.Get("game.ok", v); return true; }
                c.Detail = S.Get("game.other", v, SupportedGame); return false;
            } });
            list.Add(new Check { Title = S.Get("bep"), Run = c =>
            {
                bool ok = File.Exists(Path.Combine(bep, "core", "BepInEx.Core.dll")) && File.Exists(Path.Combine(gameDir, "winhttp.dll"));
                c.Detail = S.Get(ok ? "bep.ok" : "bep.none"); return ok;
            } });
            list.Add(new Check { Title = S.Get("mod"), SeriousOnFail = true, Run = c => ModIntegrity(c, Path.Combine(bep, "plugins", "PocketRoles.dll"), stateDir) });
            list.Add(new Check { Title = S.Get("plug"), SeriousOnFail = true, Run = c =>
            {
                var others = new List<string>();
                string dir = Path.Combine(bep, "plugins");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories))
                        if (!string.Equals(Path.GetFileName(f), "PocketRoles.dll", StringComparison.OrdinalIgnoreCase)) others.Add(Path.GetFileName(f));
                if (others.Count == 0) { c.Detail = S.Get("plug.ok"); return true; }
                c.Detail = S.Get("plug.warn", string.Join(", ", others.ToArray())); return false;
            } });
            list.Add(new Check { Title = S.Get("inj"), SeriousOnFail = true, Run = c => Injection(c, gameDir) });
            list.Add(new Check { Title = S.Get("cfg"), Run = c =>
            {
                string cfg = Path.Combine(bep, "config", "jp.pocketroles.mod.cfg");
                if (!File.Exists(cfg)) { c.Detail = S.Get("cfg.none"); return true; }
                var v = ReadSection(cfg, "AntiCheat");
                Func<string, string> onoff = k => { string x; return (!v.TryGetValue(k, out x) || x.Trim().ToLowerInvariant() != "false") ? S.Get("on") : S.Get("off"); };
                string detect = onoff("Detect");
                if (detect == S.Get("off")) { c.Detail = S.Get("cfg.off"); return false; }
                c.Detail = S.Get("cfg.ok", detect, onoff("AutoKick"), onoff("AnnounceKick"), onoff("Callout")); return true;
            } });
            list.Add(new Check { Title = S.Get("ban"), Run = c =>
            {
                int n = 0;
                string f = Path.Combine(bep, "PocketRoles", "Banlist.txt");
                if (File.Exists(f))
                    foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        // the mod's rules: a trailing "// comment" is dropped; ";" / "#" start a comment line
                        string t = line;
                        int cm = t.IndexOf("//", StringComparison.Ordinal);
                        if (cm >= 0) t = t.Substring(0, cm);
                        t = t.Trim();
                        if (t.Length > 0 && t[0] != ';' && t[0] != '#') n++;
                    }
                c.Detail = S.Get("ban.ok", n); return true;
            } });
            list.Add(new Check { Title = S.Get("sb"), Run = SecureBoot });
            list.Add(new Check { Title = S.Get("tpm"), Run = Tpm });
            list.Add(new Check { Title = S.Get("kern"), SeriousOnFail = true, Run = Kernel });
            list.Add(new Check { Title = S.Get("tools"), SeriousOnFail = true, Run = CheatTools });
            return list;
        }

        // ---- this PC (read-only: registry values any user can read, the process NAME list; no handles to other processes)

        static Microsoft.Win32.RegistryKey Hklm(string path)
        {
            try { return Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(path); }
            catch (Exception) { return null; }
        }

        static bool SecureBoot(Check c)
        {
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
            {
                if (k == null) { c.Detail = S.Get("sb.unknown"); return true; }
                object v = k.GetValue("UEFISecureBootEnabled");
                bool on = v is int && (int)v == 1;
                c.Detail = S.Get(on ? "sb.on" : "sb.off"); return on;
            }
        }

        static bool Tpm(Check c)
        {
            // every TPM 2.0 (firmware or discrete) is the ACPI device MSFT0101
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Enum\ACPI\MSFT0101"))
            {
                bool ok = k != null && k.SubKeyCount > 0;
                c.Detail = S.Get(ok ? "tpm.ok" : "tpm.none"); return ok;
            }
        }

        static bool Kernel(Check c)
        {
            string opts = "";
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control")) { if (k != null) opts = (k.GetValue("SystemStartOptions") as string) ?? ""; }
            var bad = new List<string>();
            foreach (var tok in opts.ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok == "TESTSIGNING") bad.Add("TESTSIGNING");
                else if (tok == "DEBUG" || tok.StartsWith("DEBUGPORT")) { if (!bad.Contains("DEBUG")) bad.Add("DEBUG"); }
                else if (tok == "DISABLE_INTEGRITY_CHECKS") bad.Add("NOINTEGRITYCHECKS");
            }
            if (bad.Count > 0) { c.Detail = S.Get("kern.warn", string.Join(" / ", bad.ToArray())); return false; }
            bool hvci = false;
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"))
            {
                if (k != null) { object v = k.GetValue("Enabled"); hvci = v is int && (int)v == 1; }
            }
            c.Detail = S.Get("kern.ok", hvci ? S.Get("kern.hvci") : ""); return true;
        }

        // process names of well-known memory editors, trainers, injectors and Among Us cheat menus (names only)
        static readonly string[] ToolNames =
        {
            "cheatengine", "artmoney", "wemod", "extremeinjector", "xenos", "ghinjector", "squalr", "cosmos",
            "speedhack", "gameguardian", "sickomenu", "amongusmenu", "reclass",
        };

        static bool CheatTools(Check c)
        {
            var found = new List<string>();
            foreach (var p in Process.GetProcesses())
            {
                string n;
                try { n = p.ProcessName; } catch (Exception) { n = ""; }
                p.Dispose();
                string key = n.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                foreach (var t in ToolNames)
                    if (key.StartsWith(t) && !found.Contains(n)) { found.Add(n); break; }
            }
            if (found.Count == 0) { c.Detail = S.Get("tools.ok"); return true; }
            c.Detail = S.Get("tools.warn", string.Join(", ", found.ToArray())); return false;
        }

        // proxy DLLs next to Among Us.exe: BepInEx uses winhttp.dll; menus like AmongUsMenu / SickoMenu load through version.dll and the like
        static readonly string[] ProxyDlls = { "version.dll", "dxgi.dll", "d3d11.dll", "dinput8.dll", "winmm.dll", "dsound.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll", "opengl32.dll" };

        static bool Injection(Check c, string gameDir)
        {
            var found = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(gameDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    if (Array.IndexOf(ProxyDlls, n) >= 0 || n.Contains("menu") || n.Contains("cheat") || n.Contains("inject")) found.Add(Path.GetFileName(f));
                }
            }
            catch (Exception) { }
            if (found.Count == 0) { c.Detail = S.Get("inj.ok"); return true; }
            c.Detail = S.Get("inj.warn", string.Join(", ", found.ToArray())); return false;
        }

        static string GameVersion(string dir)
        {
            try
            {
                string f = Path.Combine(dir, "Among Us_Data", "globalgamemanagers");
                if (!File.Exists(f)) return null;
                string text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(f));
                foreach (Match m in Regex.Matches(text, @"20\d\d\.\d{1,2}\.\d{1,2}(?![\dfa-z])"))
                    if (!m.Value.StartsWith("2022.")) return m.Value;
            }
            catch (Exception) { }
            return null;
        }

        static bool ModIntegrity(Check c, string dll, string stateDir)
        {
            if (!File.Exists(dll)) { c.Detail = S.Get("mod.none"); c.SeriousOnFail = false; return false; }   // not installed yet: the launcher's job
            string ver = "?";
            try { var fv = FileVersionInfo.GetVersionInfo(dll); ver = (fv.ProductVersion ?? fv.FileVersion ?? "?").Split('+')[0]; } catch (Exception) { }
            string sha;
            using (var s = File.Open(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
            string state = Path.Combine(stateDir, "mod-fingerprint.txt");
            string prevSha = null, prevVer = null;
            try { if (File.Exists(state)) { var p = File.ReadAllText(state).Trim().Split('|'); if (p.Length >= 2) { prevSha = p[0]; prevVer = p[1]; } } } catch (Exception) { }
            try { File.WriteAllText(state, sha + "|" + ver); } catch (Exception) { }
            if (prevSha == null) { c.Detail = S.Get("mod.first", ver); return true; }
            if (prevSha == sha) { c.Detail = S.Get("mod.same", ver); return true; }
            if (prevVer != ver) { c.Detail = S.Get("mod.update", ver, prevVer); return true; }
            c.Detail = S.Get("mod.changed", ver); return false;
        }

        static Dictionary<string, string> ReadSection(string file, string section)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inside = false;
            foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { inside = string.Equals(line.Substring(1, line.Length - 2), section, StringComparison.OrdinalIgnoreCase); continue; }
                if (!inside || line.StartsWith("#") || line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }
    }

    // ------------------------------------------------------------------ splash (the scan screen)
    public class Splash : Form
    {
        readonly List<Check> checks;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        int step = -1, warnings, serious;
        public bool PreLaunchMode;
        public int Serious { get { return serious; } }
        double stepStartMs, nowMs, doneAtMs = -1;
        float angle, progress, shownProgress, sweep;
        bool fading;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Font fTitle, fSub, fRow, fRowBold, fStatus;
        public event EventHandler Finished;

        public Splash(List<Check> checks)
        {
            this.checks = checks;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 150 + checks.Count * 30 + 70);
            BackColor = Color.FromArgb(10, 16, 32);
            Text = "Aegis Anti-Cheat";
            ShowInTaskbar = true;
            TopMost = true;
            Icon = Art.ShieldIcon(32, Art.Teal1, Art.Teal2);
            fTitle = new Font("Segoe UI Semibold", 26f, FontStyle.Bold, GraphicsUnit.Point);
            fSub = Art.UiFont(9f, FontStyle.Regular);
            fRow = Art.UiFont(10f, FontStyle.Regular);
            fRowBold = Art.UiFont(10f, FontStyle.Bold);
            fStatus = Art.UiFont(9.5f, FontStyle.Regular);
            anim.Interval = 30;
            anim.Tick += (s, e) => Tick();
            MouseDown += (s, e) =>
            {
                if (doneAtMs >= 0) { BeginFade(); return; }
                if (e.Button == MouseButtons.Left) { NativeDrag(); }
            };
            Shown += (s, e) => anim.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int wp, int lp);
        void NativeDrag() { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }

        void Tick()
        {
            nowMs = clock.Elapsed.TotalMilliseconds;
            angle = (angle + 7f) % 360f;
            sweep += 0.018f; if (sweep > 1.35f) sweep = -0.35f;
            if (step < 0 && nowMs > 450) BeginStep(0);
            else if (step >= 0 && step < checks.Count && nowMs - stepStartMs > 300 + (step % 3) * 80)
            {
                var c = checks[step];
                bool ok;
                try { ok = c.Run(c); } catch (Exception ex) { c.Detail = ex.Message; ok = false; }
                c.State = ok ? 2 : c.SeriousOnFail ? 4 : 3;
                if (!ok) { warnings++; if (c.SeriousOnFail) serious++; }
                if (step + 1 < checks.Count) BeginStep(step + 1);
                else { step = checks.Count; progress = 1f; doneAtMs = nowMs; }
            }
            shownProgress += (progress - shownProgress) * 0.18f;
            double linger = PreLaunchMode ? (serious > 0 ? 9000 : 900) : (warnings > 0 ? 4200 : 1800);
            if (doneAtMs >= 0 && !fading && nowMs - doneAtMs > linger) BeginFade();
            if (fading)
            {
                Opacity = Math.Max(0, Opacity - 0.07);
                if (Opacity <= 0.01) { anim.Stop(); if (Finished != null) Finished(this, EventArgs.Empty); return; }
            }
            Invalidate();
        }

        void BeginStep(int i)
        {
            step = i; stepStartMs = nowMs;
            checks[i].State = 1;
            progress = (float)i / checks.Count;
        }

        void BeginFade() { fading = true; }
        public int Warnings { get { return warnings; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(9, 14, 30), Color.FromArgb(14, 28, 48), 90f)) g.FillRectangle(bg, 0, 0, W, H);
            // faint grid
            using (var gp = new Pen(Color.FromArgb(18, 38, 198, 218), 1f))
            {
                for (int x = 0; x < W; x += 24) g.DrawLine(gp, x, 0, x, H);
                for (int y = 0; y < H; y += 24) g.DrawLine(gp, 0, y, W, y);
            }
            bool done = doneAtMs >= 0;
            Color c1 = done ? (serious > 0 ? Art.Red1 : warnings > 0 ? Art.Amber1 : Art.Green1) : Art.Teal1;
            Color c2 = done ? (serious > 0 ? Art.Red2 : warnings > 0 ? Art.Amber2 : Art.Green2) : Art.Teal2;
            using (var border = new Pen(Color.FromArgb(160, c1), 1.5f)) g.DrawRectangle(border, 0.75f, 0.75f, W - 1.5f, H - 1.5f);

            // shield + rotating scan ring
            var sr = new RectangleF(34, 30, 74, 82);
            if (!done)
            {
                using (var ring = new Pen(Color.FromArgb(200, Art.Teal1), 3f))
                {
                    ring.StartCap = LineCap.Round; ring.EndCap = LineCap.Round;
                    g.DrawArc(ring, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24, angle, 80);
                    g.DrawArc(ring, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24, angle + 180, 80);
                }
                using (var ring2 = new Pen(Color.FromArgb(70, Art.Teal1), 1.2f))
                    g.DrawEllipse(ring2, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24);
            }
            else
            {
                using (var glow = new Pen(Color.FromArgb(90, c1), 6f)) g.DrawEllipse(glow, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24);
            }
            Art.DrawShield(g, sr, c1, c2);

            using (var white = new SolidBrush(Color.White)) g.DrawString("AEGIS", fTitle, white, 136, 26);
            using (var teal = new SolidBrush(c1)) g.DrawString("A N T I - C H E A T", fSub, teal, 140, 74);
            using (var gray = new SolidBrush(Color.FromArgb(150, 170, 190))) g.DrawString(S.Get("sub"), fSub, gray, 140, 94);

            // check rows
            int y0 = 140;
            for (int i = 0; i < checks.Count; i++)
            {
                var c = checks[i];
                int y = y0 + i * 30;
                DrawGlyph(g, c.State, new RectangleF(40, y + 3, 16, 16));
                Color tc = c.State == 0 ? Color.FromArgb(90, 110, 130) : Color.FromArgb(225, 235, 245);
                using (var tb = new SolidBrush(tc)) g.DrawString(c.Title, c.State == 1 ? fRowBold : fRow, tb, 66, y);
                if (c.State >= 2)
                {
                    Color dc = c.State == 4 ? Art.Red1 : c.State == 3 ? Art.Amber1 : Color.FromArgb(140, 200, 215);
                    using (var db = new SolidBrush(dc)) g.DrawString(c.Detail, fRow, db, new RectangleF(236, y, W - 256, 22), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
                else if (c.State == 1)
                {
                    using (var db = new SolidBrush(Color.FromArgb(120, 38, 198, 218))) g.DrawString(new string('.', 1 + (int)(nowMs / 250) % 3), fRow, db, 236, y);
                }
            }
            // sweeping scan band over the rows
            if (!done)
            {
                float bandH = 36f, top = y0 - 10 + sweep * (checks.Count * 30 + 20);
                var band = new RectangleF(20, top - bandH / 2, W - 40, bandH);
                using (var lb = new LinearGradientBrush(new RectangleF(band.X, band.Y - 1, band.Width, band.Height + 2), Color.FromArgb(0, Art.Teal1), Color.FromArgb(0, Art.Teal1), 90f))
                {
                    var blend = new ColorBlend(3);
                    blend.Colors = new[] { Color.FromArgb(0, Art.Teal1), Color.FromArgb(46, Art.Teal1), Color.FromArgb(0, Art.Teal1) };
                    blend.Positions = new[] { 0f, 0.5f, 1f };
                    lb.InterpolationColors = blend;
                    g.FillRectangle(lb, band);
                }
                using (var line = new Pen(Color.FromArgb(120, Art.Teal1), 1f)) g.DrawLine(line, 20, top, W - 20, top);
            }

            // progress bar + status
            int by = H - 52;
            var bar = new RectangleF(40, by, W - 80, 8);
            using (var bb = new SolidBrush(Color.FromArgb(28, 40, 62))) FillRound(g, bb, bar, 4);
            float pw = Math.Max(0, Math.Min(1, shownProgress)) * bar.Width;
            if (pw > 2)
                using (var pb = new LinearGradientBrush(new RectangleF(bar.X, bar.Y, bar.Width, bar.Height), c2, c1, 0f)) FillRound(g, pb, new RectangleF(bar.X, bar.Y, pw, bar.Height), 4);
            string status = !done ? S.Get("scanning", Math.Min(step + 1, checks.Count), checks.Count)
                          : serious > 0 && PreLaunchMode ? S.Get("blocked", serious)
                          : PreLaunchMode ? S.Get("go")
                          : warnings > 0 ? S.Get("done.warn", warnings) : S.Get("done");
            using (var sb = new SolidBrush(done ? c1 : Color.FromArgb(170, 190, 210))) g.DrawString(status, fStatus, sb, 38, by + 14);
            using (var vb = new SolidBrush(Color.FromArgb(90, 110, 130))) g.DrawString("Aegis 1.0", fStatus, vb, W - 110, by + 14);
        }

        void DrawGlyph(Graphics g, int state, RectangleF r)
        {
            if (state == 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(60, 80, 100))) g.FillEllipse(b, r.X + 5, r.Y + 5, 6, 6);
            }
            else if (state == 1)
            {
                using (var p = new Pen(Art.Teal1, 2.2f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, r, angle * 2f, 250); }
            }
            else if (state == 2)
            {
                using (var b = new SolidBrush(Color.FromArgb(40, Art.Green1))) g.FillEllipse(b, r);
                using (var p = new Pen(Art.Green1, 2.2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new[] { new PointF(r.X + 3.5f, r.Y + 8.5f), new PointF(r.X + 7f, r.Y + 12f), new PointF(r.X + 13f, r.Y + 4.5f) });
                }
            }
            else
            {
                using (var b = new SolidBrush(state == 4 ? Art.Red1 : Art.Amber1))
                    g.FillPolygon(b, new[] { new PointF(r.X + 8, r.Y + 1), new PointF(r.Right - 0.5f, r.Bottom - 1), new PointF(r.X + 0.5f, r.Bottom - 1) });
                using (var p = new Pen(Color.FromArgb(40, 20, 0), 1.8f)) { g.DrawLine(p, r.X + 8, r.Y + 5.5f, r.X + 8, r.Y + 10.5f); g.DrawLine(p, r.X + 8, r.Y + 12.5f, r.X + 8, r.Y + 13.2f); }
            }
        }

        static void FillRound(Graphics g, Brush b, RectangleF r, float rad)
        {
            if (r.Width < rad * 2) { g.FillRectangle(b, r); return; }
            using (var p = new GraphicsPath())
            {
                float d = rad * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure(); g.FillPath(b, p);
            }
        }
    }

    // ------------------------------------------------------------------ toast (Aegis's own, never takes the focus)
    public class Toast : Form
    {
        static readonly List<Toast> Open = new List<Toast>();
        readonly string text;
        readonly Color c1, c2;
        readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly Font fTitle, fText;
        readonly double holdMs;
        bool closing;

        public static void Show(string text, Color c1, Color c2, double holdMs)
        {
            while (Open.Count >= 4) Open[0].Close();
            var t = new Toast(text, c1, c2, holdMs);
            t.Show();
        }

        Toast(string text, Color c1, Color c2, double holdMs)
        {
            this.text = text; this.c1 = c1; this.c2 = c2; this.holdMs = holdMs;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            fTitle = Art.UiFont(10f, FontStyle.Bold);
            fText = Art.UiFont(9.5f, FontStyle.Regular);
            Size = new Size(400, 78);
            BackColor = Color.FromArgb(12, 20, 36);
            Opacity = 0;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 18, wa.Bottom - Height - 18 - Open.Count * (Height + 10));
            Open.Add(this);
            FormClosed += (s, e) => { Open.Remove(this); t.Stop(); };
            MouseClick += (s, e) => closing = true;
            t.Interval = 30;
            t.Tick += (s, e) =>
            {
                double ms = sw.Elapsed.TotalMilliseconds;
                if (!closing && ms > 250 + holdMs) closing = true;
                if (closing) { Opacity = Math.Max(0, Opacity - 0.08); if (Opacity <= 0.01) Close(); }
                else Opacity = Math.Min(0.96, ms / 250.0);
            };
            t.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008;   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(12, 20, 38), Color.FromArgb(16, 30, 52), 90f)) g.FillRectangle(bg, 0, 0, W, H);
            using (var accent = new SolidBrush(c1)) g.FillRectangle(accent, 0, 0, 4, H);
            using (var border = new Pen(Color.FromArgb(120, c1), 1f)) g.DrawRectangle(border, 0.5f, 0.5f, W - 1f, H - 1f);
            Art.DrawShield(g, new RectangleF(16, 14, 42, 48), c1, c2);
            using (var tb = new SolidBrush(Color.White)) g.DrawString("AEGIS", fTitle, tb, 70, 10);
            using (var xb = new SolidBrush(Color.FromArgb(215, 228, 240)))
                g.DrawString(text, fText, xb, new RectangleF(70, 32, W - 84, H - 38), new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
        }
    }

    // ------------------------------------------------------------------ tray app
    public class Tray : ApplicationContext
    {
        readonly string gameDir, stateDir;
        readonly int launcherPid;
        readonly bool scanOnly;
        NotifyIcon icon;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        Icon iWait, iWatch, iAlert;
        bool watching, everWatched;
        int flagged, removed;
        double alertUntil;
        DateTime lastBalloon = DateTime.MinValue;
        long logPos = -1, logBaseline = -1;
        bool firstPoll = true;
        string logRest = "", modVersion = "";
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly List<string> events = new List<string>();
        Form statusForm;
        Splash splash;
        DateTime gameGoneAt = DateTime.MinValue;

        static readonly Regex ReRemoved = new Regex(@"CheatDetector: removing #\d+ (.+?) \(client \d+\) with a room ban: (\w+)");
        static readonly Regex ReFlag = new Regex(@"CheatDetector: (\[test\] )?(\w+) \((Certain|Repeat|Notice)\) #\d+ (.+?) \(client");
        static readonly Regex ReLoaded = new Regex(@"PocketRoles v([\d.]+) loaded");

        public Tray(string gameDir, int launcherPid, string stateDir, bool scanOnly)
        {
            this.gameDir = gameDir; this.launcherPid = launcherPid; this.stateDir = stateDir; this.scanOnly = scanOnly;
            iWait = Art.ShieldIcon(32, Art.Teal1, Art.Teal2);
            iWatch = Art.ShieldIcon(32, Art.Green1, Art.Green2);
            iAlert = Art.ShieldIcon(32, Art.Amber1, Art.Amber2);
            ShowSplash();
        }

        void ShowSplash()
        {
            if (splash != null) return;
            splash = new Splash(Scanner.Build(gameDir, stateDir));
            splash.Finished += (s, e) =>
            {
                var sp = splash; splash = null;
                sp.Close(); sp.Dispose();
                if (scanOnly) { ExitThread(); return; }
                if (icon == null) StartTray();
            };
            splash.Show();
        }

        void StartTray()
        {
            icon = new NotifyIcon { Icon = iWait, Text = S.Get("tip.wait"), Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add(S.Get("m.open"), null, (s, e) => ShowStatus());
            menu.Items.Add(S.Get("m.scan"), null, (s, e) => ShowSplash());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(S.Get("m.quit"), null, (s, e) => Quit());
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += (s, e) => ShowStatus();
            if (!GameRunning()) Balloon(S.Get("b.ready"), ToolTipIcon.Info);   // a running game gets the "watching" toast instead
            poll.Interval = 1000;
            poll.Tick += (s, e) => Poll();
            poll.Start();
            Poll();
        }

        static bool GameRunning()
        {
            var ps = Process.GetProcessesByName("Among Us");
            bool any = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            return any;
        }

        static bool Alive(int pid)
        {
            if (pid <= 0) return false;
            try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; } catch (Exception) { return false; }
        }

        void Poll()
        {
            bool game = GameRunning();
            if (game && !watching)
            {
                watching = true; everWatched = true; flagged = 0; removed = 0;
                // Aegis started while the game already ran: the log on disk is this run's. Otherwise BepInEx rewrites the
                // log a moment after the game starts: wait until it shrinks below its old length, then read from 0.
                logPos = firstPoll ? 0 : -2;
                logBaseline = LogLength();
                logRest = "";
                modVersion = "";
                AddEvent(S.Get("b.watch0"));
            }
            else if (!game && watching)
            {
                watching = false;
                gameGoneAt = DateTime.Now;
                Balloon(S.Get("b.stop"), ToolTipIcon.Info);
                AddEvent(S.Get("b.stop"));
            }
            firstPoll = false;
            if (watching) ReadLog();
            UpdateIcon();
            // the launcher is closed and no game runs: done (a manual start stays until Quit, or 15 s after its game)
            if (!game)
            {
                if (launcherPid > 0 && !Alive(launcherPid)) Quit();
                else if (launcherPid <= 0 && everWatched && (DateTime.Now - gameGoneAt).TotalSeconds > 15) Quit();
            }
        }

        void ReadLog()
        {
            string path = Path.Combine(gameDir, "BepInEx", "LogOutput.log");
            try
            {
                if (!File.Exists(path)) return;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    if (logPos == -2)
                    {
                        if (logBaseline > 0 && len >= logBaseline) return;   // still the previous run's log
                        logPos = 0;
                    }
                    if (len < logPos) { logPos = 0; logRest = ""; }
                    if (len == logPos) return;
                    fs.Position = logPos;
                    var buf = new byte[Math.Min(len - logPos, 4 * 1024 * 1024)];
                    int n = fs.Read(buf, 0, buf.Length);
                    logPos += n;
                    string text = logRest + Encoding.UTF8.GetString(buf, 0, n);
                    int cut = text.LastIndexOf('\n');
                    if (cut < 0) { logRest = text; return; }
                    logRest = text.Substring(cut + 1);
                    foreach (var line in text.Substring(0, cut).Split('\n')) Line(line);
                }
            }
            catch (Exception) { }
        }

        long LogLength()
        {
            try { var fi = new FileInfo(Path.Combine(gameDir, "BepInEx", "LogOutput.log")); return fi.Exists ? fi.Length : 0; }
            catch (Exception) { return 0; }
        }

        void Line(string line)
        {
            Match m;
            if ((m = ReLoaded.Match(line)).Success)
            {
                modVersion = m.Groups[1].Value;
                Balloon(S.Get("b.watch", "v" + modVersion), ToolTipIcon.Info);
                return;
            }
            if ((m = ReRemoved.Match(line)).Success)
            {
                removed++;
                alertUntil = clock.Elapsed.TotalSeconds + 12;
                string msg = S.Get("b.removed", m.Groups[1].Value.Trim(), S.Rule(m.Groups[2].Value));
                AddEvent(msg);
                Balloon(msg, ToolTipIcon.Warning, true);
                return;
            }
            if ((m = ReFlag.Match(line)).Success)
            {
                string rule = m.Groups[2].Value;
                if (rule == "Callout" || rule == "CalloutRepeat") return;   // names impostors: never shown outside the game
                bool test = m.Groups[1].Success;
                if (!test) flagged++;
                string msg = (test ? S.Get("test") : "") + S.Get("b.flag", m.Groups[4].Value.Trim(), S.Rule(rule));
                AddEvent(msg);
                if (m.Groups[3].Value != "Notice" || test) Balloon(msg, ToolTipIcon.Warning);
            }
        }

        void AddEvent(string text)
        {
            events.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            try { File.AppendAllText(Path.Combine(stateDir, "events.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8); } catch (Exception) { }
            if (events.Count > 200) events.RemoveAt(0);
            if (statusForm != null && !statusForm.IsDisposed) FillStatus();
        }

        void Balloon(string text, ToolTipIcon kind, bool force = false)
        {
            // Aegis's own toast: Windows puts balloon tips away while a game runs (Do Not Disturb when playing a game)
            if (!force && (DateTime.Now - lastBalloon).TotalSeconds < 4) return;
            lastBalloon = DateTime.Now;
            if (kind == ToolTipIcon.Warning) Toast.Show(text, Art.Amber1, Art.Amber2, 6000);
            else Toast.Show(text, Art.Green1, Art.Green2, 3500);
        }

        void UpdateIcon()
        {
            if (icon == null) return;
            bool alert = clock.Elapsed.TotalSeconds < alertUntil;
            var want = alert ? iAlert : watching ? iWatch : iWait;
            if (icon.Icon != want) icon.Icon = want;
            string tip = watching ? S.Get("tip.watch", flagged, removed) : S.Get("tip.wait");
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (icon.Text != tip) icon.Text = tip;
        }

        ListBox statusList;
        Label statusHead;
        void ShowStatus()
        {
            if (statusForm != null && !statusForm.IsDisposed) { statusForm.Activate(); FillStatus(); return; }
            var f = new Form { Text = S.Get("st.title"), Icon = iWatch, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(560, 380), BackColor = Color.FromArgb(12, 20, 36), ForeColor = Color.FromArgb(225, 235, 245), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            statusHead = new Label { Left = 16, Top = 14, Width = 528, Height = 26, Font = Art.UiFont(11f, FontStyle.Bold), ForeColor = Art.Teal1 };
            statusList = new ListBox { Left = 16, Top = 46, Width = 528, Height = 222, BackColor = Color.FromArgb(18, 28, 48), ForeColor = Color.FromArgb(225, 235, 245), BorderStyle = BorderStyle.FixedSingle, Font = Art.UiFont(9.5f, FontStyle.Regular) };
            var about = new Label { Left = 16, Top = 276, Width = 528, Height = 60, Text = S.Get("st.about"), Font = Art.UiFont(8.5f, FontStyle.Regular), ForeColor = Color.FromArgb(150, 170, 190) };
            var close = new Button { Text = S.Get("st.close"), Left = 444, Top = 342, Width = 100, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(10, 92, 122) };
            close.Click += (s, e) => f.Close();
            f.Controls.Add(statusHead); f.Controls.Add(statusList); f.Controls.Add(about); f.Controls.Add(close);
            statusForm = f;
            FillStatus();
            f.Show();
        }

        void FillStatus()
        {
            statusHead.Text = watching ? S.Get("st.watch", flagged, removed) : S.Get("st.wait");
            statusList.BeginUpdate();
            statusList.Items.Clear();
            if (events.Count == 0) statusList.Items.Add(S.Get("st.none"));
            for (int i = events.Count - 1; i >= 0; i--) statusList.Items.Add(events[i]);
            statusList.EndUpdate();
        }

        void Quit()
        {
            poll.Stop();
            if (icon != null) { icon.Visible = false; icon.Dispose(); icon = null; }
            ExitThread();
        }
    }

    public static class Entry
    {
        /// <summary>The launcher's "start with mod": a fresh scan on screen; 3 when a cheat-related check failed (do not start), else 0.</summary>
        public static int PreLaunch(string gameDir, string lang, string stateDir)
        {
            S.Lang = string.IsNullOrEmpty(lang) ? "ja" : lang;
            Application.EnableVisualStyles();
            var splash = new Splash(Scanner.Build(gameDir, stateDir)) { PreLaunchMode = true };
            splash.Finished += (s, e) => splash.Close();
            Application.Run(splash);
            try { File.AppendAllText(Path.Combine(stateDir, "events.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  pre-launch scan: " + (splash.Serious > 0 ? "blocked (" + splash.Serious + ")" : "ok") + Environment.NewLine, Encoding.UTF8); } catch (Exception) { }
            return splash.Serious > 0 ? 3 : 0;
        }

        public static void Run(string gameDir, int launcherPid, string lang, string stateDir, bool scanOnly)
        {
            S.Lang = string.IsNullOrEmpty(lang) ? "ja" : lang;
            bool created;
            using (var mutex = new Mutex(true, "Local\\wakayamachannel.Aegis.AntiCheat", out created))
            {
                if (!created) return;   // one Aegis at a time
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Tray(gameDir, launcherPid, stateDir, scanOnly));
            }
        }
    }
}
'@

try {
    Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.Windows.Forms', 'System.Drawing', 'System.Core') -Language CSharp -ErrorAction Stop
    if ($PreLaunch) { $code = [AegisApp.Entry]::PreLaunch($GameDir, $Lang, $stateDir); exit $code }
    [AegisApp.Entry]::Run($GameDir, $LauncherPid, $Lang, $stateDir, [bool]$ScanOnly)
} catch {
    Write-AegisLog ('Aegis failed: ' + $_.Exception.ToString())
    if ($PreLaunch) { exit 0 }   # never block the game because Aegis itself failed
}
