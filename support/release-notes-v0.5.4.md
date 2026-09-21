# GitHub Release 本文（v0.5.4）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.5.4`、タイトル `PocketRoles v0.5.4 (Among Us 2026.8.18)`。添付は `dist\PocketRoles-Setup-0.5.4.zip`、`dist\PocketRoles-0.5.4.zip`、`dist\SHA256SUMS.txt` の 3 つ（`build-release.ps1` の出力）。ハッシュは `SHA256SUMS.txt` の値。

---

## PocketRoles v0.5.4 — Aegis のトレイアプリ・ゲーム内の Aegis の強化・高 PING の部屋の自動作り直し

**部屋を作る人だけ** が入れる役職 MOD です。参加者は PC / スマホ / Switch の **バニラのまま**、部屋コードを打つだけ。26 役職（シェリフ、メイヤー、スニッチ、ジャッカル、ジェスター、ラバーズ…）が名前タグとチャットで本人にだけ届き、案内は日本語 / 中文 / English、外国語のチャットは自動翻訳。登録オフ（便利ホスト）の部屋でも案内・翻訳・試合結果・アンチチートが使えます。

説明書: [日本語](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [简体中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)

### v0.5.4 の変更

- **Aegis のトレイアプリ**（MOD 本体とは別のアプリ。ランチャーを開くと起動、閉じると終了）:
  - ランチャーを開くとスキャン画面で 12 項目を確認: 検知ルール、ゲームの版、BepInEx、MOD 本体の指紋、ほかのプラグイン、注入 DLL（version.dll など）、Aegis の設定、BAN リスト、セキュアブート、TPM 2.0、テスト署名・デバッグモード、起動中のチートツール。
  - 確認が終わると Windows の通知領域に常駐（待機中は青緑の盾、ゲーム中は緑）。試合中に Aegis が退出させた・検知した時に画面の右下へ通知（ゲーム中で Windows の通知が止まっていても出ます）。
  - **起動前の検査**: 「起動」の直前にもう一度確認し、チートにつながる異常（見知らぬプラグイン・注入 DLL・起動中のチートツール・テスト署名／デバッグ・MOD 本体の改ざん）があれば起動を止めて、項目ごとに原因と直し方を出します。セキュアブートと TPM は注意だけです。
  - **定義ファイル**: チートツール名の一覧（`aegis/definitions.txt`）をランチャーを開くたびに GitHub から取って、次のスキャンから使います。Among Us 以外のゲームのツール（Cheat Engine など）も見つけます。
  - 見るのはこの PC のファイルとプロセスの一覧だけで、ほかのゲームやアプリには触れず、署名や常駐ドライバも使いません。
- **ゲーム内の Aegis を強化**（登録オフの部屋。試合は止まらず、その人だけが対象）:
  - **2 回で退出**: チャットの連投（3 秒に 5 通）、スピードハック（設定の 2.5 倍を超える速さが 2 秒続く）。2 回は 5 分以内の 2 回だけを数えます。
  - **ホストに知らせるだけ**: 設定の 1.8 倍の速さ、名前の付け直し・試合中の色変更（要求はその場で捨てます）、ロビーでの色の連打、ベントから遠すぎるベント、ホストしか送らない通信が届いた（偽装された通信）。
  - `/ac test chatflood|name|color|speed|ventfar <名前>` で表示を試せます。
- **高 PING の部屋を自動で作り直す（既定でオン）**: 公式のアジア地域でも部屋ごとに割り当てられるサーバーが変わり、近いサーバー（PING 8〜10 ms）と遠いサーバー（100 ms 以上。スマホでは 500 ms を超えることも）があります。部屋を作った直後、まだ誰もいない間に PING が 5 秒続けて 80 ms を超えたら「作り直しますか？」と聞き、**15 秒答えがなければ自動で作り直します**（続けて最大 3 回）。`[Lobby] HostPingLimit`（旧 `MaxHostPing`）、`/opt maxping <ms>`、0 でオフ。

変更履歴の詳細は `CHANGELOG.md`。

### 更新のしかた（v0.5.3 から）

- **ランチャー**: Aegis のトレイアプリは **Setup zip に入っています**。`PocketRoles-Setup-0.5.4.zip` を今のランチャーのフォルダに上書き展開してから、ランチャーの **「更新を確認」** → 「今すぐ更新しますか？」で「はい」（`PocketRoles-0.5.4.zip` をダウンロードして配置します）。`BepInEx\config\` は上書きしないので設定は残ります。新しい設定項目は初回起動時に既定値で追記され、`MaxHostPing` の行は `HostPingLimit = 80` に置き換わります。
- **手動**: `PocketRoles-0.5.4.zip` を展開して `BepInEx\plugins\PocketRoles.dll` と `BepInEx\PocketRoles\lang\*.json` を MOD 用コピーの同じ場所に上書き。

### 確認済み（2026-09-21、登録オフの部屋）

- Aegis のトレイアプリ: スキャン画面（12 項目すべて合格）、常駐と監視の開始、`/ac test` の通知（右下の Aegis の通知）、偽の version.dll を置いた時に起動が止まり、原因と直し方が出ること。
- PC ホスト＋エミュレーター＋スマホ 2 台の 4 人: PING が 100〜500 ms と大きく揺れる状態で 7 分遊び、スピードハックなどの誤検知 0 件。
- 高 PING: 179 ms の部屋 → 15 秒答えなし → 自動で作り直し → 8 ms の部屋（エミュレーターも 141 ms → 10 ms）。古い設定 `MaxHostPing = 0` が `HostPingLimit = 80` に置き換わること。
- 反証レビュー（2 視点・11 件の指摘をすべて修正）。

### ハッシュ（SHA256SUMS.txt）

```
HASHES
```
