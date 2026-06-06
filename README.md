# LRCounter

【v1.0.0】ゲームプレイ中に左手・右手それぞれのPP（Performance Points）をリアルタイム表示するMOD  
(for BeatSaber 1.39.1, 1.40.8)

## 機能

- プレイ中に **左手PP** / **右手PP** を HUD にリアルタイム表示
- 各手の **正確率（Accuracy）** も同時表示
- オプションで **左右合計PP** も表示
- ScoreSaber のPP計算カーブを内部実装（ScoreSaber DLL不要）
- BSML 設定画面でカラー・サイズ・位置などをカスタマイズ可能

## 表示イメージ

```
[L]          [R]
12.34pp      15.67pp
 95.2%        97.8%

   Total: 27.01pp
```

## インストール

1. `LRCounter.dll` を `[BeatSaber]\Plugins\` フォルダに格納
2. BeatSaberを起動

## 依存MOD

| MOD | バージョン |
|-----|-----------|
| BSIPA | ^4.3.0 |
| BeatSaberMarkupLanguage (BSML) | ^1.12.0 |
| SiraUtil | ^3.1.0 |

## 設定画面

ソロ → MODSメニューに **LRCOUNTER** タブが追加されます。

| 設定項目 | 説明 |
|---------|------|
| Enabled | MODの有効/無効 |
| 最低Star評価 | 指定以上の Star の譜面でのみ表示（0=常に表示） |
| テキストサイズ | HUD 上のフォントサイズ |
| 小数点以下桁数 | PP の表示精度（0〜4桁） |
| 合計PPを表示 | 左右の合計PP を表示するか |
| 左手カラー | 左手PP テキストの色（HTMLカラーコード） |
| 右手カラー | 右手PP テキストの色（HTMLカラーコード） |
| X/Y オフセット | HUD 上の表示位置調整 |
| RESET ALL SETTINGS | すべての設定を初期値にリセット |

## ビルド方法

```bash
# BeatSaberDir の確認（LRCounter.csproj の BeatSaberDir を環境に合わせて変更してください）

dotnet build LRCounter/LRCounter.csproj
# または VSCode で Ctrl+Shift+B
```

## ⚠ PP計算について

本MODは ScoreSaber 公開情報をもとにした **近似計算** を使用しています。  
ScoreSaberの実際のPP値とは若干差異が出る場合があります。  
Star評価は ScoreSaber のカスタムデータが含まれる譜面でのみ反映されます。

## ライセンス

MIT License
