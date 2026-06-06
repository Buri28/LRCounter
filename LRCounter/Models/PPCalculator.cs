using System;

namespace LRCounter.Models
{
    // ScoreSaberのPP計算ロジックを再現したユーティリティクラス（staticのみ・インスタンス不要）
    public static class PPCalculator
    {
        // ScoreSaberのPP曲線テーブル（精度→PP倍率のマッピング）
        // 精度が高いほど倍率が急激に上がる（100%付近でのわずかな差が大きく効く）
        // 値はScoreSaberが公開している近似値を使用
        private static readonly (double threshold, double ppMultiplier)[] PPCurve =
        {
            (1.0,    7.424),
            (0.999,  6.241),
            (0.9975, 5.158),
            (0.995,  4.010),
            (0.9925, 3.241),
            (0.99,   3.005),
            (0.9875, 2.758),
            (0.985,  2.580),
            (0.9825, 2.394),
            (0.98,   2.227),
            (0.975,  2.099),
            (0.97,   1.983),
            (0.965,  1.882),
            (0.96,   1.791),
            (0.955,  1.711),
            (0.95,   1.640),
            (0.94,   1.517),
            (0.93,   1.404),
            (0.92,   1.297),
            (0.91,   1.196),
            (0.90,   1.099),
            (0.875,  0.916),
            (0.85,   0.756),
            (0.825,  0.618),
            (0.80,   0.499),
            (0.75,   0.327),
            (0.70,   0.194),
            (0.65,   0.098),
            (0.60,   0.038),
            (0.55,   0.009),
            (0.50,   0.000),
        };

        // 精度（0.0〜1.0）とStar評価からPPを計算する
        // 計算式: PP = PP倍率 × Star評価 × 42.117208413
        // 42.117208413 はScoreSaberの内部定数（Star1・精度100%のときのPP値）
        public static double CalculatePP(double accuracy, double starRating)
        {
            if (starRating <= 0) return 0; // アンランク譜面
            if (accuracy <= 0)  return 0;
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
                    double t         = (accuracy - lower) / (upper - lower); // 0.0〜1.0の補間係数
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
            return Clamp(beforeCutScore,    0, 70)
                 + Clamp(afterCutScore,     0, 30)
                 + Clamp(cutDistanceScore,  0, 15);
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
