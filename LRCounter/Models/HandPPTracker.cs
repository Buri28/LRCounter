namespace LRCounter.Models
{
    // 片手（左または右）のスコアと精度をリアルタイムで追跡するクラス
    // ノーツを切るたびにAddCut/AddMissが呼ばれ、AccuracyとPPが自動で更新される
    public class HandPPTracker
    {
        public enum HandType { Left, Right }

        public HandType Hand { get; } // この追跡が左手か右手か

        // ── 統計情報（外部から読み取り可能・内部からのみ書き込み） ──────────────

        public int TotalScore      { get; private set; } = 0; // グッドカットで獲得したスコアの累計
        public int MaxPossibleScore { get; private set; } = 0; // ノーツ数 × 115（ミスも分母に含まれる）
        public int TotalNotes      { get; private set; } = 0; // 判定されたノーツの総数
        public int CutNotes        { get; private set; } = 0; // グッドカットの数
        public int MissedNotes     { get; private set; } = 0; // ミスの数
        public int BadCuts         { get; private set; } = 0; // バッドカットの数

        // 現在の精度（0.0〜1.0）。MaxPossibleScoreが0のときは0を返す
        // 計算式: TotalScore / MaxPossibleScore
        public double Accuracy => MaxPossibleScore > 0
            ? (double)TotalScore / MaxPossibleScore
            : 0;

        public double PP          { get; private set; } = 0;   // 現在の精度から推定されるPP（StarRatingが0なら0）
        public int    LastCutScore { get; private set; } = -1;  // 直前のカット生スコア（0〜115）、未カット時は-1
        public int    CutScoreSerial { get; private set; } = 0; // LastCutScore が更新されるたびに+1（新しいカットの検知用）

        private double _starRating = 0; // PP計算に使うStar評価（LRTrackerServiceからセットされる）

        public HandPPTracker(HandType hand)
        {
            Hand = hand;
        }

        // Star評価をセットしてPPを再計算する（曲開始時・API取得完了後に呼ばれる）
        public void SetStarRating(double starRating)
        {
            _starRating = starRating;
            RecalculatePP();
        }

        // グッドカットを記録する。TotalScoreには実倍率、MaxPossibleScoreにはFC想定倍率を使う。
        // maxCutScore はそのノーツの最大点（通常115・チェーンヘッド85・チェーンリンク20など、
        // ゲームの scoringElement.maxPossibleCutScore をそのまま渡す）。
        // actualMult: 実倍率（ミスで低下する）、idealMult: FC想定倍率（ミスの影響を受けない）
        public void AddCut(int rawCutScore, int maxCutScore, int actualMult, int idealMult)
        {
            int score = ClampScore(rawCutScore, maxCutScore);
            TotalScore       += score * actualMult;
            MaxPossibleScore += maxCutScore * idealMult;
            CutNotes++;
            TotalNotes++;
            // LastCutScore（デバッグ表示用）は115満点ノーツのみ記録する（チェーンは生スコアの意味が異なるため）
            if (maxCutScore == PPCalculator.MaxNoteScore)
            {
                LastCutScore = score;
                CutScoreSerial++;
            }
            RecalculatePP();
        }

        // ミスを記録する。スコアは加算されないが分母（MaxPossibleScore）は倍率込みで増える
        public void AddMiss(int maxCutScore, int idealMult)
        {
            MaxPossibleScore += maxCutScore * idealMult;
            MissedNotes++;
            TotalNotes++;
            RecalculatePP();
        }

        // バッドカットを記録する。分母はミスと同様に増えるが、ミスとは別カウントにする
        public void AddBadCut(int maxCutScore, int idealMult)
        {
            MaxPossibleScore += maxCutScore * idealMult;
            BadCuts++;
            TotalNotes++;
            RecalculatePP();
        }

        // 生スコアをそのノーツの [0, maxCutScore] に収める
        private static int ClampScore(int value, int maxCutScore)
        {
            if (value < 0) return 0;
            if (value > maxCutScore) return maxCutScore;
            return value;
        }

        // 曲開始時に全カウンタをリセットする
        public void Reset()
        {
            TotalScore       = 0;
            MaxPossibleScore = 0;
            TotalNotes       = 0;
            CutNotes         = 0;
            MissedNotes      = 0;
            BadCuts          = 0;
            PP               = 0;
            LastCutScore     = -1;
            CutScoreSerial   = 0;
        }

        // 現在のAccuracyとStarRatingからPPを再計算する
        private void RecalculatePP()
        {
            PP = PPCalculator.CalculatePP(Accuracy, _starRating);
        }

        public override string ToString()
        {
            string handStr = Hand == HandType.Left ? "L" : "R";
            return $"{handStr}: {PP:F2}pp ({Accuracy * 100:F2}%)";
        }
    }
}
