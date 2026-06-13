namespace LRCounter.Controllers.Results
{
    // 直近のプレイの「左右の平均精度・PP」を保持する永続ストア（App スコープ）。
    // ゲームプレイシーン(Player)で書き込み、リザルト画面(Menu)で読み出すための受け渡し用。
    public class LRResultStore
    {
        public bool HasResult { get; private set; }       // 1曲ぶんの結果が入っているか
        public bool HasStar { get; private set; }          // ランク譜面か（PPを表示してよいか）
        public double LeftAccuracyPercent { get; private set; }  // 左手の平均精度(%)
        public double RightAccuracyPercent { get; private set; } // 右手の平均精度(%)
        public double LeftPP { get; private set; }         // 左手のPP
        public double RightPP { get; private set; }        // 右手のPP
        public int LeftCutNotes { get; private set; }       // 左手のグッドカット数（ミス・バッドカットを除く）
        public int LeftTotalNotes { get; private set; }     // 左手の全ノーツ数
        public int RightCutNotes { get; private set; }      // 右手のグッドカット数（ミス・バッドカットを除く）
        public int RightTotalNotes { get; private set; }    // 右手の全ノーツ数

        // 前回までの自己ベスト精度との差分(%)。HasDelta=false なら表示しない（初回プレイ・練習・未クリア時）。
        public bool HasDelta { get; private set; }          // 差分を表示してよいか（前回記録あり＝true）
        public double LeftDeltaPercent { get; private set; }  // 左手: 今回精度 − 前回ベスト精度(%)
        public double RightDeltaPercent { get; private set; } // 右手: 今回精度 − 前回ベスト精度(%)

        // 曲終了時に呼ばれ、結果を保存する
        public void Set(double leftAccPercent, double rightAccPercent, double leftPP, double rightPP, bool hasStar,
            int leftCutNotes, int leftTotalNotes, int rightCutNotes, int rightTotalNotes,
            bool hasDelta, double leftDeltaPercent, double rightDeltaPercent)
        {
            LeftAccuracyPercent = leftAccPercent;
            RightAccuracyPercent = rightAccPercent;
            LeftPP = leftPP;
            RightPP = rightPP;
            HasStar = hasStar;
            LeftCutNotes = leftCutNotes;
            LeftTotalNotes = leftTotalNotes;
            RightCutNotes = rightCutNotes;
            RightTotalNotes = rightTotalNotes;
            HasDelta = hasDelta;
            LeftDeltaPercent = leftDeltaPercent;
            RightDeltaPercent = rightDeltaPercent;
            HasResult = true;
        }
    }
}
