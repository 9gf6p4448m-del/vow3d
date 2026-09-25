namespace Vow.Core.Logic
{
    public enum CaptureMatchState { Off, Lobby, Active, Ended }

    public enum CaptureMatchResult { None, BlueWins, RedWins, Draw }

    // 板塊佔領迴圈的唯一事實來源（V080_CAPTURE_PLAN.md §2.1-1；v0.9.0 擴充見 V090_ENCIRCLE_PLAN.md §2.1-1）。零 UnityEngine。
    // 棋盤由 CaptureBoardSpec 決定：單參數建構子＝V080Seven（v0.8.0 規則，包夾與狂怒關閉，E27），
    // 雙參數建構子傳 V090Nineteen＝本批十九塊規則。
    //
    // Tick 內固定順序（語意凍結，不得打亂）：
    //   ① 非 Active 狀態只推進結算停頓
    //   ②′ 雙方狂怒倒數減 dt（夾在 0）
    //   ② 算雙方在哪個光圈（倒地方不算）
    //   ③ 引導／爭奪／翻塊
    //   ③b 本 tick 有翻塊就對雙方各跑一次 BFS，連不回己方根的己方塊中立化；依 E13／E14 判定狂怒
    //   ④ 倒地倒數與復活地點（必須在 ③ 之後：看得到本 tick 的翻塊）
    //   ⑤ 計分時鐘
    //   ⑥ 勝負（進入 Ended 時清除狂怒）
    public sealed class CaptureMatchLogic
    {
        // 板塊歸屬代碼，數值對齊 Contracts.Faction 的序數（BlueTeam=0,RedTeam=1,Neutral=2）；
        // 本檔不得 using 該型別——它定義在含 using UnityEngine 的 ICombatTarget.cs（precedent：ElementReactionLogic.cs）。
        public const int BlueFactionId = 0;
        public const int RedFactionId = 1;
        public const int NeutralFactionId = 2;

        private readonly CaptureTuning _tuning;
        private readonly CaptureBoardSpec _spec;
        private readonly int[] _ownership;

        // BFS 暫存（建構時配置，Tick 內零配置）。
        private readonly int[] _bfsQueue;
        private readonly bool[] _bfsReached;

        // 以陣營代碼（0＝藍、1＝紅）為索引的 v0.9.0 狀態。
        private readonly float[] _rageRemaining = new float[2];
        private readonly int[] _neutralizedCount = new int[2];
        private readonly int[] _cutEventCount = new int[2];
        private readonly int[] _rageTriggerCount = new int[2];

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

        public CaptureMatchLogic(CaptureTuning tuning) : this(tuning, CaptureBoardSpec.V080Seven) { }

        public CaptureMatchLogic(CaptureTuning tuning, CaptureBoardSpec spec)
        {
            _tuning = tuning;
            _spec = spec;
            _ownership = new int[spec.TileCount];
            _bfsQueue = new int[spec.TileCount];
            _bfsReached = new bool[spec.TileCount];
        }

        public CaptureBoardSpec Spec => _spec;
        public int TileCount => _spec.TileCount;

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

        // v0.9.0 包夾與狂怒（E9～E16）。狂怒剩餘 > 0 即生效。
        public float BlueRageRemaining => _rageRemaining[BlueFactionId];
        public float RedRageRemaining => _rageRemaining[RedFactionId];
        public int BlueNeutralizedCount => _neutralizedCount[BlueFactionId];
        public int RedNeutralizedCount => _neutralizedCount[RedFactionId];
        public int BlueCutEventCount => _cutEventCount[BlueFactionId];
        public int RedCutEventCount => _cutEventCount[RedFactionId];
        public int BlueRageTriggerCount => _rageTriggerCount[BlueFactionId];
        public int RedRageTriggerCount => _rageTriggerCount[RedFactionId];

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

            // 開局歸屬：雙方母板塊各歸己方，其餘中立（v0.8.0 E5；v0.9.0 E7）。
            for (int i = 0; i < _ownership.Length; i++) _ownership[i] = NeutralFactionId;
            for (int k = 0; k < _spec.MotherCount(BlueFactionId); k++) _ownership[_spec.MotherTile(BlueFactionId, k)] = BlueFactionId;
            for (int k = 0; k < _spec.MotherCount(RedFactionId); k++) _ownership[_spec.MotherTile(RedFactionId, k)] = RedFactionId;
            _rageRemaining[BlueFactionId] = 0f;
            _rageRemaining[RedFactionId] = 0f;
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

        // 包夾測試用的盤面種子入口（V090 E28）：只寫入歸屬，不跑 BFS，不動引導、計分時鐘、狂怒與比分。
        // 呼叫端必須給連通的盤面（每一隊的每一塊都連得回自己的根），否則不連通的塊會留到下一次翻塊才被中立化。
        public void SeedOwnershipForTest(int[] owners)
        {
            if (owners == null || owners.Length != _ownership.Length)
                throw new System.ArgumentException("owners 的長度必須等於 TileCount", nameof(owners));
            for (int i = 0; i < _ownership.Length; i++) _ownership[i] = owners[i];
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

            // ②′ 雙方狂怒倒數（E15：先減再夾在 0；觸發那個 tick 的 ③b 會設回 12，所以觸發當 tick 不扣）
            TickRage(dt);

            // ② 算雙方在哪個光圈（倒地方不算）
            int blueCircle = _blueKnockedOut ? -1 : _spec.CircleAt(heroX, heroZ, _tuning.CircleRadius);
            int redCircle = _redKnockedOut ? -1 : _spec.CircleAt(opponentX, opponentZ, _tuning.CircleRadius);

            // 本 tick 翻下的塊（每方至多一塊）：各方只能翻自己所在的光圈，而雙方同圈是爭奪、不翻塊，
            // 所以「該圈在 ③ 之後變成己方、③ 之前不是」就是本 tick 翻下的那塊。
            int blueCircleOwnerBefore = blueCircle == -1 ? -1 : _ownership[blueCircle];
            int redCircleOwnerBefore = redCircle == -1 ? -1 : _ownership[redCircle];

            ProcessChannels(blueCircle, redCircle, dt);   // ③ 引導／爭奪／翻塊

            int blueFlipped = blueCircle != -1 && blueCircleOwnerBefore != BlueFactionId && _ownership[blueCircle] == BlueFactionId ? blueCircle : -1;
            int redFlipped = redCircle != -1 && redCircleOwnerBefore != RedFactionId && _ownership[redCircle] == RedFactionId ? redCircle : -1;
            if (blueFlipped != -1 || redFlipped != -1) ProcessEncircle(blueFlipped, redFlipped);   // ③b 包夾 BFS 與狂怒判定

            // ④ 必須排在 ③ 之後：復活地點看得到本 tick 的翻塊（E20；把它搬到 ③ 之前就是突變 N13）。
            ProcessKnockouts(dt);   // ④ 倒地倒數與復活地點

            ProcessScoring(dt);   // ⑤ 計分時鐘 ⑥ 勝負
        }

        private void TickRage(float dt)
        {
            for (int side = BlueFactionId; side <= RedFactionId; side++)
            {
                if (_rageRemaining[side] <= 0f) continue;
                _rageRemaining[side] -= dt;
                if (_rageRemaining[side] < 0f) _rageRemaining[side] = 0f;
            }
        }

        // ③b（E9～E14）：對雙方各跑一次 BFS。落後判定用本 tick ⑤ 計分之前的比分（此時尚未計分）。
        private void ProcessEncircle(int blueFlipped, int redFlipped)
        {
            if (!_spec.EncircleEnabled) return;
            int leader = BlueScore > RedScore ? BlueScore : RedScore;
            EncircleSide(BlueFactionId, blueFlipped, BlueScore, leader);
            EncircleSide(RedFactionId, redFlipped, RedScore, leader);
        }

        // 根＝該隊自己的母板塊中目前仍屬該隊的那幾塊（E8）；沿己方相鄰塊擴散，連不到的己方塊中立化（E9／E12）。
        // 只寫歸屬，不動任何引導狀態（E11）。一輪即不動點，不必迭代（E9）。
        private void EncircleSide(int side, int justFlipped, int ownScore, int leader)
        {
            int head = 0, tail = 0;
            for (int i = 0; i < _bfsReached.Length; i++) _bfsReached[i] = false;
            for (int k = 0; k < _spec.MotherCount(side); k++)
            {
                int m = _spec.MotherTile(side, k);
                if (_ownership[m] == side)
                {
                    _bfsReached[m] = true;
                    _bfsQueue[tail++] = m;
                }
            }
            while (head < tail)
            {
                int u = _bfsQueue[head++];
                for (int k = 0; k < _spec.NeighborCount(u); k++)
                {
                    int v = _spec.Neighbor(u, k);
                    if (!_bfsReached[v] && _ownership[v] == side)
                    {
                        _bfsReached[v] = true;
                        _bfsQueue[tail++] = v;
                    }
                }
            }

            bool cutEvent = false;
            for (int i = 0; i < _ownership.Length; i++)
            {
                if (_ownership[i] == side && !_bfsReached[i])
                {
                    _ownership[i] = NeutralFactionId;
                    _neutralizedCount[side]++;
                    // E13：只有「本 tick 開始時就屬於該隊」的塊被中立化才算斷能；該隊本 tick 剛翻下的孤島不算。
                    if (i != justFlipped) cutEvent = true;
                }
            }
            if (!cutEvent) return;
            _cutEventCount[side]++;

            // E14：Denominator·(L−S) > Numerator·L（L＝0 時兩邊都是 0，自然不成立）。
            if (_spec.RageEnabled && _tuning.RageDeficitDenominator * (leader - ownScore) > _tuning.RageDeficitNumerator * leader)
            {
                _rageRemaining[side] = _tuning.RageSeconds;   // E15：刷新回 12，不累加
                _rageTriggerCount[side]++;
            }
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

        // 倒地倒數與復活地點；在倒數到期的那個 tick 才判定（E20），此時已看得到本 tick ③ 的翻塊。
        private void ProcessKnockouts(float dt)
        {
            if (_blueKnockedOut)
            {
                if (dt > 0f) _blueRespawnRemaining -= dt;
                if (_blueRespawnRemaining <= 0f)
                {
                    _blueRespawnRemaining = 0f;
                    _blueKnockedOut = false;
                    SelectRespawnPoint(BlueFactionId, out _bluePendingRespawnX, out _bluePendingRespawnZ);
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
                    SelectRespawnPoint(RedFactionId, out _redPendingRespawnX, out _redPendingRespawnZ);
                    _redPendingRespawn = true;
                    RespawnCount++;
                }
            }
        }

        // 依優先序選第一塊「仍屬己方的己方母板塊」的復活點；一塊都沒有就去場邊（v0.8.0 E7/E8；v0.9.0 E20）。
        private void SelectRespawnPoint(int side, out float x, out float z)
        {
            for (int rank = 0; rank < _spec.MotherCount(side); rank++)
            {
                if (_ownership[_spec.MotherTile(side, rank)] == side)
                {
                    x = _spec.MotherRespawnX(side, rank);
                    z = _spec.MotherRespawnZ(side, rank);
                    return;
                }
            }
            x = _spec.EdgeRespawnX(side);
            z = _spec.EdgeRespawnZ(side);
        }

        // 計分時鐘（每滿 1.0 秒整點發一次；翻塊與倒地都不重置這個時鐘）與勝負判定。
        private void ProcessScoring(float dt)
        {
            _scoreClock += dt;
            if (_scoreClock >= _tuning.ScoreTickSeconds)
            {
                _scoreClock -= _tuning.ScoreTickSeconds;
                int blueTiles = 0, redTiles = 0;
                for (int i = 0; i < _ownership.Length; i++)
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
                    // E15：進入 Ended 時雙方狂怒清為 0。
                    _rageRemaining[BlueFactionId] = 0f;
                    _rageRemaining[RedFactionId] = 0f;
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
