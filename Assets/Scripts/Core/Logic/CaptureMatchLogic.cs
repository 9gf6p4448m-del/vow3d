namespace Vow.Core.Logic
{
    public enum CaptureMatchState { Off, Lobby, Active, Ended }

    public enum CaptureMatchResult { None, BlueWins, RedWins, Draw }

    // 七塊板塊佔領迴圈的唯一事實來源（V080_CAPTURE_PLAN.md §2.1-1，2026-09-24 凍結）。零 UnityEngine。
    //
    // Tick 內固定順序（語意凍結，不得打亂）：
    //   ① 非 Active 狀態只推進結算停頓
    //   ② 算雙方在哪個光圈（倒地方不算）
    //   ③ 引導／爭奪／翻塊
    //   ④ 倒地倒數與復活地點（此時已看得到本 tick 的翻塊）
    //   ⑤ 計分時鐘
    //   ⑥ 勝負
    public sealed class CaptureMatchLogic
    {
        // 板塊歸屬代碼，數值對齊 Contracts.Faction 的序數（BlueTeam=0,RedTeam=1,Neutral=2）；
        // 本檔不得 using 該型別——它定義在含 using UnityEngine 的 ICombatTarget.cs（precedent：ElementReactionLogic.cs）。
        public const int BlueFactionId = 0;
        public const int RedFactionId = 1;
        public const int NeutralFactionId = 2;

        private readonly CaptureTuning _tuning;
        private readonly int[] _ownership = new int[HexBoardLayout.TileCount];

        private int _blueTile = -1;
        private float _blueProgress;
        private int _redTile = -1;
        private float _redProgress;

        private bool _blueKnockedOut;
        private float _blueRespawnRemaining;
        private bool _bluePendingRespawn;
        private float _bluePendingRespawnX;
        private float _bluePendingRespawnZ;

        private bool _redKnockedOut;
        private float _redRespawnRemaining;
        private bool _redPendingRespawn;
        private float _redPendingRespawnX;
        private float _redPendingRespawnZ;

        private float _scoreClock;
        private float _endPauseRemaining;
        private bool _justReturnedToLobby;

        public CaptureMatchLogic(CaptureTuning tuning) { _tuning = tuning; }

        public CaptureMatchState State { get; private set; } = CaptureMatchState.Off;
        public int StartCount { get; private set; }

        public int BlueScore { get; private set; }
        public int RedScore { get; private set; }

        public CaptureMatchResult Result { get; private set; } = CaptureMatchResult.None;
        public CaptureMatchResult LastResult { get; private set; } = CaptureMatchResult.None;

        // 活性計數器（§3 共同遵守：沒發生/相等這類斷言都要附活性證據）。
        public int FlipCount { get; private set; }
        public int ScoreTickCount { get; private set; }
        public int ContestTickCount { get; private set; }
        public int InterruptCount { get; private set; }
        public int RespawnCount { get; private set; }

        public int OwnerOf(int tile) => _ownership[tile];

        public int BlueChannelingTile => _blueTile;
        public float BlueChannelProgress => _blueProgress;
        public int RedChannelingTile => _redTile;
        public float RedChannelProgress => _redProgress;

        public bool BlueKnockedOut => _blueKnockedOut;
        public float BlueRespawnRemaining => _blueRespawnRemaining;
        public bool RedKnockedOut => _redKnockedOut;
        public float RedRespawnRemaining => _redRespawnRemaining;

        public bool TryEnterCaptureMode()
        {
            if (State != CaptureMatchState.Off) return false;
            State = CaptureMatchState.Lobby;
            return true;
        }

        public bool TryExitCaptureMode()
        {
            if (State != CaptureMatchState.Lobby) return false;
            State = CaptureMatchState.Off;
            return true;
        }

        public bool TryStart()
        {
            if (State != CaptureMatchState.Lobby) return false;
            State = CaptureMatchState.Active;
            StartCount++;

            for (int i = 0; i < HexBoardLayout.TileCount; i++)
            {
                _ownership[i] = i == HexBoardLayout.BlueBaseTile ? BlueFactionId
                               : i == HexBoardLayout.RedBaseTile ? RedFactionId
                               : NeutralFactionId;
            }
            BlueScore = 0;
            RedScore = 0;
            _scoreClock = 0f;

            _blueTile = -1; _blueProgress = 0f;
            _redTile = -1; _redProgress = 0f;

            _blueKnockedOut = false; _blueRespawnRemaining = 0f; _bluePendingRespawn = false;
            _redKnockedOut = false; _redRespawnRemaining = 0f; _redPendingRespawn = false;

            Result = CaptureMatchResult.None;
            _endPauseRemaining = 0f;
            return true;
        }

        // 受傷打斷（E11）：該方引導進度歸零，包含護盾全額吸收的情形（由呼叫端在扣護盾前判斷傷害>0 就通知）。
        // 在圈外受傷（該方目前沒有在任何光圈引導）是安全的no-op，不丟例外。
        public void NotifyDamaged(int side)
        {
            if (side == BlueFactionId)
            {
                if (_blueTile != -1) InterruptCount++;
                _blueTile = -1;
                _blueProgress = 0f;
            }
            else if (side == RedFactionId)
            {
                if (_redTile != -1) InterruptCount++;
                _redTile = -1;
                _redProgress = 0f;
            }
        }

        // 倒地（E19）：重複呼叫（已經倒地時再叫一次）不重啟倒數（V-A16）。
        public void NotifyKnockedOut(int side)
        {
            if (side == BlueFactionId)
            {
                if (_blueKnockedOut) return;
                _blueKnockedOut = true;
                _blueRespawnRemaining = _tuning.RespawnSeconds;
                _blueTile = -1; _blueProgress = 0f;
            }
            else if (side == RedFactionId)
            {
                if (_redKnockedOut) return;
                _redKnockedOut = true;
                _redRespawnRemaining = _tuning.RespawnSeconds;
                _redTile = -1; _redProgress = 0f;
            }
        }

        // 一次性的復活事件：取出時附座標，取一次就清空（E20：基地還是自己的→基地復活點，否則→場邊復活點）。
        public bool TryConsumeBlueRespawn(out float x, out float z)
        {
            if (!_bluePendingRespawn) { x = 0f; z = 0f; return false; }
            x = _bluePendingRespawnX; z = _bluePendingRespawnZ;
            _bluePendingRespawn = false;
            return true;
        }

        public bool TryConsumeRedRespawn(out float x, out float z)
        {
            if (!_redPendingRespawn) { x = 0f; z = 0f; return false; }
            x = _redPendingRespawnX; z = _redPendingRespawnZ;
            _redPendingRespawn = false;
            return true;
        }

        // 一次性的「回待機」旗標（E17：結算停頓結束、Ended→Lobby 那個 tick）。
        public bool TryConsumeJustReturnedToLobby()
        {
            if (!_justReturnedToLobby) return false;
            _justReturnedToLobby = false;
            return true;
        }

        // PlayMode 的終局用比分種子入口（R12）；只寫兩個比分整數，不動計分時鐘、歸屬、進度（V-A15）。
        // #if UNITY_EDITOR 的權限收斂由 Unity 端的入口（Bootstrap）負責——本檔是純邏輯測試也要呼叫得到，不能在這裡加。
        public void SeedScoresForTest(int blueScore, int redScore)
        {
            BlueScore = blueScore;
            RedScore = redScore;
        }

        public void Tick(float dt, float heroX, float heroZ, float opponentX, float opponentZ)
        {
            // ① 非 Active 狀態只推進結算停頓
            if (State != CaptureMatchState.Active)
            {
                if (State == CaptureMatchState.Ended && dt > 0f)
                {
                    _endPauseRemaining -= dt;
                    if (_endPauseRemaining <= 0f)
                    {
                        _endPauseRemaining = 0f;
                        State = CaptureMatchState.Lobby;
                        LastResult = Result;
                        _justReturnedToLobby = true;
                    }
                }
                return;
            }

            // ② 算雙方在哪個光圈（倒地方不算）
            int blueCircle = _blueKnockedOut ? -1 : HexBoardLayout.CircleAt(heroX, heroZ, _tuning.CircleRadius);
            int redCircle = _redKnockedOut ? -1 : HexBoardLayout.CircleAt(opponentX, opponentZ, _tuning.CircleRadius);

            ProcessChannels(blueCircle, redCircle, dt);   // ③ 引導／爭奪／翻塊

            // 復活地點要看「已經處理過本 tick 翻塊之後」的歸屬，所以快照必須排在 ProcessChannels 之後
            // （這兩行的相對位置就是 E20「同一 tick 先翻塊再判定」的語意本體，順序不得調換）。
            bool blueBaseOwnedByBlue = _ownership[HexBoardLayout.BlueBaseTile] == BlueFactionId;
            bool redBaseOwnedByRed = _ownership[HexBoardLayout.RedBaseTile] == RedFactionId;
            ProcessKnockouts(dt, blueBaseOwnedByBlue, redBaseOwnedByRed);   // ④ 倒地倒數與復活地點

            ProcessScoring(dt);   // ⑤ 計分時鐘 ⑥ 勝負
        }

        // 單一方的引導狀態機：離開光圈就歸零（E12）；換到別的光圈視同離開再進入；滿 CaptureSeconds 直接翻轉
        // （不必先中立化，E9/V-A10）。
        private void ProcessChannels(int blueCircle, int redCircle, float dt)
        {
            if (blueCircle != -1 && blueCircle == redCircle)
            {
                // 爭奪只凍結「同一個圈」的進度；若某方是這個 tick 才憑空出現在這個圈（例如 B 步的傳送/復活），
                // 它在別的圈留下的殘留進度視同離圈，先歸零，不得帶進這次爭奪（否則之後回原圈會接著算，違反 E12）。
                if (_blueTile != blueCircle) { _blueTile = -1; _blueProgress = 0f; }
                if (_redTile != redCircle) { _redTile = -1; _redProgress = 0f; }
                ContestTickCount++; // 雙方進度都凍結保留，不增加也不歸零
            }
            else
            {
                ProcessSide(blueCircle, dt, ref _blueTile, ref _blueProgress, BlueFactionId);
                ProcessSide(redCircle, dt, ref _redTile, ref _redProgress, RedFactionId);
            }
        }

        // 倒地倒數與復活地點；atHome 由呼叫端在本 tick 翻塊處理完之後算好傳入（E20）。
        private void ProcessKnockouts(float dt, bool blueBaseOwnedByBlue, bool redBaseOwnedByRed)
        {
            if (_blueKnockedOut)
            {
                if (dt > 0f) _blueRespawnRemaining -= dt;
                if (_blueRespawnRemaining <= 0f)
                {
                    _blueRespawnRemaining = 0f;
                    _blueKnockedOut = false;
                    _bluePendingRespawnX = blueBaseOwnedByBlue ? _tuning.BlueHomeRespawnX : _tuning.BlueEdgeRespawnX;
                    _bluePendingRespawnZ = blueBaseOwnedByBlue ? _tuning.BlueHomeRespawnZ : _tuning.BlueEdgeRespawnZ;
                    _bluePendingRespawn = true;
                    RespawnCount++;
                }
            }
            if (_redKnockedOut)
            {
                if (dt > 0f) _redRespawnRemaining -= dt;
                if (_redRespawnRemaining <= 0f)
                {
                    _redRespawnRemaining = 0f;
                    _redKnockedOut = false;
                    _redPendingRespawnX = redBaseOwnedByRed ? _tuning.RedHomeRespawnX : _tuning.RedEdgeRespawnX;
                    _redPendingRespawnZ = redBaseOwnedByRed ? _tuning.RedHomeRespawnZ : _tuning.RedEdgeRespawnZ;
                    _redPendingRespawn = true;
                    RespawnCount++;
                }
            }
        }

        // 計分時鐘（每滿 1.0 秒整點發一次；翻塊與倒地都不重置這個時鐘）與勝負判定。
        private void ProcessScoring(float dt)
        {
            _scoreClock += dt;
            if (_scoreClock >= _tuning.ScoreTickSeconds)
            {
                _scoreClock -= _tuning.ScoreTickSeconds;
                int blueTiles = 0, redTiles = 0;
                for (int i = 0; i < HexBoardLayout.TileCount; i++)
                {
                    if (_ownership[i] == BlueFactionId) blueTiles++;
                    else if (_ownership[i] == RedFactionId) redTiles++;
                }
                BlueScore += blueTiles * _tuning.ScorePerTilePerTick;
                RedScore += redTiles * _tuning.ScorePerTilePerTick;
                ScoreTickCount++;

                // 同一次計分雙方都達標時分高者勝，相等判平手；比分不截在 1000。
                bool blueWon = BlueScore >= _tuning.WinScore;
                bool redWon = RedScore >= _tuning.WinScore;
                if (blueWon || redWon)
                {
                    State = CaptureMatchState.Ended;
                    if (blueWon && redWon)
                    {
                        Result = BlueScore > RedScore ? CaptureMatchResult.BlueWins
                                : RedScore > BlueScore ? CaptureMatchResult.RedWins
                                : CaptureMatchResult.Draw;
                    }
                    else
                    {
                        Result = blueWon ? CaptureMatchResult.BlueWins : CaptureMatchResult.RedWins;
                    }
                    _endPauseRemaining = _tuning.EndPauseSeconds;
                }
            }
        }

        // ProcessChannels 對單一方套用的狀態機（不必先中立化，E9/V-A10）。
        // 己方塊不引導（主對話 2026-09-24 審稿裁定，加嚴）：單獨站在自己已經擁有的塔圈內不算引導，
        // 不然每 CaptureSeconds 會對自己的塊空轉一次「翻塊」。守塔爭奪（對方也在同一圈）走上面
        // ProcessChannels 的凍結分支，不受這條影響。
        private void ProcessSide(int circle, float dt, ref int tile, ref float progress, int factionId)
        {
            if (circle == -1 || _ownership[circle] == factionId)
            {
                tile = -1;
                progress = 0f;
                return;
            }
            if (circle != tile)
            {
                tile = circle;
                progress = 0f;
            }
            progress += dt;
            if (progress >= _tuning.CaptureSeconds)
            {
                _ownership[circle] = factionId;
                progress = 0f;
                tile = -1;
                FlipCount++;
            }
        }
    }
}
