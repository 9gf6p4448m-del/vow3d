using System.Collections;
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
    // v0.10.0 步驟 B 的驗收（V0100_SANCTUARY_PLAN.md §3-B，凍結）：V10-B01、B03～B10。
    //   V10-B02＝§2.6 以外全部既有 PlayMode／EditMode 測試 0 失敗（全套回歸，不另寫）；
    //   V10-B11＝v0.9.0 V9-B20 逐字保留（Capture19PlayTests.V9_B20_…，不另寫）。
    // captureDeltaTime＝1/60（TearDown 還原）；「幀」＝一次 yield return null；「開局」＝真實點 CAPTURE 再真實點對手。
    // 期望值一律寫字面值，不讀 CaptureTuning／CaptureBoardSpec／HexBoardLayout；點世界座標前斷言投影離四邊都 ≥ 16px。
    public sealed class CaptureSanctuaryPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float TapMarginPixels = 16f;   // §3 共同遵守：點擊前投影離四邊都 ≥ 16px，不成立就判紅
        private const int TileCount = 19;
        private const int BlueCode = 0, RedCode = 1, NeutralCode = 2; // CaptureMatchLogic 陣營代碼（字面值）

        // 用到的塔心（v0.9.0 E3 字面值）。
        private static readonly Vector3 Tower1 = new Vector3(0f, 0f, 7.578125f);
        private static readonly Vector3 Tower7 = new Vector3(0f, 0f, 15.15625f);
        private static readonly Vector3 Tower13 = new Vector3(0f, 0f, -15.15625f);

        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;
        private HeroLocomotion _heroLocomotion;
        private HeroLocomotion _opponentLocomotion;
        private CaptureBoardView _board;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; }

        private IEnumerator Setup()
        {
            Time.captureDeltaTime = 1f / 60f;
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
            _opponentLocomotion = _opponent.GetComponent<HeroLocomotion>();
            _board = _bootstrap.CaptureBoard;
            Assert.IsNotNull(_board, "場景缺少 CaptureBoard");
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
        }

        // ───────────── 共用操作（全部走真實觸控路由）─────────────

        private void TapCaptureButton()
        {
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float x, out float y), "拿不到 CAPTURE 鈕的螢幕點");
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

        private void TapOpponent()
        {
            TapWorldStrict(_opponent.transform.position + Vector3.up, "對手");
        }

        private void TapWorldStrict(Vector3 world, string who)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            Assert.Greater(screen.z, 0f, who + "：點擊點在鏡頭後方");
            Assert.IsTrue(screen.x >= TapMarginPixels && screen.x <= Screen.width - TapMarginPixels
                          && screen.y >= TapMarginPixels && screen.y <= Screen.height - TapMarginPixels,
                who + "：點擊點投影 (" + screen.x + ", " + screen.y + ") 離畫面邊緣不到 16px（畫面 "
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

        // ═════════════════════════ V10-B01 規則集與 HUD 字串接線 ═════════════════════════
        [UnityTest]
        public IEnumerator V10_B01_TheShippedRulesetHasSanctuaries_AndTheHudShowsTheClockAndSanct()
        {
            yield return Setup();
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            yield return null;
            Assert.IsNull(_bootstrap.CaptureClockLabel, "Lobby 時倒數字串應為 null");
            Assert.IsNull(_bootstrap.CaptureSanctuaryLabel, "Lobby 時第二列字串應為 null");

            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "在 Lobby 點對手應開佔領局");
            yield return null; // 開局後第 1 幀
            ICaptureMatchView view = _bootstrap.CaptureView;
            Assert.IsTrue(view.BlueInSanctuary, "開局後 1 幀：英雄在 13 號母板塊上應在聖所");
            Assert.IsTrue(view.RedInSanctuary, "開局後 1 幀：對手在 7 號母板塊上應在聖所");
            Assert.AreEqual("15:00", _bootstrap.CaptureClockLabel, "開局後 1 幀的倒數");
            Assert.AreEqual("SANCT 15%", _bootstrap.CaptureSanctuaryLabel, "開局後 1 幀的第二列");

            for (int f = 2; f <= 90; f++) yield return null; // 開局後第 90 幀（1.5 秒）
            Assert.AreEqual("14:59", _bootstrap.CaptureClockLabel, "開局後 90 幀（1.5 秒）的倒數");
        }

        // ═════════════════════════ V10-B03 英雄聖所（測試入口，A／B）═════════════════════════
        [UnityTest]
        public IEnumerator V10_B03_TheHeroTakesEightyFivePercentInItsSanctuary_ThroughTheTestEntry()
        {
            // A 組：開局後等 1 幀（第一個 Active tick 已判定在聖所），英雄在出生點（13 號母板塊）挨 20 → 83。
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            Assert.IsTrue(_bootstrap.CaptureView.BlueInSanctuary, "A 組前提：英雄應在聖所");
            _hero.TakeDuelDamage(20f);
            Assert.AreEqual(83f, _hero.Health, 1e-4f, "A 組：聖所內挨 20 應剩 83");

            // 對照組：英雄傳送到 4 號板塊 (0,0,−9.578125)，等 1 幀 → 不在聖所，挨 20 → 80。
            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(0f, 0f, -9.578125f));
            yield return null;
            Assert.IsFalse(_bootstrap.CaptureView.BlueInSanctuary, "對照組前提：4 號板塊不是聖所");
            _hero.TakeDuelDamage(20f);
            Assert.AreEqual(80f, _hero.Health, 1e-4f, "對照組：4 號板塊上挨 20 應剩 80");

            // 開局幀：開局同一個協程步（還沒有 Active tick）挨 20 → 80（E5／Q13）。
            yield return Setup();
            EnterLobbyAndStart();
            _hero.TakeDuelDamage(20f);
            Assert.AreEqual(80f, _hero.Health, 1e-4f, "開局幀（第一個 Active tick 之前）挨 20 應剩 80");
        }

        // ═════════════════════════ V10-B04 對手聖所（測試入口，A／B）═════════════════════════
        [UnityTest]
        public IEnumerator V10_B04_TheOpponentTakesEightyFivePercentInItsSanctuary_AndTheDamageEventCarriesIt()
        {
            float received = -1f;
            int events = 0;
            System.Action<float> onDamaged = amount => { received = amount; events++; };

            // A 組：開局後等 1 幀，對手在出生點（7 號母板塊）挨 60 → 249，同一次 OnDamaged 收到 51。
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            Assert.IsTrue(_bootstrap.CaptureView.RedInSanctuary, "A 組前提：對手應在聖所");
            _opponent.OnDamaged += onDamaged;
            _opponent.ReceiveDamage(60f, DamageType.Physical, _hero.gameObject);
            _opponent.OnDamaged -= onDamaged;
            Assert.AreEqual(249f, _opponent.Health, 1e-4f, "A 組：聖所內挨 60 應剩 249");
            Assert.AreEqual(1, events, "A 組：OnDamaged 應恰好送出一次");
            Assert.AreEqual(51f, received, 1e-4f, "A 組：OnDamaged（頭頂飄字）應收到 51");

            // 對照組：對手傳送到塔心 1，等 1 幀 → 240。
            received = -1f;
            events = 0;
            yield return Setup();
            EnterLobbyAndStart();
            _opponentLocomotion.WarpTo(Tower1);
            yield return null;
            Assert.IsFalse(_bootstrap.CaptureView.RedInSanctuary, "對照組前提：1 號不是紅方母板塊");
            _opponent.OnDamaged += onDamaged;
            _opponent.ReceiveDamage(60f, DamageType.Physical, _hero.gameObject);
            _opponent.OnDamaged -= onDamaged;
            Assert.AreEqual(240f, _opponent.Health, 1e-4f, "對照組：塔心 1 挨 60 應剩 240");
            Assert.AreEqual(1, events, "對照組：OnDamaged 應恰好送出一次");
            Assert.AreEqual(60f, received, 1e-4f, "對照組：OnDamaged 應收到 60");
        }

        // ═════════════════════════ V10-B05 真實出手（A／B）═════════════════════════
        [UnityTest]
        public IEnumerator V10_B05_RealStrikesAreReducedInTheSanctuary_BothWays()
        {
            // (a) 開局後等 1 幀，對手傳送到 (0,0,−14.65625)（距英雄 2.0m）→ 120 幀內英雄第一次掉血，掉完 83。
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            _opponentLocomotion.WarpTo(new Vector3(0f, 0f, -14.65625f));
            int f = 0;
            while (_hero.Health >= 100f && f < 120)
            {
                yield return null;
                f++;
            }
            Debug.Log("[SANCT] V10-B05(a) first hero hit at frame " + f + " hp=" + _hero.Health);
            Assert.Less(_hero.Health, 100f, "(a) 120 幀內英雄應第一次掉血");
            Assert.GreaterOrEqual(_opponent.AttacksResolved, 1, "(a) 掉血應來自對手的真實出手（活性）");
            Assert.AreEqual(83f, _hero.Health, 1e-4f, "(a) 聖所內被真實出手打中一次應剩 83");

            // (b) 對照：開局後種子紅 {7,8,18,1,0,4,13}、藍 {12,14}（13 號不在藍方手上），其餘同 (a) → 第一次掉血後 80。
            yield return Setup();
            EnterLobbyAndStart();
            SeedBoard(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4, 13 });
            yield return null;
            Assert.IsFalse(_bootstrap.CaptureView.BlueInSanctuary, "(b) 前提：13 號是紅的，英雄不在聖所");
            _opponentLocomotion.WarpTo(new Vector3(0f, 0f, -14.65625f));
            f = 0;
            while (_hero.Health >= 100f && f < 120)
            {
                yield return null;
                f++;
            }
            Debug.Log("[SANCT] V10-B05(b) first hero hit at frame " + f + " hp=" + _hero.Health);
            Assert.Less(_hero.Health, 100f, "(b) 120 幀內英雄應第一次掉血");
            Assert.AreEqual(80f, _hero.Health, 1e-4f, "(b) 不在聖所被真實出手打中一次應剩 80");

            // (c) 英雄出手：英雄傳送到 (0,0,13.65625)（對手在出生點，距 3m），等 30 幀（鏡頭跟上）再真實點對手 → d1；
            //     對照：對手傳送到塔心 1、英雄傳送到 (0,0,4.578125)（塔心 1 往南 3m，不在任何光圈內），同樣等 30 幀再點 → d2。
            yield return MeasureFirstHeroStrike(null, new Vector3(0f, 0f, 13.65625f));
            float d1 = _firstStrikeDamage;
            yield return MeasureFirstHeroStrike(Tower1, new Vector3(0f, 0f, 4.578125f));
            float d2 = _firstStrikeDamage;
            Debug.Log("[SANCT] V10-B05(c) d1=" + d1 + " d2=" + d2 + " ratio=" + (d2 != 0f ? d1 / d2 : float.NaN));
            Assert.Greater(d2, 0f, "(c) 對照組英雄應打中對手（d2 > 0）");
            Assert.GreaterOrEqual(d1 / d2, 0.849f, "(c) d1/d2");
            Assert.LessOrEqual(d1 / d2, 0.851f, "(c) d1/d2");
        }

        private float _firstStrikeDamage;

        // 開局 → （可選）對手傳送 → 英雄傳送 → 等 30 幀 → 真實點對手 → 600 幀內第一次命中，記 300−Health 到 _firstStrikeDamage。
        private IEnumerator MeasureFirstHeroStrike(Vector3? opponentAt, Vector3 heroAt)
        {
            _firstStrikeDamage = 0f;
            yield return Setup();
            EnterLobbyAndStart();
            if (opponentAt.HasValue) _opponentLocomotion.WarpTo(opponentAt.Value);
            _heroLocomotion.WarpTo(heroAt);
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭跟上傳送後的英雄
            Assert.IsTrue(_opponent.IsAlive, "(c) 點擊前對手應活著");
            TapOpponent();
            int g = 0;
            while (_opponent.Health >= 300f && g < 600)
            {
                yield return null;
                g++;
            }
            Debug.Log("[SANCT] V10-B05(c) first strike at frame " + g + " opponentHp=" + _opponent.Health
                      + " redInSanctuary=" + _bootstrap.CaptureView.RedInSanctuary + " opp=" + _opponent.transform.position);
            Assert.Less(_opponent.Health, 300f, "(c) 點對手後 600 幀內英雄應第一次命中");
            _firstStrikeDamage = 300f - _opponent.Health;
        }

        // ═════════════════════════ V10-B06 非 Active 一律 100 ═════════════════════════
        [UnityTest]
        public IEnumerator V10_B06_OutsideAnActiveMatch_DamageIsFull_IncludingTheDuelAfterwards()
        {
            yield return Setup();
            EnterLobbyAndStart();
            for (int i = 0; i < 60; i++) yield return null; // 英雄在聖所等 60 幀（寫入過 85）
            Assert.AreEqual(85, _hero.DamageTakenPercent, "前提：英雄在聖所時應已寫入 85（活性）");
            Assert.AreEqual(85, _opponent.DamageTakenPercent, "前提：對手在聖所時應已寫入 85（活性）");

            _bootstrap.SeedCaptureScoresForTest(999, 0); // 讓對局結束
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && f < 240)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "種子 (999,0) 後 240 幀內應結束");
            int k = 0;
            while (true)
            {
                // Ended 當幀起（含結算停頓每一幀與回 Lobby 那一幀）雙方都是 100。
                Assert.AreEqual(100, _hero.DamageTakenPercent, "Ended 後第 " + k + " 幀英雄的受傷百分比");
                Assert.AreEqual(100, _opponent.DamageTakenPercent, "Ended 後第 " + k + " 幀對手的受傷百分比");
                if (_bootstrap.CaptureState == CaptureMatchState.Lobby || k >= 186) break;
                yield return null;
                k++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "結算後 186 幀內應回 Lobby");

            TapCaptureButton(); // 回 Off
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.AreEqual(100, _hero.DamageTakenPercent, "回 Off 當下英雄的受傷百分比");
            Assert.AreEqual(100, _opponent.DamageTakenPercent, "回 Off 當下對手的受傷百分比");
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上回到出生點的英雄
            TapOpponent(); // 開單挑
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState, "Off 點對手應開單挑");
            int g = 0;
            while (_hero.Health >= 100f && g < 240)
            {
                yield return null;
                g++;
            }
            Debug.Log("[SANCT] V10-B06 duel hit at frame " + g + " hp=" + _hero.Health);
            Assert.Less(_hero.Health, 100f, "單挑中對手應在 240 幀內真實出手命中英雄");
            Assert.AreEqual(80f, _hero.Health, 1e-4f, "單挑命中應掉 20（不得被殘留的 85 縮小成 17）");
        }

        // ═════════════════════════ V10-B07 奪回接線與進度盤 ═════════════════════════
        [UnityTest]
        public IEnumerator V10_B07_ReclaimingAMotherTile_TakesOnePointEightSeconds_AndTheDiscFillsOverThatTime()
        {
            // 藍方：13 號在紅方手上，對手倒地，英雄傳送到塔心 13（f0）。
            yield return Setup();
            EnterLobbyAndStart();
            SeedBoard(new[] { 12, 14 }, new[] { 7, 8, 18, 1, 0, 4, 13 });
            _opponent.ReceiveDamage(1000f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(_opponent.IsAlive, "前提：對手應倒地");
            _heroLocomotion.WarpTo(Tower13); // f0
            bool radiusChecked = false;
            int flipFrame = -1;
            for (int f = 1; f <= 113; f++)
            {
                yield return null;
                if (f == 54)
                {
                    Renderer disc = _board.ProgressDisc(13);
                    Assert.IsTrue(disc.enabled, "f0＋54 幀：13 號進度盤應可見");
                    Debug.Log("[SANCT] V10-B07 disc radius at f0+54 = " + disc.bounds.extents.x);
                    Assert.AreEqual(1.25f, disc.bounds.extents.x, 0.05f, "f0＋54 幀（0.9 秒）13 號進度盤半徑");
                    radiusChecked = true;
                }
                if (f == 102) Assert.AreEqual(Faction.RedTeam, Owner(13), "f0＋102 幀時 13 號應仍紅");
                if (flipFrame < 0 && Owner(13) == Faction.BlueTeam) flipFrame = f;
            }
            Debug.Log("[SANCT] V10-B07 blue reclaim flipped at f0+" + flipFrame);
            Assert.IsTrue(radiusChecked);
            Assert.Greater(flipFrame, 0, "f0＋114 幀前 13 號應翻藍");

            // 紅方：7 號在藍方手上，英雄在 (−17,0,−17)，對手傳送到塔心 7（f0）。
            yield return Setup();
            EnterLobbyAndStart();
            SeedBoard(new[] { 12, 13, 14, 4, 0, 1, 7 }, new[] { 18, 8 });
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f));
            _opponentLocomotion.WarpTo(Tower7); // f0
            flipFrame = -1;
            for (int f = 1; f <= 113; f++)
            {
                yield return null;
                if (f == 102) Assert.AreEqual(Faction.BlueTeam, Owner(7), "f0＋102 幀時 7 號應仍藍");
                if (flipFrame < 0 && Owner(7) == Faction.RedTeam) flipFrame = f;
            }
            Debug.Log("[SANCT] V10-B07 red reclaim flipped at f0+" + flipFrame);
            Assert.Greater(flipFrame, 0, "f0＋114 幀前 7 號應翻紅");
        }

        // ═════════════════════════ V10-B08 圍城接線與 HUD ═════════════════════════
        [UnityTest]
        public IEnumerator V10_B08_UnderSiege_TheSanctuaryDecaysAPercentASecond_OnTheHudAndInTheDamage()
        {
            yield return Setup();
            EnterLobbyAndStart();
            // V10-A06 主盤面：藍 {12,13,14,4}、紅＝其餘去掉 11 號共 14 塊。英雄留在出生點。
            SeedBoard(new[] { 12, 13, 14, 4 }, new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 15, 16, 17, 18 });
            _opponent.ReceiveDamage(1000f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(_opponent.IsAlive, "前提：對手應倒地");
            _bootstrap.SeedCaptureSiegeForTest(BlueCode, 119.5f); // f0
            ICaptureMatchView view = _bootstrap.CaptureView;
            int knockouts = 1;
            int fourteenFrame = -1;
            int zeroFrame = -1;
            for (int f = 1; f <= 935; f++)
            {
                yield return null;
                if (_opponent.IsAlive)
                {
                    // 每當它復活就立刻再倒地（讓紅方不再翻塊，圍城不中斷）。
                    _opponent.ReceiveDamage(1000f, DamageType.Physical, _hero.gameObject);
                    knockouts++;
                }
                int percent = view.BlueSanctuaryPercent;
                if (f == 24)
                {
                    Assert.AreEqual(15, percent, "f0＋24 幀聖所強度");
                    Assert.AreEqual("SANCT 15%", _bootstrap.CaptureSanctuaryLabel, "f0＋24 幀第二列");
                    _hero.TakeDuelDamage(20f);
                    Assert.AreEqual(83f, _hero.Health, 1e-4f, "f0＋24 幀聖所 15% 時挨 20 應剩 83");
                }
                if (f == 84) Assert.AreEqual(15, percent, "f0＋84 幀聖所強度應仍 15");
                if (fourteenFrame < 0 && percent == 14)
                {
                    fourteenFrame = f;
                    Assert.AreEqual("SIEGE 14%", _bootstrap.CaptureSanctuaryLabel, "強度變 14 的同一幀第二列");
                }
                if (f == 924) Assert.GreaterOrEqual(percent, 1, "f0＋924 幀聖所強度應仍 ≥ 1");
                if (zeroFrame < 0 && percent == 0)
                {
                    zeroFrame = f;
                    Assert.AreEqual("SIEGE 0%", _bootstrap.CaptureSanctuaryLabel, "強度歸 0 的同一幀第二列");
                    _hero.TakeDuelDamage(20f);
                    Assert.AreEqual(63f, _hero.Health, 1e-4f, "聖所強度 0 時挨 20 應剩 63（83−20）");
                    break;
                }
            }
            Debug.Log("[SANCT] V10-B08 14% at f0+" + fourteenFrame + " 0% at f0+" + zeroFrame + " knockouts=" + knockouts);
            Assert.Greater(fourteenFrame, 84, "強度變 14 應在 f0＋84 幀之後");
            Assert.Less(fourteenFrame, 96, "f0＋96 幀前強度應變 14");
            Assert.Greater(zeroFrame, 924, "強度歸 0 應在 f0＋924 幀之後");
            Assert.Less(zeroFrame, 936, "f0＋936 幀前強度應歸 0");
            Assert.AreEqual(CaptureMatchState.Active, view.State, "全程仍在對局中（活性）");
        }

        // ═════════════════════════ V10-B09 倒數與時間到 ═════════════════════════
        [UnityTest]
        public IEnumerator V10_B09_WhenTheClockRunsOut_TheHigherScoreWins_AndTheNextMatchStartsAtFifteenMinutes()
        {
            // 藍勝組：種子 (10,5)、已過 898 秒（f0）。
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(10, 5);
            _bootstrap.SeedCaptureMatchElapsedForTest(898f); // f0
            yield return null;
            Assert.AreEqual("0:02", _bootstrap.CaptureClockLabel, "f0 的下一幀倒數");
            int f = 1;
            while (_bootstrap.CaptureState == CaptureMatchState.Active && f < 125)
            {
                yield return null;
                f++;
                if (f == 114) Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "f0＋114 幀時應仍 Active");
            }
            Debug.Log("[SANCT] V10-B09 ended by time at f0+" + f);
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "f0＋126 幀前應結束");
            Assert.Greater(f, 114, "時間到太早：f0＋" + f);
            ICaptureMatchView view = _bootstrap.CaptureView;
            Assert.IsTrue(view.EndedByTime, "應是時間到結束");
            Assert.AreEqual(CaptureMatchResult.BlueWins, view.Result);
            Assert.AreEqual("BLUE WINS", _bootstrap.MatchStatusLabel);
            Assert.AreEqual("0:00", _bootstrap.CaptureClockLabel, "時間到那一幀倒數");

            int ended = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && ended < 186)
            {
                yield return null;
                ended++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "結算 186 幀內應回 Lobby");
            Assert.GreaterOrEqual(ended, 174, "結算停頓太短：" + ended + " 幀");
            Assert.AreEqual("LAST: BLUE WINS", _bootstrap.MatchStatusLabel);
            Assert.IsNull(_bootstrap.CaptureClockLabel, "回 Lobby 後倒數字串應為 null");

            // 第二局：點對手 → 下一幀倒數 15:00、SANCT 15%。
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上回到出生點的英雄
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "第二局應開局");
            yield return null;
            Assert.AreEqual("15:00", _bootstrap.CaptureClockLabel, "第二局下一幀的倒數");
            Assert.AreEqual("SANCT 15%", _bootstrap.CaptureSanctuaryLabel, "第二局下一幀的第二列");

            // 平手組：種子 (10,10) → 時間到 DRAW。
            yield return Setup();
            EnterLobbyAndStart();
            _bootstrap.SeedCaptureScoresForTest(10, 10);
            _bootstrap.SeedCaptureMatchElapsedForTest(898f); // f0
            f = 0;
            while (_bootstrap.CaptureState == CaptureMatchState.Active && f < 125)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "平手組 f0＋126 幀前應結束");
            Assert.IsTrue(_bootstrap.CaptureView.EndedByTime, "平手組應是時間到結束");
            Assert.AreEqual(CaptureMatchResult.Draw, _bootstrap.CaptureView.Result);
            Assert.AreEqual("DRAW", _bootstrap.MatchStatusLabel);
        }

        // ═════════════════════════ V10-B10 慢計分接線 ═════════════════════════
        [UnityTest]
        public IEnumerator V10_B10_ScoringPaysOneSeventhPointPerTilePerSecond()
        {
            yield return Setup();
            EnterLobbyAndStart();
            for (int f = 1; f <= 630; f++) yield return null; // 開局後不輸入，第 630 幀（10.5 秒）
            ICaptureMatchView view = _bootstrap.CaptureView;
            Debug.Log("[SANCT] V10-B10 at frame 630: blue=" + view.BlueScore + " red=" + view.RedScore
                      + " scoreTicks=" + _bootstrap.CaptureScoreTickCount + " owner1=" + Owner(1));
            Assert.AreEqual(10, _bootstrap.CaptureScoreTickCount, "10.5 秒內應已計分 10 次（活性）");
            Assert.AreEqual(4, view.BlueScore, "藍方 3 塊 × 10 次＝30 單位＝4 分");
            Assert.GreaterOrEqual(view.RedScore, 4, "紅方 30～38 單位＝4 或 5 分");
            Assert.LessOrEqual(view.RedScore, 5, "紅方 30～38 單位＝4 或 5 分");
        }
    }
}
