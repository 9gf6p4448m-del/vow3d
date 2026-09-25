namespace Vow.Core.Logic
{
    // 佔領模式 HUD 字串表（V080_CAPTURE_PLAN.md V-A23，2026-09-24 凍結）。全部預先建好，查表不配置記憶體
    // （§2.3：字串一律查預建表）。比分不可用 IntStringCache（上限 999，1000 會顯示成 999，R6）。
    public static class CaptureHudLabels
    {
        // v0.9.0 E24：19 塊時最後一次計分前 ≤999、一次最多 +38，最高 1037（v0.8.0 原為 1012）。
        public const int MaxScore = 1037;
        private static readonly string[] ScoreTable = BuildScoreTable();

        public const int MinRespawnSeconds = 1;
        public const int MaxRespawnSeconds = 5;
        private static readonly string[] RespawnTable = BuildRespawnTable();

        // v0.9.0 E25：藍方狂怒剩餘秒數 "RAGE n"（n＝無條件進位，1～12）。
        public const int MinRageSeconds = 1;
        public const int MaxRageSeconds = 12;
        private static readonly string[] RageTable = BuildRageTable();

        // 對局狀態與按鈕字串（§2.4；預建常數，同一參考重複使用）。
        public const string CaptureButtonLabelOff = "CAPTURE";
        public const string CaptureButtonLabelOn = "CAPTURE: ON";
        public const string StatusLobby = "CAPTURE: TAP RED";
        public const string StatusActive = "CAPTURE ACTIVE";
        public const string StatusBlueWins = "BLUE WINS";
        public const string StatusRedWins = "RED WINS";
        public const string StatusDraw = "DRAW";
        public const string StatusLastBlueWins = "LAST: BLUE WINS";
        public const string StatusLastRedWins = "LAST: RED WINS";
        public const string StatusLastDraw = "LAST: DRAW";

        public static string Score(int value)
        {
            if (value < 0) value = 0;
            if (value > MaxScore) value = MaxScore;
            return ScoreTable[value];
        }

        // 剩餘秒數無條件進位到整數秒（E17/§2.4 的 n＝剩餘秒數無條件進位）；夾在 [1,5]。
        public static string Respawn(float remainingSeconds)
        {
            int whole = (int)remainingSeconds;
            int n = remainingSeconds > whole ? whole + 1 : whole;
            if (n < MinRespawnSeconds) n = MinRespawnSeconds;
            if (n > MaxRespawnSeconds) n = MaxRespawnSeconds;
            return RespawnTable[n - MinRespawnSeconds];
        }

        // 狂怒剩餘秒數無條件進位到整數秒（E25：12.0→12、11.75→12、11.0→11、0.25→1）；夾在 [1,12]。
        public static string Rage(float remainingSeconds)
        {
            int whole = (int)remainingSeconds;
            int n = remainingSeconds > whole ? whole + 1 : whole;
            if (n < MinRageSeconds) n = MinRageSeconds;
            if (n > MaxRageSeconds) n = MaxRageSeconds;
            return RageTable[n - MinRageSeconds];
        }

        private static string[] BuildScoreTable()
        {
            string[] table = new string[MaxScore + 1];
            for (int i = 0; i <= MaxScore; i++) table[i] = i.ToString();
            return table;
        }

        private static string[] BuildRespawnTable()
        {
            string[] table = new string[MaxRespawnSeconds - MinRespawnSeconds + 1];
            for (int i = MinRespawnSeconds; i <= MaxRespawnSeconds; i++)
                table[i - MinRespawnSeconds] = "RESPAWN " + i;
            return table;
        }

        private static string[] BuildRageTable()
        {
            string[] table = new string[MaxRageSeconds - MinRageSeconds + 1];
            for (int i = MinRageSeconds; i <= MaxRageSeconds; i++)
                table[i - MinRageSeconds] = "RAGE " + i;
            return table;
        }
    }
}
