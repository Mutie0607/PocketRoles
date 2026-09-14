# GitHub Release 本文（v0.5.1）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.5.1`、タイトル `PocketRoles v0.5.1 (Among Us 2026.8.18)`。添付は `dist\PocketRoles-Setup-0.5.1.zip`、`dist\PocketRoles-0.5.1.zip`、`dist\SHA256SUMS.txt` の 3 つ（`build-release.ps1` の出力）。ハッシュは `SHA256SUMS.txt` の値。

---

## PocketRoles v0.5.1 — インポスターの人数修正・自分の役を決める `/next`・登録オフのタスク数

**部屋を作る人だけ** が入れる役職 MOD です。参加者は PC / スマホ / Switch の **バニラのまま**、部屋コードを打つだけ。26 役職（シェリフ、メイヤー、スニッチ、ジャッカル、ジェスター、ラバーズ…）が名前タグとチャットで本人にだけ届き、案内は日本語 / 中文 / English、外国語のチャットは自動翻訳。登録オフ（便利ホスト）の部屋でも案内・翻訳・試合結果が使えます。

説明書: [日本語](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [简体中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)

### v0.5.1 の変更

- **インポスターの人数が設定より少なくなる問題を修正**: Among Us 本体（2026.8.18）は、インポスター役職（シフター・ファントム・ヴァイパー）を 100% にしていると、インポスターの枠に選んだ人へ誤って「クルー」を送ることがあります（9/14 の 14 人部屋: 3 人設定なのに 2 人だけ）。MOD が本体の役職選択を見張り、その人をそのままインポスターに戻します（参加者には最初からインポスターとして届き、人数も設定どおり）。登録オフ・登録ありの両方で有効。それでも足りない時は素のクルーから補充します。
- **次の試合の自分の役 `/next`**: `/next impostor` / `/next crew` / `/next auto` / `/next <本体の役職名>`（例 `/next shapeshifter`、`/next 探偵`）で、次の 1 試合だけ自分の役を決められます。設定タブ「ホスト」の「次の自分」ボタン（おまかせ → インポスター → クルー）でも。**テストモード不要で、登録オフの部屋でも効きます。** 仕組みは、本体の役職選択の中で希望の役が他の人に出たときに役職メッセージの宛先を自分と入れ替え、その人には自分がもらうはずだった役を渡すだけなので、参加者には普通の配役に見え、インポスターの人数も変わりません。指定すると左上に「次:自分=インポスター」と出て、試合が始まると使い切ります（部屋を出ると解除）。ゲームマスターモード中は使えません。
- **登録オフでもタスクの配布数を増やせる**: 登録オフの部屋で `/vset common 4` `/vset short 8` `/vset long 5` のように本体の範囲を超える値を指定すると、サーバーに送る設定値は本体の範囲のまま、実際に配る個数だけがその数になります（`/vset show` に「配る個数（登録オフ）」、設定タブに「配るコモン数(登録オフ)」などの行）。範囲内の値を指定し直すか、設定画面の矢印を動かすと元に戻ります。マップにある種類より多くすると同じタスクが複数回出ます（登録ありの範囲拡張と同じ本体の仕様）。
- **ランチャー**: 起動中のタスクバーのアイコンが PowerShell のものになっていたのを、PocketRoles のアイコンに。
- 試合結果の投稿は、ロビーに戻ってから最初の参加者が入るまで待ってから送ります（v0.5.0 から）。

変更履歴の詳細は `CHANGELOG.md`。

### 更新のしかた（v0.5.0 から）

- **ランチャー**: 「PocketRoles Launcher」を開いて **「更新を確認」** → 「今すぐ更新しますか？」で「はい」。`PocketRoles-0.5.1.zip` をダウンロードして配置します（`BepInEx\config\` は上書きしないので設定は残ります。新しい設定項目は初回起動時に既定値で追記されます）。ランチャー本体（タスクバーのアイコン）を新しくするには `PocketRoles-Setup-0.5.1.zip` を今のランチャーのフォルダに上書き展開してください。
- **手動**: `PocketRoles-0.5.1.zip` を MOD 用コピーの Among Us フォルダに上書き展開（`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`）。**`lang\*.json` も上書き** してください（`/next` などの文言は新しい lang にしかありません。上書きしなくても英語・日本語の内蔵文言で動きます）。
- 初めての人は下の `PocketRoles-Setup-0.5.1.zip` から。参加者側には何も要りません。

### ダウンロード

| ファイル | 誰向け | 使い方 |
|---|---|---|
| **`PocketRoles-Setup-0.5.1.zip`** | **初めての人・友達に渡す用（おすすめ）** | 下の `PocketRoles-0.5.1.zip` と一緒に同じフォルダ（例: ドキュメント\PocketRoles）に展開 → `PocketRoles Launcher.cmd` → 「インストール」→ Steam を起動して「起動」。Steam 版のコピー・BepInEx・MOD を自動で入れます（Steam 版は書き換えません） |
| `PocketRoles-0.5.1.zip` | MOD 本体（手動導入・更新用） | BepInEx 6.0.0-be.735（Unity.IL2CPP, win-x86）を入れた Among Us のコピーに上書き（`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`、README、LICENSE）。ランチャーの「更新を確認」もこの zip を取得します |
| `SHA256SUMS.txt` | 検証用 | 下のハッシュと同じ内容 |

```
SHA256
PocketRoles-Setup-0.5.1.zip  a7b3bf8ef4ab0cf99e8acc53cf2da490da7ecd0b20ff6155bdbcdfae07594ae0
PocketRoles-0.5.1.zip        af664ce9151095606adb801a49f3156593e5eba756f5e001092ba72fdf281843
```

必要なもの: Windows 10 / 11、Steam 版 Among Us **2026.8.18**、Steam クライアント起動中。参加者側には何も要りません。

### 既知の制限（v0.5.1）

- 実機（PC ホスト＋エミュレータ 2 台、2026-09-14）で確認済み: インポスターの復元（登録オフ 3 人・登録あり 3 人・100% 設定で余計な置き換えなし）、`/next impostor` / `crew` / `shapeshifter` / `detective`（登録オフ）と `/next impostor`（登録あり）、登録オフのタスク配布数（コモン 4・ショート 8 でホスト・参加者とも 14 個）、侍の複数斬り（対象＋範囲内の 1 人＋恋人の後追い）、ランチャーのタスクバーのアイコン。設定タブの「次の自分」ボタンはコード上のみで、画面での確認はまだです（チャットの `/next` は確認済み）。
- `/next crew` で本体が自分をインポスターに選んだ時は、他の人に出るクルー役職と入れ替えます（役なしの人がいればその人へ）。3 人だけの試合で全員が本体の役職を持つ場合など、入れ替え相手がいないとその回は指定どおりになりません（チャットでお知らせします）。
- `/next` は本体の役職選択に相乗りするので、指定した本体の役職がその選択で 1 人も出なかった回は「おまかせ」になります（人数・確率をその回だけ 1 / 100% に上げるので通常は出ます）。
- ゲームマスターモードでは `/next` は使えません（ゲームマスターは試合に参加しないため）。
- 登録オフのタスク配布数は `[Vanilla] ClampInUnregistered`（既定オン）の時だけ使われます。オフにした場合は設定値そのものを変える従来の方式です。
- **シェリフ・ジャッカル・放火魔・崇拝者のイントロは「インポスター」** と表示されます（本人のクライアントがインポスターとして動くため）。本当の役職はイントロ直後に名前タグとチャットで届きます。
- **家庭用機やクイックチャット限定の参加者はコマンドを打てません**（役職の通知やチャットは読めます）。
- **ホスト移譲は非対応** です。ホストが抜けるとその試合は正常に続きません。
- 登録オフ（便利ホスト）の部屋では MOD の役職は配りません（本体の役職と `/next` は使えます）。役職ありで遊ぶときは登録オン（既定）の部屋を使ってください。
- 2 人だけの試合は、会議のあと参加者の画面が黒くなります（本体の仕様。部屋に戻ると直ります）。動作確認は 3 人以上で。
- 短時間に何度も部屋を作り直す・試合の途中で退出すると、公式サーバーの ban points で部屋作成が一時制限されます（MOD に関係なく起こります）。
- 対応はクラシックモードのみ。ゲームが更新されると MOD は自動で無効になります（対応版をお待ちください）。

### 困ったら

- 質問: `pocketroles.report+help@gmail.com`（日本語・中文・English。読んで返事をします）
- 不具合: `pocketroles.report@gmail.com`（ランチャーの「報告 zip を作る」の zip を添付）/ [Issues](https://github.com/wakayamachannel/PocketRoles/issues)
- 要望: `pocketroles.report+request@gmail.com`

### 免責

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

Innersloth の Among Us Mod Policy（2026-07-30）に従い、部屋作成時に MOD 部屋登録（公式ルール・役職に必須）を自動で行います。登録した部屋は公開一覧に出ないので、Discord などで部屋コードを伝えるか、案内部屋から入ってもらいます。無料・非営利、GPL-3.0-or-later。ランチャーは署名証明書を付けていないため、初回に SmartScreen の警告が出ます（「詳細情報」→「実行」）。

---

## 简体中文（简版）

**只需房主安装** 的 Among Us 职业模组。其他玩家用 PC / 手机 / Switch 的 **原版** 输入房间代码即可加入。v0.5.1：**修正内鬼人数少于设置的问题**（原版把内鬼职业设为 100% 时会把内鬼名额的人误发成船员；模组会把该玩家恢复为内鬼，未注册与已注册房间均有效）；**`/next impostor` / `/next crew` / `/next auto` / `/next <原版职业名>`** 指定下一局自己的职业（无需测试模式，未注册房间也可；设置页“房主”的“下局的我”按钮亦可；只是交换职业消息的收件人，玩家看到的是普通分配，内鬼人数不变）；**未注册房间也可增加任务数量**（`/vset common 4` 等：同步的设置保持在原版范围内，只改变实际分发的数量）；启动器任务栏图标改为 PocketRoles 的图标。

- **更新**：打开 PocketRoles Launcher → “检查更新” → “是”。设置会保留，新设置项会以默认值补上。手动更新则把 `PocketRoles-0.5.1.zip` 覆盖解压到模组副本的目录（`lang\*.json` 也请覆盖）。
- **安装**（首次）：把 `PocketRoles-Setup-0.5.1.zip` 和 `PocketRoles-0.5.1.zip` 解压到同一个文件夹 → 双击 `PocketRoles Launcher.cmd` → “安装” → 启动 Steam 后点“启动”。需要 Windows + Steam 版 Among Us 2026.8.18。玩家什么都不用装。
- **说明书**：[README.zh-CN.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md)。提问：`pocketroles.report+help@gmail.com`，问题报告：`pocketroles.report@gmail.com`（附上启动器生成的报告 zip）。

## English (short)

An Among Us role mod that **only the host installs**. Everyone else joins with the **vanilla** game on PC, mobile or Switch by entering the room code. v0.5.1: **fixes games starting with fewer impostors than set** (vanilla 2026.8.18 sends a Crewmate to one of its impostor picks when impostor roles are at 100 %; the mod restores that player as Impostor, in registered and unregistered lobbies); **`/next impostor` / `/next crew` / `/next auto` / `/next <vanilla role>`** fixes your own role for the next game (no test mode, unregistered lobbies too; also the Host page button; it only swaps the recipients of the role messages, so players see an ordinary assignment and the impostor count is unchanged); **more tasks in unregistered lobbies** (`/vset common 4` etc.: the synced setting stays inside the vanilla range, only the number handed out changes); the launcher's taskbar icon is now the PocketRoles icon.

- **Update**: open the PocketRoles Launcher → "Check for updates" → "Yes". Your config is kept; new settings are added with their defaults. Manual update: extract `PocketRoles-0.5.1.zip` over the modded copy (overwrite `lang\*.json` too).
- **Install** (first time): extract `PocketRoles-Setup-0.5.1.zip` and `PocketRoles-0.5.1.zip` into the same folder → double-click `PocketRoles Launcher.cmd` → "Install" → start Steam and press "Launch". Needs Windows + Steam Among Us 2026.8.18. Players install nothing.
- **Manual**: [README.en.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md). Questions: `pocketroles.report+help@gmail.com`; bugs: `pocketroles.report@gmail.com` (attach the launcher's report zip).

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.
