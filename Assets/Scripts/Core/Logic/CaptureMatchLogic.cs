namespace Vow.Core.Logic
{
    public enum CaptureMatchState { Off, Lobby, Active, Ended }

    public enum CaptureMatchResult { None, BlueWins, RedWins, Draw }

    // 板塊佔領迴圈的唯一事實來源（V080_CAPTURE_PLAN.md §2.1-1；v0.9.0 擴充見 V090_ENCIRCLE_PLAN.md §2.1-1）。零 UnityEngine。
    // 棋盤由 CaptureBoardSpec 決定：單參數建構子＝V080Seven（v0.8.0 規則，包夾與狂怒關閉，E27），
    // 雙參數建構子傳 V090Nineteen＝本批十九塊規則。
    //
    // Tick 內固定順序（語意凍結，不得打亂；v0.10.0 新增 ③c、⑦，見 V0100_SANCTUARY_PLAN.md §2.1-1）：
    //   ① 非 Active 狀態只推進結算停頓
    //   ②′ 雙方狂怒倒數減 dt（夾在 0）
    //   ② 算雙方在哪個光圈與所在板塊（倒地方都算 -1）
    //   ③ 引導／爭奪／翻塊（門檻依 E6：自己的母板塊用 ReclaimCaptureSeconds，否則 CaptureSeconds）
    //   ③b 本 tick 有翻塊就對雙方各跑一次 BFS，連不回己方根的己方塊中立化；依 E13／E14 判定狂怒
    //   ③c 圍城時鐘、聖所強度、在聖所判定（E2、E9～E11；每個 Active tick 都跑，不像 ③b 只在翻塊時跑）
    //   ④ 倒地倒數與復活地點（必須在 ③ 之後：看得到本 tick 的翻塊）
    //   ⑤ 計分時鐘、MatchElapsed += dt、計分（E15 慢計分）
    //   ⑥ 1000 分勝負（進入 Ended 時清除狂怒）
    //   ⑦ 時間到（E13；⑥ 之後仍是 Active 且 MatchElapsed ≥ MatchTimeLimitSeconds 才判）
    public sealed class CaptureMatchLogic
    {
        // 板塊歸屬代碼，數值對齊 Contracts.Faction 的序數（BlueTeam=0,RedTeam=1,Neutral=2）；
        // 本檔不得 using 該型別——它定義在含 using UnityEngine 的 ICombatTarget.cs（precedent：ElementReactionLogic.cs）。
        public const int BlueFactionId = 0;
        public const int RedFactionId = 1;
        public const int NeutralFactionId = 2;

        private readonly CaptureTuning _tuning;
        private readonly CaptureBoardSpec _spec;
        private readonly PactTalentLogic _talents = new PactTalentLogic();
        private readonly int[] _ownership;

        // BFS 暫存（建構時配置，Tick 內零配置）。
        private readonly int[] _bfsQueue;
        private readonly bool[] _bfsReached;

        // 以陣營代碼（0＝藍、1＝紅）為索引的 v0.9.0 狀態。
        private readonly float[] _rageRemaining = new float[2];
        private readonly int[] _neutralizedCount = new int[2];
        private readonly int[] _cutEventCount = new int[2];
        private readonly int[] _rageTriggerCount = new int[2];

        // 以陣營代碼為索引的 v0.10.0 狀態（V0100_SANCTUARY_PLAN.md E2～E15）。
        private readonly bool[] _inSanctuary = new bool[2];
        private readonly int[] _sanctuaryPercent = new int[2];      // pct，E3
        private readonly float[] _siegeTime = new float[2];         // siege_B，E10
        private readonly float[] _siegeStepClock = new float[2];    // E11 步進時鐘
        private readonly int[] _previousSiegeTarget = new int[2];   // E11 上一 tick 的 target
        private readonly int[] _units = new int[2];                 // 慢計分整數餘數，E15

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
        public bool FogEnabled { get; private set; } = true;

#if UNITY_EDITOR
        // 只由 Editor 的舊版場景回歸入口使用：保留 v0.9～v0.11 原腳本的全圖可見前提。
        public void DisableFogForLegacyTest() { FogEnabled = false; }
#endif
        public int TileCount => _spec.TileCount;

        public CaptureMatchState State { get; private set; } = CaptureMatchState.Off;
        public int StartCount { get; private set; }

        public int BlueScore { get; private set; }
        public int RedScore { get; private set; }

        public int UnlockedTalentTier => State == CaptureMatchState.Active ? _talents.UnlockedTier : 0;
        public int PendingTalentTier(int side) => State == CaptureMatchState.Active ? _talents.PendingTier(side) : 0;
        public PactTalent SelectedTalent(int side, int tier) => State == CaptureMatchState.Active ? _talents.Selected(side, tier) : PactTalent.None;
        public bool TryChooseTalent(int side, PactTalent talent)
        {
            return State == CaptureMatchState.Active && _spec.TalentsEnabled && _talents.TryChoose(side, talent);
        }

        // 傷害結算當下讀位置和歸屬；燃燒區不可把施法當刻的加成快取下來。
        public float DamageMultiplierFor(int side, float attackerX, float attackerZ)
        {
            if (State != CaptureMatchState.Active || !_spec.TalentsEnabled
                || (side != BlueFactionId && side != RedFactionId)
                || _talents.Selected(side, 3) != PactTalent.GeothermalFrenzy) return 1f;
            int tile = _spec.TileAt(attackerX, attackerZ);
            return tile >= 0 && _ownership[tile] == side ? 1.15f : 1f;
        }

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

        // v0.10.0 母板塊聖所／圍城／倒數（E2、E3、E9～E11、E13、E18）。
        // 在聖所與受傷百分比只在對局 Active 時才可能為 true／<100（E2 的定義本身含「對局 Active」）。
        public bool BlueInSanctuary => State == CaptureMatchState.Active && _inSanctuary[BlueFactionId];
        public bool RedInSanctuary => State == CaptureMatchState.Active && _inSanctuary[RedFactionId];
        public int BlueSanctuaryPercent => _sanctuaryPercent[BlueFactionId];
        public int RedSanctuaryPercent => _sanctuaryPercent[RedFactionId];
        public int BlueDamageTakenPercent => State != CaptureMatchState.Active ? 100
            : (_inSanctuary[BlueFactionId] ? 100 - _sanctuaryPercent[BlueFactionId] : 100);
        public int RedDamageTakenPercent => State != CaptureMatchState.Active ? 100
            : (_inSanctuary[RedFactionId] ? 100 - _sanctuaryPercent[RedFactionId] : 100);
        public float BlueChannelRequiredSeconds => _blueTile == -1 ? 0f : RequiredCaptureSeconds(BlueFactionId, _blueTile);
        public float RedChannelRequiredSeconds => _redTile == -1 ? 0f : RequiredCaptureSeconds(RedFactionId, _redTile);
        // 被圍時間，不進 ICaptureMatchView（E18）；供 V10-A07、A10(h) 的「siege 0」斷言讀取。
        public float BlueSiegeSeconds => _siegeTime[BlueFactionId];
        public float RedSiegeSeconds => _siegeTime[RedFactionId];

        public float MatchElapsed { get; private set; }
        public float MatchRemainingSeconds
        {
            get
            {
                float remaining = _tuning.MatchTimeLimitSeconds - MatchElapsed;
                return remaining > 0f ? remaining : 0f;
            }
        }
        public bool EndedByTime { get; private set; }

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
            _talents.Reset();
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
            _talents.Reset();
            BlueScore = 0;
            RedScore = 0;
            _scoreClock = 0f;

            _blueTile = -1; _blueProgress = 0f;
            _redTile = -1; _redProgress = 0f;

            _blueKnockedOut = false; _blueRespawnRemaining = 0f; _bluePendingRespawn = false;
            _redKnockedOut = false; _redRespawnRemaining = 0f; _redPendingRespawn = false;

            // v0.10.0（E20）：聖所／圍城／倒數／慢計分歸零，第二局不沿用上一局的狀態。
            _inSanctuary[BlueFactionId] = false; _inSanctuary[RedFactionId] = false;
            _sanctuaryPercent[BlueFactionId] = _tuning.SanctuaryPercent;
            _sanctuaryPercent[RedFactionId] = _tuning.SanctuaryPercent;
            _siegeTime[BlueFactionId] = 0f; _siegeTime[RedFactionId] = 0f;
            _siegeStepClock[BlueFactionId] = 0f; _siegeStepClock[RedFactionId] = 0f;
            _previousSiegeTarget[BlueFactionId] = _tuning.SanctuaryPercent;
            _previousSiegeTarget[RedFactionId] = _tuning.SanctuaryPercent;
            _units[BlueFactionId] = 0; _units[RedFactionId] = 0;
            MatchElapsed = 0f;
            EndedByTime = false;

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
        // v0.10.0：另外清雙方 units（慢計分整數餘數），否則種子後殘留的餘數會讓比分提早跨過門檻
        // （V10-A08：不清的話下一次計分就可能多算一分）。不動時鐘、歸屬、進度。
        public void SeedScoresForTest(int blueScore, int redScore)
        {
            BlueScore = blueScore;
            RedScore = redScore;
            _units[BlueFactionId] = 0;
            _units[RedFactionId] = 0;
        }

        // v0.10.0（E19）：只寫 MatchElapsed，不動 pct、步進時鐘、歸屬、比分。
        public void SeedMatchElapsedForTest(float matchElapsed)
        {
            MatchElapsed = matchElapsed;
        }

        // v0.10.0（E19）：只寫該方的被圍時間 siege_B，不動 pct、步進時鐘、歸屬、比分。
        public void SeedSiegeForTest(int side, float seconds)
        {
            _siegeTime[side] = seconds;
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

            // ③c（v0.10.0）：圍城時鐘、聖所強度、在聖所判定。每個 Active tick 都跑（不像 ③b 只在翻塊時跑），
            // 用 ③b 之後的歸屬與本 tick 的位置（E2、E9～E11）。
            ProcessSanctuaryAndSiege(dt, heroX, heroZ, opponentX, opponentZ);

            // ④ 必須排在 ③ 之後：復活地點看得到本 tick 的翻塊（E20；把它搬到 ③ 之前就是突變 N13）。
            ProcessKnockouts(dt);   // ④ 倒地倒數與復活地點

            ProcessScoring(dt);   // ⑤ 計分時鐘、MatchElapsed ⑥ 勝負 ⑦ 時間到
            if (_spec.TalentsEnabled && State == CaptureMatchState.Active)
                _talents.Refresh(MatchElapsed, BlueScore, RedScore);
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

        // 計分時鐘（每滿 1.0 秒整點發一次；翻塊與倒地都不重置這個時鐘）、MatchElapsed（E13）與勝負判定
        // （⑥ 1000 分勝負；⑦ 時間到，v0.10.0）。⑥ 一定排在 ⑦ 之前判——S18 驗的就是這個順序。
        private void ProcessScoring(float dt)
        {
            // ⑤（v0.10.0 E13）：MatchElapsed 只在 TimeLimitEnabled 時累加；V090Nineteen／V080Seven 不受影響。
            if (_spec.TimeLimitEnabled) MatchElapsed += dt;

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
                ScoreTickCount++;

                // E15（v0.10.0）：PacedScoringEnabled 時走整數餘數制（每塊每秒 1 單位，7 單位 1 分），
                // 全程整數運算不會有 float32 累加誤差；否則沿用 v0.8.0 的直接乘法。
                if (_spec.PacedScoringEnabled)
                {
                    _units[BlueFactionId] += blueTiles * _tuning.PacedScoreUnitsPerTilePerTick;
                    _units[RedFactionId] += redTiles * _tuning.PacedScoreUnitsPerTilePerTick;
                    BlueScore += _units[BlueFactionId] / _tuning.PacedScoreUnitsPerPoint;
                    RedScore += _units[RedFactionId] / _tuning.PacedScoreUnitsPerPoint;
                    _units[BlueFactionId] %= _tuning.PacedScoreUnitsPerPoint;
                    _units[RedFactionId] %= _tuning.PacedScoreUnitsPerPoint;
                }
                else
                {
                    BlueScore += blueTiles * _tuning.ScorePerTilePerTick;
                    RedScore += redTiles * _tuning.ScorePerTilePerTick;
                }

                // ⑥ 同一次計分雙方都達標時分高者勝，相等判平手；比分不截在 1000。
                bool blueWon = BlueScore >= _tuning.WinScore;
                bool redWon = RedScore >= _tuning.WinScore;
                if (blueWon || redWon)
                {
                    CaptureMatchResult result = blueWon && redWon
                        ? (BlueScore > RedScore ? CaptureMatchResult.BlueWins
                          : RedScore > BlueScore ? CaptureMatchResult.RedWins
                          : CaptureMatchResult.Draw)
                        : (blueWon ? CaptureMatchResult.BlueWins : CaptureMatchResult.RedWins);
                    EndMatch(result, endedByTime: false);
                }
            }

            // ⑦（v0.10.0 E13）：⑥ 判完後仍是 Active 才判時間到；只在 TimeLimitEnabled 時運作。
            if (_spec.TimeLimitEnabled && State == CaptureMatchState.Active && MatchElapsed >= _tuning.MatchTimeLimitSeconds)
            {
                CaptureMatchResult result = BlueScore > RedScore ? CaptureMatchResult.BlueWins
                    : RedScore > BlueScore ? CaptureMatchResult.RedWins
                    : CaptureMatchResult.Draw;
                EndMatch(result, endedByTime: true);
            }
        }

        // ⑥／⑦ 共用的結束處理：進入 Ended、記結果、開始 3 秒結算、清雙方狂怒（E15：進入 Ended 時雙方狂怒清為 0，
        // ⑥⑦ 都要清——N15 驗的就是這個）。
        private void EndMatch(CaptureMatchResult result, bool endedByTime)
        {
            State = CaptureMatchState.Ended;
            Result = result;
            EndedByTime = endedByTime;
            _endPauseRemaining = _tuning.EndPauseSeconds;
            _rageRemaining[BlueFactionId] = 0f;
            _rageRemaining[RedFactionId] = 0f;
            _talents.Reset();
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
            if (progress >= RequiredCaptureSeconds(factionId, circle))
            {
                _ownership[circle] = factionId;
                progress = 0f;
                tile = -1;
                FlipCount++;
            }
        }

        // E6（v0.10.0）：某方引導的光圈是自己的母板塊時，門檻＝ReclaimCaptureSeconds（1.8），否則 CaptureSeconds（3.5）。
        // 只在 SanctuaryEnabled 時生效；V090Nineteen／V080Seven（SanctuaryEnabled=false）一律用 CaptureSeconds
        // （V10-A05(g) 明白要求舊夾具維持 3.5，不隨新功能縮短）。
        private float RequiredCaptureSeconds(int side, int tile)
        {
            return _spec.SanctuaryEnabled && IsMotherTile(side, tile) ? _tuning.ReclaimCaptureSeconds : _tuning.CaptureSeconds;
        }

        // 某塊是否為該方自己的母板塊（不管目前歸屬）；用於 E2 聖所判定與 E6 奪回門檻。
        private bool IsMotherTile(int side, int tile)
        {
            for (int rank = 0; rank < _spec.MotherCount(side); rank++)
            {
                if (_spec.MotherTile(side, rank) == tile) return true;
            }
            return false;
        }

        // ③c（v0.10.0，E2、E9～E11）：圍城時鐘、聖所強度、在聖所判定。每個 Active tick 都跑。
        // 用本 tick 的位置（heroX/heroZ/opponentX/opponentZ，② 取樣的同一組座標，Tick 期間不會變動）
        // 與 ③b 之後的歸屬（_ownership 已是本 tick 最新狀態）。
        private void ProcessSanctuaryAndSiege(float dt, float heroX, float heroZ, float opponentX, float opponentZ)
        {
            // E2／E3：在聖所判定，只在 SanctuaryEnabled 時生效；否則恆為 false（DamageTakenPercent 恆 100）。
            if (_spec.SanctuaryEnabled)
            {
                int blueTileIdx = _blueKnockedOut ? -1 : _spec.TileAt(heroX, heroZ);
                int redTileIdx = _redKnockedOut ? -1 : _spec.TileAt(opponentX, opponentZ);
                _inSanctuary[BlueFactionId] = blueTileIdx != -1 && IsMotherTile(BlueFactionId, blueTileIdx)
                    && _ownership[blueTileIdx] == BlueFactionId;
                _inSanctuary[RedFactionId] = redTileIdx != -1 && IsMotherTile(RedFactionId, redTileIdx)
                    && _ownership[redTileIdx] == RedFactionId;
            }
            else
            {
                _inSanctuary[BlueFactionId] = false;
                _inSanctuary[RedFactionId] = false;
            }

            // E9～E11：圍城衰減，只在 SiegeEnabled 時運作；否則 pct 維持 TryStart 設定的值不變。
            if (_spec.SiegeEnabled)
            {
                int blueTileCount = 0, redTileCount = 0;
                for (int i = 0; i < _ownership.Length; i++)
                {
                    if (_ownership[i] == BlueFactionId) blueTileCount++;
                    else if (_ownership[i] == RedFactionId) redTileCount++;
                }
                // E9：A 方圍城 B 方 ⇔ 10·(A 方持有塊數) ≥ 7·19（≥14 塊）。B 方進入被圍狀態。
                bool blueBesieged = _tuning.SiegeTileDenominator * redTileCount >= _tuning.SiegeTileNumerator * _spec.TileCount;
                bool redBesieged = _tuning.SiegeTileDenominator * blueTileCount >= _tuning.SiegeTileNumerator * _spec.TileCount;
                StepSiege(BlueFactionId, blueBesieged, dt);
                StepSiege(RedFactionId, redBesieged, dt);
            }
        }

        // E10／E11：先依本 tick 條件更新被圍時間，再用更新後的值算 target；target 改變本 tick 不累加步進時鐘，
        // 不變且 pct≠target 才累加，每滿 SiegeStepSeconds 秒 pct 朝 target 移動 1；pct＝target 步進時鐘歸零。
        private void StepSiege(int side, bool besieged, float dt)
        {
            if (besieged) _siegeTime[side] += dt;
            else _siegeTime[side] = 0f;

            int target = besieged && _siegeTime[side] >= _tuning.SiegeSeconds ? 0 : _tuning.SanctuaryPercent;
            if (target != _previousSiegeTarget[side])
            {
                _siegeStepClock[side] = 0f;
            }
            else if (_sanctuaryPercent[side] != target)
            {
                _siegeStepClock[side] += dt;
                while (_siegeStepClock[side] >= _tuning.SiegeStepSeconds)
                {
                    _siegeStepClock[side] -= _tuning.SiegeStepSeconds;
                    if (_sanctuaryPercent[side] < target) _sanctuaryPercent[side]++;
                    else if (_sanctuaryPercent[side] > target) _sanctuaryPercent[side]--;
                }
            }
            else
            {
                _siegeStepClock[side] = 0f;
            }
            _previousSiegeTarget[side] = target;
        }
    }
}
