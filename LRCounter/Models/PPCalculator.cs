using System;

namespace LRCounter.Models
{
    // ScoreSaberのPP計算ロジックを再現したユーティリティクラス（staticのみ・インスタンス不要）
    public static class PPCalculator
    {
        // ScoreSaberのPP曲線テーブル（精度→PP倍率のマッピング）
        // pp = ppMultiplier(精度) × star × 42.117208413
        // 0.70〜0.9875 は MyBeatSaberStats の ScoreSaber キャッシュから、PP≠0 のランクスコア
        // 約 4,900 件（star は scoresaber_ranked_maps.json 側の正確な値を採用）の実測 PP 倍率
        // pp ÷ (star × 42.117208413) を精度帯ごとに中央値で再集計した値。
        // カーブは上に凸なので、点が疎だと点と点の間で線形補間が系統的に過大評価してしまう
        // （★6〜9 の譜面で実測より +0.4pp 程度高く出ていた）。これを抑えるため 0.86〜0.97 に
        // 0.0025〜0.005 刻みで中間ノットを追加し、補間線を実測カーブに密着させている。
        // 99% 以上と低精度端はサンプルが薄いため、端点だけは既存の理論寄り値を残して補間する。
        private static readonly (double threshold, double ppMultiplier)[] PPCurve =
        {
            (1.0000, 7.424),
            (0.9990, 6.241),
            (0.9975, 3.679),
            (0.9950, 3.251),
            (0.9925, 2.584),
            (0.9900, 2.311),
            (0.9875, 1.999),
            (0.9850, 1.832),
            (0.9825, 1.691),
            (0.9800, 1.585),
            (0.9775, 1.467),
            (0.9750, 1.380),
            (0.9725, 1.310),
            (0.9700, 1.250),
            (0.9675, 1.202),
            (0.9650, 1.156),
            (0.9625, 1.121),
            (0.9600, 1.093),
            (0.9575, 1.062),
            (0.9550, 1.041),
            (0.9525, 1.020),
            (0.9500, 1.001),
            (0.9450, 0.973),
            (0.9400, 0.941),
            (0.9350, 0.924),
            (0.9300, 0.903),
            (0.9250, 0.888),
            (0.9200, 0.873),
            (0.9150, 0.861),
            (0.9100, 0.848),
            (0.9050, 0.837),
            (0.9000, 0.828),
            (0.8900, 0.809),
            (0.8800, 0.791),
            (0.8700, 0.773),
            (0.8600, 0.760),
            (0.8500, 0.747),
            (0.8250, 0.713),
            (0.8000, 0.687),
            (0.7750, 0.667),
            (0.7500, 0.645),
            (0.7250, 0.628),
            (0.7000, 0.614),
            (0.6500, 0.590),
            (0.6000, 0.450),
            (0.5500, 0.212),
            (0.5000, 0.182),
            (0.00,   0.000),
        };

        // 精度（0.0〜1.0）とStar評価からPPを計算する
        // 計算式: PP = PP倍率 × Star評価 × 42.117208413
        // 42.117208413 はScoreSaberの内部定数（Star1・精度100%のときのPP値）
        public static double CalculatePP(double accuracy, double starRating)
        {
            if (starRating <= 0) return 0; // アンランク譜面
            if (accuracy <= 0) return 0;
            if (accuracy > 1.0) accuracy = 1.0; // 念のため上限を1に丸める

            double ppMultiplier = GetPPMultiplier(accuracy);
            double rawPP = ppMultiplier * starRating * 42.117208413;
            return rawPP;
        }

        // テーブルの隣接する2点を線形補間してPP倍率を返す
        private static double GetPPMultiplier(double accuracy)
        {
            // テーブル範囲外の場合は端の値をそのまま返す
            if (accuracy >= PPCurve[0].threshold)
                return PPCurve[0].ppMultiplier;
            if (accuracy <= PPCurve[PPCurve.Length - 1].threshold)
                return PPCurve[PPCurve.Length - 1].ppMultiplier;

            // accuracyが含まれる区間を探して線形補間
            // 例: accuracy=0.985 → upper=0.985, lower=0.98 の区間で補間
            for (int i = 0; i < PPCurve.Length - 1; i++)
            {
                double upper = PPCurve[i].threshold;
                double lower = PPCurve[i + 1].threshold;

                if (accuracy <= upper && accuracy >= lower)
                {
                    double t = (accuracy - lower) / (upper - lower); // 0.0〜1.0の補間係数
                    double upperMult = PPCurve[i].ppMultiplier;
                    double lowerMult = PPCurve[i + 1].ppMultiplier;
                    return lowerMult + t * (upperMult - lowerMult);
                }
            }

            return 0;
        }

        // スコアと最大スコアから精度を計算する（汎用ユーティリティ）
        public static double ScoreToAccuracy(int score, int maxScore)
        {
            if (maxScore <= 0) return 0;
            return (double)score / maxScore;
        }

        // 3値に分かれたカットスコアから合計スコアを計算する
        // BeatSaberのスコア構造: beforeCut(最大70) + afterCut(最大30) + cutDistance(最大15) = 最大115
        public static int CalculateNoteScore(int beforeCutScore, int afterCutScore, int cutDistanceScore)
        {
            return Clamp(beforeCutScore, 0, 70)
                 + Clamp(afterCutScore, 0, 30)
                 + Clamp(cutDistanceScore, 0, 15);
        }

        // APIから取得した合計カットスコアを0〜115の範囲に正規化する
        public static int CalculateNoteScore(int noteScore)
        {
            return Clamp(noteScore, 0, MaxNoteScore);
        }

        // 1ノーツの最大スコア（beforeCut70 + afterCut30 + cutDistance15 = 115）
        public const int MaxNoteScore = 115;

        // ミスのスコアは0（定数として定義。将来ペナルティが変わったときに変更しやすくするため）
        public static int MissScore => 0;

        // System.Math.Clampが使えないバージョン向けの自前実装
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
