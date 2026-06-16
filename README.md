# LRCounter

#準備中

Beat Saber で左右それぞれの精度(%)と、その精度が何PPに相当するか表示するカウンターです。  
私のような右手と左手の精度が違いすぎる人向けです。  
左右にバーを表示して周辺視野で精度が認識できるのを目指して作りました。  
※Counters+は使用しません。

- 対応バージョン: Beat Saber **1.39.1**／**1.40.8**
- 依存: BSIPA `^4.3.0` / BeatSaberMarkupLanguage `^1.12.0` / SiraUtil `^3.1.0`
- 設定: ソロ選曲画面の **MODS タブ → LRCounter**、または `UserData/LRCounter.json`

## インストール  
Pluginsフォルダに「LRCounter.dll」を格納してBeatSaberを起動します。

---

## プレイ画面

### 1. プレイ中の左右精度バー（HUD）
右手と左手の精度を左右のバーで表示します。  
<img width="50%" height="50%" alt="image" src="https://github.com/user-attachments/assets/ef8fcf7b-8836-4d6c-9462-6db126628e04" />

- **バーの色（11段階）**:色変更可能
  | 精度 | 色 |
  |---|---|
  | 0%～49% | $\color{#ff0000}{\text{■}}$ 赤 | 
  | 50%～69% | $\color{#ff7f00}{\text{■}}$ 橙 |
  | 70%～79% | $\color{#ffff00}{\text{■}}$ 黄 |
  | 80%～89% | $\color{#00ff00}{\text{■}}$ 緑 |
  | 90%～94% | $\color{#0000ff}{\text{■}}$ 青 |
  | 95% | $\color{#ff00dd}{\text{■}}$ マゼンダ |
  | 96% | $\color{#1AE6E6}{\text{■}}$ シアン |
  | 97% | $\color{#007fff}{\text{■}}$ 水色 |
  | 98% | $\color{#fcb880}{\text{■}}$ 肌色 |
  | 99% | $\color{#a600ff}{\text{■}}$ 紫 |
  | 100% | $\color{#E6E6E6}{\text{■}}$ 白(薄いグレー) |
- **動的レンジ**: 初期は100%上限、90%が下限、精度に追従して上限と下限が切り替わります。(両方の精度が同じ範囲に5ノーツ留まるときに上限、下限を変更)
   - **開始時の自動調整**: 左右合わせて最初の **5ノーツ**（ミス含む）の時点で、左右のうち**高い方の精度**が90%未満なら、その精度を含む上限、下限から開始します
   - 上限下限は10%ごと(デフォルト)、20%ごと、50%ごとの変更が可能
- **バーの横ライン**: 10段階の白線、5段目(95%)は黒の横線の強調
   - **PP取得ライン**: 白の横線の強調。ベスト更新、または0.1pp獲得できるライン
   - **自己ベスト更新ライン**: 黄色の横線の強調。自己ベストの精度を更新
   - **片手ベスト更新ライン**: オレンジの横線の強調。片手の自己ベストの精度を更新(このMODで保存した片手精度のベスト値)
- **精度低下時のフラッシュ**: 精度が下がると一瞬赤く光る。
- **%/PP ラベル**: 各バーの上に現在の精度% と PP を表示（アンランク譜面では PP 非表示）。

#### 精度バーのボーダー
精度バーの枠が、達成状況に応じて点灯します。優先度は **白 ＞ 黄 ＞ オレンジ**
<img width="50%" height="50%" alt="image" src="https://github.com/user-attachments/assets/a1f66547-cff0-4d39-a9bf-128f827ae0c5" />

| 色 | 条件 | 点灯範囲 |
|---|---|---|
| **白** (`BorderColorPP`) | 合算PPが「PP取得ライン」を超えた | 両手同時に点灯 |
| **黄** (`BorderColorScoreUpdate`) | 両手(合算)の自己ベスト精度を更新 | 両手同時に点灯 |
| **オレンジ** (`BorderColorHandBest`) | 片手の自己ベスト(このMOD独自)を更新 | 片手ごとに点灯 |

精度同士の比較なので、ScoreSaber API 失敗時やアンランク譜面でも黄/オレンジは点灯します。

### 2. 合算ラベル
画面中央上部に、両手合算の **PP（上段）と精度%（下段）** を表示します（位置・サイズは設定可）。
- PP は通常黄色で表示、PP取得ラインを超えたときは緑色で表示。
- %は、各%ごとの色で表示。
- アンランク譜面では精度%のみ表示。

---
## リザルト画面

### 1. リザルト画面の左右リザルト
ステージクリア画面の上部に、中央の縦線で区切って左右の結果を表示します。
<img width="50%" height="50%" alt="image" src="https://github.com/user-attachments/assets/393a16f9-93f8-48ea-b7f8-0aac5f644277" />

- 1段目: `精度% (PP)`
- 2段目: `前回ベストとの差分(+/-%)` ＋ `グッドカット数 / 全ノーツ数`
  - 差分は**前回までの自己ベスト精度**と比較。プラスは緑 `+0.03%`、マイナスは赤 `-0.03%`。
  - 初回プレイ・練習・差分なしのときは差分行を空けてノーツ数の位置を固定。

---
## 設定画面（MODS タブ / `LRCounter.json`）
<img width="50%" height="50%" alt="image" src="https://github.com/user-attachments/assets/2ab8bfb2-2eb9-4424-a404-b02ad306d6e5" />

| 項目 | 内容 |
|---|---|
| Enabled | MOD 有効/無効 |
| TextSize | テキストサイズ |
| ShowTotalLabel / ShowAccBar | 合算ラベル・精度バーの表示ON/OFF |
| DepthZ | Canvas をカメラ側へ寄せる前面化距離 |
| AccBar 各種 | 精度バーの間隔・Y・高さ・幅・下端精度(AccBarMin)・動的レンジ(AccBarDynamic) |
| TotalLabel 各種 | 合算ラベルの位置(X/Y)・サイズ |
| Color00〜10 | 11段階の精度帯色 |
| BorderColorPP / BorderColorScoreUpdate / BorderColorHandBest | 枠の色（白/黄/オレンジ） |
| DebugLogging | 詳細ログ出力（既定OFF。ONで診断ログを Debug レベル出力） |

設定画面の **Reset All Settings** で全項目を既定値に戻せます。


## 補足
### 1. PP・Star・Threshold の算出
- **PP 計算**: ScoreSaber の "Automatically Generated Curve V3"（37点・線形補間）を採用。`PP = PP倍率 × Star × 42.114296`。
- **Star 評価**: ScoreSaber API から譜面の Star を取得（ランク譜面のみ）。
- **Threshold（PP取得ライン）**: プレイヤーの全ランクスコア（重み付け `0.965^rank`）から、「この譜面でいくつ以上のPPを出せば合計PPが底上げされるか」の最低ラインを二分探索で算出。目標ラインの精度に逆算して表示。

### 2. ノーフェイル(NF)対応
NF で体力が0になった時点でスコア・最大スコアを半減（ゲーム挙動を再現）。精度は不変。

### 3. 自己ベスト精度の永続化

譜面ごとの**左右の自己ベスト精度**を `UserData/LRCounter_HandBests.txt` に保存します（TAB区切り）。

```
key  leftAcc  rightAcc  leftPP  rightPP  songName  author
```

- `key` = `levelId|難易度|ゲームモード`（難易度・モード違いは別記録）。
- フルクリア時のみ記録・更新（練習モード・途中退出は対象外）。
- 左右それぞれ独立に、上回ったときだけ更新（手ごとの自己ベスト）。
- 付随情報として左右PP・曲名・譜面作者も保存（現状は表示には未使用）。

### 4. ScoreSaber API の発行タイミング

API 呼び出しは最小限に抑えています（レートリミット対策）。

- **起動時に1回**: プレイヤーの合計PP（v2 `players/{id}`）＋ランクスコア（最大3ページ）を順番に取得しキャッシュ。
- **未取得の譜面を初めてプレイしたとき1回**: Star 評価（v2 `leaderboards/hash/...`）を取得しセッション中キャッシュ。
- 同じ曲のリトライ・リスタートでは **API を発行しません**（キャッシュを使用）。
- **v2 APIが失敗したら v1 APIで代替**。
- クリア後のスコア反映はローカル計算のみ（API なし）。

プレイヤーIDは Steam/Oculus 非依存の `IPlatformUserModel` から取得します。
