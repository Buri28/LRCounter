namespace LRCounter.Controllers
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

        // 曲終了時に呼ばれ、結果を保存する
        public void Set(double leftAccPercent, double rightAccPercent, double leftPP, double rightPP, bool hasStar)
        {
            LeftAccuracyPercent = leftAccPercent;
            RightAccuracyPercent = rightAccPercent;
            LeftPP = leftPP;
            RightPP = rightPP;
            HasStar = hasStar;
            HasResult = true;
        }
    }
}
