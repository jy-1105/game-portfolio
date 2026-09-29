# VR寿司弾丸 — 主要ソースコード

ポートフォリオで紹介している実装のうち、投擲・注文・客の移動・スコア管理を担当する7ファイルです。コメントとInspectorの説明文を整理し、ファイル名をクラス名に合わせています。ゲームの処理は変更していません。

| ファイル | 処理 |
| --- | --- |
| [SushiThrowable.cs](SushiThrowable.cs) | SteamVRのつかみ・投擲、客への受け渡し、同じ寿司の重複判定防止 |
| [CustomerOrderWithTimer.cs](CustomerOrderWithTimer.cs) | 注文の選択・時間管理・正誤判定、次の注文への切り替え |
| [CustomerOrderView.cs](CustomerOrderView.cs) | 注文アイコン・残り時間・タイムゲージの表示 |
| [SushiOrderData.cs](SushiOrderData.cs) | 寿司の種類・アイコン・注文ボイスのデータ |
| [CustomerSpawner.cs](CustomerSpawner.cs) | 空席の確認、客の順次生成、NavMesh上への配置 |
| [CustomerSitting.cs](CustomerSitting.cs) | 座席への移動・着席、注文の開始、予約席と着席済みの席の解放 |
| [ScoreManager.cs](ScoreManager.cs) | 得点・統計・ハイスコア、シーンをまたぐUI参照、リプレイ画像の記録と解放 |

## 読む順番

注文処理は `SushiOrderData` → `CustomerOrderWithTimer` → `CustomerOrderView` の順で確認できます。寿司の命中は `SushiThrowable.TryDeliverTo` から `TryReceiveSushi` に渡され、結果が `ScoreManager` に反映されます。

客の生成は `CustomerSpawner`、移動と着席は `CustomerSitting` が担当します。`GoToSeat` は移動先の設定後に席を予約し、`Leave` と `OnDestroy` は共通の `ReleaseSeat` を呼びます。

## 公開範囲

Unityプロジェクト全体ではありません。この7ファイルだけではビルド・実行できません。

- Unity、SteamVR、TextMeshProなどの環境と設定が必要です。
- 参照する `SushiKind`、`SushiType`、`SeatPoint` の定義、シーン、Prefab、素材はこの公開範囲に含みません。
- 寿司の生成、ゲーム全体の進行、ランク・リプレイの表示を担当する別スクリプトは含みません。`ScoreManager` にはランクの閾値とリプレイ画像の記録処理が含まれます。
- リトライ時のスコア初期化や録画停止など、別スクリプトからの呼び出しも必要です。
- フィールドの初期値（注文45秒、正解+100、誤答・時間切れ各-10、客の生成間隔30秒、画像記録0.5秒ごと・最大50枚）はコード上の値です。InspectorやPrefabに保存された値によって実際の動作は変わります。

実行データとプレイ動画は[ポートフォリオ](https://jy-1105.github.io/game-portfolio/#sushi)をご覧ください。
