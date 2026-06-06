using System;

namespace LRCounter.Models
{
    // ScoreSaberのPP計算ロジックを再現したユーティリティクラス（staticのみ・インスタンス不要）
    public static class PPCalculator
    {
        // ScoreSaberのPP曲線テーブル（精度→PP倍率のマッピング）
        // pp = ppMultiplier(精度) × star × 42.117208413
        // 0.70〜0.995 は MyBeatSaberStats の ScoreSaber キャッシュ約 3.6 万件から
        // 精度帯ごとの中央値を再集計した実測値。99.9% 以上と低精度端はサンプルが薄いため、
        // 端点だけは既存の理論寄り値を残して補間する。
        private static readonly (double threshold, double ppMultiplier)[] PPCurve =
        {
            (1.0000, 7.424),
            (0.9990, 6.241),
            (0.9975, 3.679),
            (0.9950, 3.251),
            (0.9925, 2.584),
            (0.9900, 2.311),
            (0.9875, 2.044),
            (0.9850, 1.823),
            (0.9825, 1.670),
            (0.9800, 1.574),
            (0.9775, 1.463),
            (0.9750, 1.374),
            (0.9725, 1.306),
            (0.9700, 1.248),
            (0.9650, 1.155),
            (0.9600, 1.089),
            (0.9550, 1.038),
            (0.9500, 1.007),
            (0.9400, 0.944),
            (0.9300, 0.906),
            (0.9200, 0.874),
            (0.9100, 0.849),
            (0.9000, 0.828),
            (0.8750, 0.782),
            (0.8500, 0.747),
            (0.8250, 0.715),
            (0.8000, 0.688),
            (0.7750, 0.667),
            (0.7500, 0.647),
            (0.7250, 0.630),
            (0.7000, 0.615),
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
