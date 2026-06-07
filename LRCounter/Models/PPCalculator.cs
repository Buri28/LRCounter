using System;

namespace LRCounter.Models
{
    // ScoreSaberのPP計算ロジックを再現したユーティリティクラス（staticのみ・インスタンス不要）
    public static class PPCalculator
    {
        // ScoreSaberのPP曲線テーブル（精度→PP倍率のマッピング）
        // pp = ppMultiplier(精度) × star × 42.11
        // ScoreSaber 現行の "Automatically Generated Curve V3"（実データから生成された公式カーブ）の
        // 37 点をそのまま採用。隣接 2 点の線形補間（GetPPMultiplier）で評価する。これは ScoreSaber 側
        // および各コミュニティ計算機（Shurdoof の PP calculator 等）と同一の方式。
        // 出典: https://github.com/Shurdoof/pp-calculator (src/lib/pp/curves.ts, AutogenV3Curve)
        private static readonly (double threshold, double ppMultiplier)[] PPCurve =
        {
            (1.00000, 5.367394282890631),
            (0.99950, 5.019543595874787),
            (0.99900, 4.715470646416203),
            (0.99825, 4.325027383589547),
            (0.99750, 3.996793606763322),
            (0.99625, 3.5526145337555373),
            (0.99500, 3.2022017597337955),
            (0.99375, 2.9190155639254955),
            (0.99250, 2.685667856592722),
            (0.99125, 2.4902905794106913),
            (0.99000, 2.324506282149922),
            (0.98750, 2.058947159052738),
            (0.98500, 1.8563887693647105),
            (0.98250, 1.697536248647543),
            (0.98000, 1.5702410055532239),
            (0.97750, 1.4664726399289512),
            (0.97500, 1.3807102743105126),
            (0.97250, 1.3090333065057616),
            (0.97000, 1.2485807759957321),
            (0.96500, 1.1552120359501035),
            (0.96000, 1.0871883573850478),
            (0.95500, 1.0388633331418984),
            (0.95000, 1.0),
            (0.94000, 0.9417362980580238),
            (0.93000, 0.9039994071865736),
            (0.92000, 0.8728710341448851),
            (0.91000, 0.8488375988124467),
            (0.90000, 0.825756123560842),
            (0.87500, 0.7816934560296046),
            (0.85000, 0.7462290664143185),
            (0.82500, 0.7150465663454271),
            (0.80000, 0.6872268862950283),
            (0.75000, 0.6451808210101443),
            (0.70000, 0.6125565959114954),
            (0.65000, 0.5866010012767576),
            (0.60000, 0.18223233667439062),
            (0.00000, 0.0),
        };

        // ScoreSaberのstar倍率定数（star評価1あたりのPP基準値）
        private const double StarMultiplier = 42.11;

        // 精度（0.0〜1.0）とStar評価からPPを計算する
        // 計算式: PP = PP倍率 × Star評価 × 42.11
        public static double CalculatePP(double accuracy, double starRating)
        {
            if (starRating <= 0) return 0; // アンランク譜面
            if (accuracy <= 0) return 0;
            if (accuracy > 1.0) accuracy = 1.0; // 念のため上限を1に丸める

            double ppMultiplier = GetPPMultiplier(accuracy);
            double rawPP = ppMultiplier * starRating * StarMultiplier;
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
