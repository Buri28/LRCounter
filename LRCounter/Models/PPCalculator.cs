using System;

namespace LRCounter.Models
{
    // ScoreSaberのPP計算ロジックを再現したユーティリティクラス（staticのみ・インスタンス不要）
    public static class PPCalculator
    {
        // ScoreSaberのPP曲線テーブル（精度→PP倍率のマッピング）
        // pp = ppMultiplier(精度) × star × 42.117208413  （star×42.117 = APIの maxPP）
        // 値は現行ScoreSaberの実スコア(by-id/.../scores の baseScore と pp)から逆算して再構築した。
        // 検証: 73.07% → 倍率0.633（10.19★ maxPP429.14 で 271.5pp）が実値と一致。
        // 精度が高いほど倍率が急激に上がり、95%で約1.0、99%で約2.3を超える。
        private static readonly (double threshold, double ppMultiplier)[] PPCurve =
        {
            (1.00,   3.90),   // 99.5%超は実スコアが少なく外挿（最上位はやや誤差あり）
            (0.9975, 3.45),
            (0.995,  3.05),
            (0.9925, 2.66),
            (0.99,   2.327),
            (0.9875, 2.066),
            (0.985,  1.862),
            (0.984,  1.793),
            (0.9825, 1.698),
            (0.98,   1.576),
            (0.975,  1.382),
            (0.97,   1.251),
            (0.965,  1.157),
            (0.96,   1.089),
            (0.955,  1.035),
            (0.95,   1.005),
            (0.945,  0.966),
            (0.94,   0.945),
            (0.93,   0.905),
            (0.92,   0.875),
            (0.91,   0.855),
            (0.90,   0.835),
            (0.875,  0.780),
            (0.85,   0.755),
            (0.825,  0.715),
            (0.80,   0.690),
            (0.775,  0.667),
            (0.75,   0.645),
            (0.725,  0.629),
            (0.70,   0.610),
            (0.65,   0.555),
            (0.60,   0.450),
            (0.50,   0.260),
            (0.00,   0.000),
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
