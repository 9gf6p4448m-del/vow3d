using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // v0.10.0 步驟 C 的新驗收（V0100_SANCTUARY_PLAN.md §3-C，凍結）：V10-C04 放置整局、V10-C05 佔領對局零配置。
    // 「幀」＝一次 yield return null；「開局」＝真實點 CAPTURE 再真實點對手。captureDeltaTime 由各條固定（C04＝1/10、
    // C05＝1/60），finally 與 TearDown 都還原。期望值一律寫字面值，不讀 CaptureTuning／CaptureBoardSpec／HexBoardLayout。
    public sealed class CaptureSanctuaryMatchPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float TapMarginPixels = 16f;   // §3 共同遵守：點擊前投影離四邊都 ≥ 16px，不成立就判紅
        private const int TileCount = 19;
        private const int BlueCode = 0, RedCode = 1, NeutralCode = 2; // CaptureMatchLogic 陣營代碼（字面值）

        // 用到的塔心（v0.9.0 E3 字面值）。
        private static readonly Vector3 Tower15 = new Vector3(-13.125f, 0f, -7.578125f);

        private static string ToolchainDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "vow-toolchain"));

        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;
        private HeroLocomotion _heroLocomotion;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; AllocationProbe.Measuring = false; }

        private IEnumerator Setup(float dt)
        {
            Time.captureDeltaTime = dt;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _bootstrap.UseFlatCaptureSpecForTest();
            _hero = Object.FindObjectOfType<HeroController>();
            _opponent = Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(_bootstrap);
            Assert.IsNotNull(_hero);
            Assert.IsNotNull(_opponent);
            _heroLocomotion = _hero.GetComponent<HeroLocomotion>();
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
        }

        // ───────────── 共用操作（全部走真實觸控路由）─────────────

        private void TapCaptureButton()
        {
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float x, out float y), "拿不到 CAPTURE 鈕的螢幕點");
            Assert.IsTrue(x >= TapMarginPixels && x <= Screen.width - TapMarginPixels
                          && y >= TapMarginPixels && y <= Screen.height - TapMarginPixels,
                "CAPTURE 鈕的螢幕點 (" + x + ", " + y + ") 離畫面邊緣不到 16px");
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

        private void TapOpponent()
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
            Assert.Greater(screen.z, 0f, "對手在鏡頭後方");
            Assert.IsTrue(screen.x >= TapMarginPixels && screen.x <= Screen.width - TapMarginPixels
                          && screen.y >= TapMarginPixels && screen.y <= Screen.height - TapMarginPixels,
                "對手的點擊點投影 (" + screen.x + ", " + screen.y + ") 離畫面邊緣不到 16px（畫面 "
                + Screen.width + "x" + Screen.height + "）");
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
        }

        private void EnterLobbyAndStart()
        {
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "在 Lobby 點對手應開佔領局");
        }

        private Faction Owner(int tile) { return _bootstrap.CaptureView.OwnerOf(tile); }

        private void SeedBoard(int[] blue, int[] red)
        {
            int[] owners = new int[TileCount];
            for (int i = 0; i < TileCount; i++) owners[i] = NeutralCode;
            for (int i = 0; i < blue.Length; i++) owners[blue[i]] = BlueCode;
            for (int i = 0; i < red.Length; i++) owners[red[i]] = RedCode;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
        }

        // ═════════════════════════ V10-C04 放置整局（captureDeltaTime＝1/10）═════════════════════════
        // 開局後英雄完全不輸入，直到回 Lobby 或 9600 幀（960 秒，窗口寫死）。逐幀記錄倒數、比分、聖所強度、在聖所、
        // 受傷百分比；英雄每一刀記「受傷當下實際套用的 _hero.DamageTakenPercent」——在 OnDuelDamaged 回呼內讀：
        // 這個事件在 HeroController.TakeDuelDamage 內、乘上 P 的前一行發出，兩者之間沒有任何寫入，所以讀到的就是
        // 這一刀實際用的值（不讀 view：對手出手與 TickCapture 同幀先後不固定）。扣血後 HP 在同一幀的測試協程讀
        //（協程在全部 Update 之後；對手出手在 TrainingOpponent.Update）。
        // 致死那一刀的 HP 被 HeroVitality 夾在 0，HP 差值不是這一刀的傷害量：改驗「扣血前 HP ≤ 17（20）」（與一刀 17（20）
        // 一致；實際扣 20 以上時，扣血前 17～20 也會倒地而被抓到）。詳見 vow-toolchain/v0100-stepC-readback.md §1。
        private const float IdleDt = 0.1f;
        private const int IdleWindowFrames = 9600;
        private const int MaxHits = 512;

        [UnityTest]
        public IEnumerator V10_C04_AnIdleMatchAtTenFps_RedWinsByScoreInFiveToTenMinutes_TheSiegeDecays_AndEveryHitUsesTheAppliedPercent()
        {
            int[] hitFrame = new int[MaxHits];
            int[] hitPercent = new int[MaxHits];
            float[] hitHpBefore = new float[MaxHits];
            float[] hitHpAfter = new float[MaxHits];
            bool[] hitKnockedOut = new bool[MaxHits];
            int hitCount = 0;
            int pendingHits = 0;
            bool hitOverflow = false;
            int currentFrame = 0;
            System.Action onDamaged = null;
            try
            {
                yield return Setup(IdleDt);
                onDamaged = () =>
                {
                    pendingHits++;
                    if (hitCount >= MaxHits) { hitOverflow = true; return; }
                    hitFrame[hitCount] = currentFrame;
                    hitPercent[hitCount] = _hero.DamageTakenPercent; // 受傷當下實際套用的值（乘 P 的前一行）
                    hitHpBefore[hitCount] = _hero.Health;             // 扣血前 HP（此時尚未乘 P、未扣）
                    hitCount++;
                };
                _hero.OnDuelDamaged += onDamaged;

                EnterLobbyAndStart();
                ICaptureMatchView view = _bootstrap.CaptureView;

                float[] remainingAt = new float[IdleWindowFrames + 1];
                int[] blueAt = new int[IdleWindowFrames + 1];
                int[] redAt = new int[IdleWindowFrames + 1];
                int[] percentAt = new int[IdleWindowFrames + 1];
                bool[] inSanctuaryAt = new bool[IdleWindowFrames + 1];
                int[] damageTakenAt = new int[IdleWindowFrames + 1];
                CaptureMatchState[] stateAt = new CaptureMatchState[IdleWindowFrames + 1];

                int lastFrame = 0, endedFrame = -1, lobbyFrame = -1;
                bool endedByTime = false;
                int blueAtEnd = -1, redAtEnd = -1;
                float elapsedAtEnd = -1f;
                for (int f = 1; f <= IdleWindowFrames; f++)
                {
                    currentFrame = f;
                    yield return null;
                    lastFrame = f;
                    if (pendingHits > 1)
                        Assert.Fail("第 " + f + " 幀出現 " + pendingHits + " 次受傷回呼（對手一刀 1.7 秒，不應同幀兩刀）");
                    Assert.IsFalse(hitOverflow, "受傷次數超過紀錄上限 " + MaxHits);
                    if (pendingHits == 1)
                    {
                        hitHpAfter[hitCount - 1] = _hero.Health;
                        hitKnockedOut[hitCount - 1] = !_hero.IsAlive;
                        pendingHits = 0;
                    }

                    remainingAt[f] = view.MatchRemainingSeconds;
                    blueAt[f] = view.BlueScore;
                    redAt[f] = view.RedScore;
                    percentAt[f] = view.BlueSanctuaryPercent;
                    inSanctuaryAt[f] = view.BlueInSanctuary;
                    damageTakenAt[f] = view.BlueDamageTakenPercent;
                    stateAt[f] = view.State;

                    if (endedFrame < 0 && view.State == CaptureMatchState.Ended)
                    {
                        endedFrame = f;
                        endedByTime = view.EndedByTime;
                        blueAtEnd = view.BlueScore;
                        redAtEnd = view.RedScore;
                        elapsedAtEnd = 900f - view.MatchRemainingSeconds;
                    }
                    if (view.State == CaptureMatchState.Lobby)
                    {
                        lobbyFrame = f;
                        break;
                    }
                }

                // ── 證據落檔（先寫檔再斷言，紅燈時也留得下軌跡）──
                int firstDecayFrame = -1;
                int worstSlackFrame = -1;
                double worstSlack = double.MaxValue;
                int violationFrame = -1, violationSum = 0;
                double violationLimit = 0;
                StringBuilder trace = new StringBuilder(64 * (lastFrame + 1));
                trace.Append("frame,elapsed,remaining,blue,red,sum,limit,blueSanctPct,blueInSanct,blueDamageTakenPct,state\n");
                for (int f = 1; f <= lastFrame; f++)
                {
                    double elapsed = 900.0 - remainingAt[f];
                    double limit = System.Math.Floor(19.0 * elapsed / 7.0) + 2.0;
                    int sum = blueAt[f] + redAt[f];
                    if (firstDecayFrame < 0 && percentAt[f] < 15) firstDecayFrame = f;
                    if (limit - sum < worstSlack) { worstSlack = limit - sum; worstSlackFrame = f; }
                    if (violationFrame < 0 && sum > limit) { violationFrame = f; violationSum = sum; violationLimit = limit; }
                    trace.Append(f).Append(',').Append(elapsed.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                         .Append(remainingAt[f].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                         .Append(blueAt[f]).Append(',').Append(redAt[f]).Append(',').Append(sum).Append(',')
                         .Append(limit.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                         .Append(percentAt[f]).Append(',').Append(inSanctuaryAt[f] ? 1 : 0).Append(',')
                         .Append(damageTakenAt[f]).Append(',').Append(stateAt[f]).Append('\n');
                }
                File.WriteAllText(Path.Combine(ToolchainDir, "v0100-C04-trace.csv"), trace.ToString(), new UTF8Encoding(false));

                int hits85 = 0, hits100 = 0, knockouts85 = 0, knockouts100 = 0, hitsOther = 0, literalMismatches = 0;
                StringBuilder hits = new StringBuilder();
                hits.Append("frame,elapsed,appliedPercent,hpBefore,hpAfter,drop,knockedOut\n");
                for (int k = 0; k < hitCount; k++)
                {
                    float drop = hitHpBefore[k] - hitHpAfter[k];
                    int p = hitPercent[k];
                    if (p == 85 && !hitKnockedOut[k]) hits85++;
                    else if (p == 100 && !hitKnockedOut[k]) hits100++;
                    else if (p == 85) knockouts85++;
                    else if (p == 100) knockouts100++;
                    else hitsOther++;
                    // 字面讀法（致死刀的 HP 差值也要恰為 17／20）只記錄不斷言，回報時一併列出。
                    if ((p == 85 && Mathf.Abs(drop - 17f) > 1e-3f) || (p == 100 && Mathf.Abs(drop - 20f) > 1e-3f)) literalMismatches++;
                    double hitElapsed = hitFrame[k] >= 1 && hitFrame[k] <= lastFrame ? 900.0 - remainingAt[hitFrame[k]] : -1.0;
                    hits.Append(hitFrame[k]).Append(',').Append(hitElapsed.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(p).Append(',').Append(hitHpBefore[k].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(hitHpAfter[k].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(drop.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(hitKnockedOut[k] ? 1 : 0).Append('\n');
                }
                File.WriteAllText(Path.Combine(ToolchainDir, "v0100-C04-hits.csv"), hits.ToString(), new UTF8Encoding(false));

                double firstDecayElapsed = firstDecayFrame > 0 ? 900.0 - remainingAt[firstDecayFrame] : -1.0;
                Debug.Log("[V10-C04] lastFrame=" + lastFrame + " endedFrame=" + endedFrame + " lobbyFrame=" + lobbyFrame
                          + " elapsedAtEnd=" + elapsedAtEnd.ToString("F3") + " score=" + blueAtEnd + "/" + redAtEnd
                          + " endedByTime=" + endedByTime + " status=" + _bootstrap.MatchStatusLabel
                          + " firstDecayFrame=" + firstDecayFrame + " firstDecayElapsed=" + firstDecayElapsed.ToString("F3")
                          + " hits=" + hitCount + " hits85=" + hits85 + " hits100=" + hits100 + " ko85=" + knockouts85
                          + " ko100=" + knockouts100 + " other=" + hitsOther + " literalMismatches=" + literalMismatches
                          + " worstSlack=" + worstSlack.ToString("F0") + "@f" + worstSlackFrame + " violationFrame=" + violationFrame);

                // ── 斷言 ──
                Assert.Greater(lobbyFrame, 0, "9600 幀（960 秒）內沒有回 Lobby；最後狀態 " + view.State
                                             + "、比分 " + view.BlueScore + "/" + view.RedScore);
                Assert.Greater(endedFrame, 0, "回 Lobby 之前應先進入 Ended");
                // ①
                Assert.AreEqual("LAST: RED WINS", _bootstrap.MatchStatusLabel, "①：放置局應紅勝");
                Assert.IsFalse(endedByTime, "①：放置局應由 1000 分結束，不是時間到");
                Assert.GreaterOrEqual(redAtEnd, 1000, "①：結束時紅分");
                Assert.LessOrEqual(redAtEnd, 1002, "①：結束時紅分");
                // ②
                Assert.GreaterOrEqual(elapsedAtEnd, 300f, "②：結束時已過時間（秒）");
                Assert.LessOrEqual(elapsedAtEnd, 600f, "②：結束時已過時間（秒）");
                // ③
                Assert.Greater(firstDecayFrame, 0, "③：結束前聖所強度從沒低於 15（圍城衰減在 AI 路線下沒發生）");
                Assert.LessOrEqual(firstDecayFrame, endedFrame, "③：第一次衰減應發生在結束前");
                Assert.GreaterOrEqual(firstDecayElapsed, 121.0, "③：第一次衰減發生在已過 " + firstDecayElapsed + " 秒");
                // ④
                for (int k = 0; k < hitCount; k++)
                {
                    float drop = hitHpBefore[k] - hitHpAfter[k];
                    string at = "第 " + hitFrame[k] + " 幀（套用值 " + hitPercent[k] + "、扣血前 " + hitHpBefore[k] + "、扣血後 "
                                + hitHpAfter[k] + (hitKnockedOut[k] ? "、倒地" : "") + "）";
                    if (hitPercent[k] == 85)
                    {
                        if (hitKnockedOut[k]) Assert.LessOrEqual(hitHpBefore[k], 17f + 1e-3f, "④：套用值 85 的致死刀，扣血前 HP 應 ≤ 17：" + at);
                        else Assert.AreEqual(17f, drop, 1e-3f, "④：套用值 85 的掉血應為 17：" + at);
                    }
                    else if (hitPercent[k] == 100)
                    {
                        if (hitKnockedOut[k]) Assert.LessOrEqual(hitHpBefore[k], 20f + 1e-3f, "④：套用值 100 的致死刀，扣血前 HP 應 ≤ 20：" + at);
                        else Assert.AreEqual(20f, drop, 1e-3f, "④：套用值 100 的掉血應為 20：" + at);
                    }
                }
                Assert.GreaterOrEqual(hits85, 1, "④：至少一次掉血發生在套用值＝85 時（活性）");
                // ⑤
                Assert.AreEqual(-1, violationFrame, "⑤：第 " + violationFrame + " 幀雙方分數和 " + violationSum
                                                    + " 超過 ⌊19×已過秒數／7⌋＋2＝" + violationLimit);
            }
            finally
            {
                if (onDamaged != null && _hero != null) _hero.OnDuelDamaged -= onDamaged;
                Time.captureDeltaTime = 0f;
            }
        }

        // ═════════════════════════ V10-C05 佔領對局零配置（含聖所、圍城、倒數字串）═════════════════════════
        // 寫法同 V9-C03（AllocationProbe 前後夾住全場 Update／LateUpdate，只用 ProfilerRecorder 讀數）。
        // 暖機局（R14：只碰量測局不會用到的東西）：藍 14 塊讓**紅方**被圍、紅方聖所衰減（HUD 第二列只看藍方，所以不碰任何
        // SIEGE n% 字串）；英雄在 15 號翻塊（跑一次翻塊與 BFS；15 號量測局不會動）；英雄不在任何母板塊（第二列不顯示，
        // 不碰 SANCT）；倒數種子讓暖機局約 4 秒後時間到結束（倒數字串只用 0:04～0:00），4 號全程中立不動。
        // 量測局：開局後種子 V10-A06 主盤面＋SeedCaptureSiegeForTest(藍, 119.5)，英雄在出生點；開局後 60 幀開始量，
        // 窗口固定 480 幀。窗口內沒有任何測試入口的動作（衰減、倒數、計分、對手走位都由遊戲自己跑）。
        [UnityTest]
        public IEnumerator V10_C05_ACaptureMatchWithSiegeDecayTheClockAndTheSanctRow_AllocatesNothingInUpdateOrLateUpdate()
        {
            try
            {
                yield return Setup(1f / 60f);
                ICaptureMatchView view = _bootstrap.CaptureView;

                // ── 暖機局 ──
                EnterLobbyAndStart();
                SeedBoard(new[] { 0, 1, 2, 3, 5, 6, 9, 10, 11, 12, 13, 14, 16, 17 }, new[] { 7, 8, 18 }); // 4、15 中立
                _bootstrap.SeedCaptureSiegeForTest(RedCode, 119.5f);
                _bootstrap.SeedCaptureMatchElapsedForTest(896f);
                _heroLocomotion.WarpTo(Tower15);
                int flipsWarmBefore = _bootstrap.CaptureFlipCount;
                int sanctLabelsWarmBefore = _bootstrap.CaptureSanctuaryLabelRecomputeCount;
                int redPercentMin = 15, bluePercentMin = 15;
                float warmRemainingMax = 0f;
                bool tile4Touched = false;
                int warm = 0;
                while (_bootstrap.CaptureState != CaptureMatchState.Lobby && warm < 600)
                {
                    yield return null;
                    warm++;
                    if (view.RedSanctuaryPercent < redPercentMin) redPercentMin = view.RedSanctuaryPercent;
                    if (view.BlueSanctuaryPercent < bluePercentMin) bluePercentMin = view.BlueSanctuaryPercent;
                    if (view.State == CaptureMatchState.Active && view.MatchRemainingSeconds > warmRemainingMax)
                        warmRemainingMax = view.MatchRemainingSeconds;
                    if (Owner(4) != Faction.Neutral) tile4Touched = true;
                }
                Debug.Log("[V10-C05] warm-up frames=" + warm + " state=" + _bootstrap.CaptureState + " endedByTime=" + view.EndedByTime
                          + " redPercentMin=" + redPercentMin + " bluePercentMin=" + bluePercentMin
                          + " remainingMax=" + warmRemainingMax.ToString("F3") + " flips=" + (_bootstrap.CaptureFlipCount - flipsWarmBefore)
                          + " sanctLabels=" + (_bootstrap.CaptureSanctuaryLabelRecomputeCount - sanctLabelsWarmBefore)
                          + " owner15=" + Owner(15) + " tile4Touched=" + tile4Touched);
                Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "暖機局應時間到結束並回 Lobby");
                Assert.IsTrue(view.EndedByTime, "暖機局應走時間到（⑦）結束");
                Assert.AreEqual(1, _bootstrap.CaptureFlipCount - flipsWarmBefore, "暖機局只翻 15 號一塊");
                Assert.AreEqual(Faction.BlueTeam, Owner(15), "暖機局：英雄應翻下 15 號");
                Assert.Less(redPercentMin, 15, "暖機局：紅方聖所應衰減（活性）");
                Assert.AreEqual(15, bluePercentMin, "暖機局不得碰藍方的圍城衰減（SIEGE 字串留給量測局）");
                Assert.AreEqual(0, _bootstrap.CaptureSanctuaryLabelRecomputeCount - sanctLabelsWarmBefore,
                    "暖機局不得碰第二列字串（SANCT／SIEGE 留給量測局）");
                Assert.LessOrEqual(warmRemainingMax, 4f, "暖機局的倒數只應落在 0:04 以下（15:00 起的字串留給量測局）");
                Assert.IsFalse(tile4Touched, "暖機局不得動 4 號（量測局對手會去翻它）");
                for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上回到出生點的英雄

                // ── 量測局 ──
                AllocationProbe.Reset();
                GameObject rig = new GameObject("CaptureSanctuaryProbeRig");
                rig.AddComponent<AllocationProbeBegin>();
                rig.AddComponent<AllocationProbeEnd>();

                TapOpponent();
                Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "量測局應開局");
                SeedBoard(new[] { 12, 13, 14, 4 }, new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18 }); // V10-A06 主盤面
                _bootstrap.SeedCaptureSiegeForTest(BlueCode, 119.5f);
                for (int i = 0; i < 60; i++) yield return null; // 開局後 60 幀開始量測
                Assert.IsTrue(view.BlueInSanctuary, "量測局前提：英雄在出生點（13 號母板塊）應在聖所");

                int clockLabelsBefore = _bootstrap.CaptureClockLabelRecomputeCount;
                int sanctLabelsBefore = _bootstrap.CaptureSanctuaryLabelRecomputeCount;
                int scoreTicksBefore = _bootstrap.CaptureScoreTickCount;
                int percentBefore = view.BlueSanctuaryPercent;
                int previousPercent = percentBefore;
                int decreases = 0;

                AllocationProbe.Measuring = true;
                for (int i = 0; i < 480; i++)
                {
                    yield return null;
                    int p = view.BlueSanctuaryPercent;
                    if (p < previousPercent) decreases++;
                    previousPercent = p;
                }
                AllocationProbe.Measuring = false;
                Object.Destroy(rig);

                int clockLabels = _bootstrap.CaptureClockLabelRecomputeCount - clockLabelsBefore;
                int sanctLabels = _bootstrap.CaptureSanctuaryLabelRecomputeCount - sanctLabelsBefore;
                int scoreTicks = _bootstrap.CaptureScoreTickCount - scoreTicksBefore;
                Debug.Log("[V10-C05] frames=" + AllocationProbe.Frames + " update=" + AllocationProbe.UpdateBytes
                          + " late=" + AllocationProbe.LateUpdateBytes + " percent " + percentBefore + "→" + previousPercent
                          + " decreases=" + decreases + " clockLabels=" + clockLabels + " sanctLabels=" + sanctLabels
                          + " scoreTicks=" + scoreTicks + " clock=" + _bootstrap.CaptureClockLabel
                          + " row2=" + _bootstrap.CaptureSanctuaryLabel + " score=" + view.BlueScore + "/" + view.RedScore);
                Assert.GreaterOrEqual(AllocationProbe.Frames, 480);
                Assert.GreaterOrEqual(decreases, 5, "BlueSanctuaryPercent 減少次數（活性）");
                Assert.GreaterOrEqual(clockLabels, 7, "倒數字串重算（活性）");
                Assert.GreaterOrEqual(sanctLabels, 5, "第二列字串重算（活性）");
                Assert.GreaterOrEqual(scoreTicks, 7, "計分（活性）");
                Assert.AreEqual(0L, AllocationProbe.UpdateBytes,
                    "佔領對局 Update 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.UpdateBytes + " bytes");
                Assert.AreEqual(0L, AllocationProbe.LateUpdateBytes,
                    "佔領對局 LateUpdate 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.LateUpdateBytes + " bytes");
            }
            finally
            {
                AllocationProbe.Measuring = false;
                Time.captureDeltaTime = 0f;
            }
        }
    }
}
