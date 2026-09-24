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
    // v0.8.0 步驟 B 的驗收（V080_CAPTURE_PLAN.md §3-B，V-B01～V-B17，2026-09-24 凍結）。
    // captureDeltaTime＝1/60；「幀」＝一次 yield return null。期望值一律寫字面值，不讀 CaptureTuning／HexBoardLayout。
    public sealed class CapturePlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float PosTolerance = 0.05f;
        private const float EdgeMarginPixels = 8f; // GestureMath.EdgeDeadzonePixels：比這更靠邊的點擊會被路由丟掉

        // E3 的 7 個塔心（字面值）。
        private static readonly Vector2[] TowerCenters =
        {
            new Vector2(0f, 0f), new Vector2(0f, 12.125f), new Vector2(10.5f, 6.0625f), new Vector2(10.5f, -6.0625f),
            new Vector2(0f, -12.125f), new Vector2(-10.5f, -6.0625f), new Vector2(-10.5f, 6.0625f)
        };

        // 開局歸屬（E5）：索引 0～6＝中立、紅、中立、中立、藍、中立、中立。
        private static readonly Faction[] OpeningOwners =
        {
            Faction.Neutral, Faction.RedTeam, Faction.Neutral, Faction.Neutral, Faction.BlueTeam, Faction.Neutral, Faction.Neutral
        };

        private static bool _screenLogged;

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
            if (!_screenLogged)
            {
                _screenLogged = true;
                Debug.Log("[CAPTURE-TEST] Screen " + Screen.width + "x" + Screen.height + " dpi " + Screen.dpi);
            }
        }

        // ───────────── 共用操作（全部走真實觸控路由）─────────────

        private void TapCaptureButton()
        {
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float x, out float y), "拿不到 CAPTURE 鈕的螢幕點");
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

        private void TapOpponent()
        {
            TapWorld(_opponent.transform.position + Vector3.up, "對手");
        }

        private void TapWorld(Vector3 world, string who)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            Assert.Greater(screen.z, 0f, who + " 在鏡頭後方");
            Assert.IsTrue(screen.x >= EdgeMarginPixels && screen.x <= Screen.width - EdgeMarginPixels
                          && screen.y >= EdgeMarginPixels && screen.y <= Screen.height - EdgeMarginPixels,
                who + " 的螢幕投影 (" + screen.x + ", " + screen.y + ") 不在可點範圍內（畫面 "
                + Screen.width + "x" + Screen.height + "）");
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
        }

        private void TapRuneButton()
        {
            Assert.IsTrue(_bootstrap.TryGetRuneButtonScreenPoint(out float x, out float y));
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

        // Off → 真實點 CAPTURE → Lobby → 真實點對手 → Active。
        private void EnterLobbyAndStart()
        {
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "在 Lobby 點對手應開佔領局");
        }

        private Faction Owner(int tile) { return _bootstrap.CaptureView.OwnerOf(tile); }

        private Faction[] SnapshotOwners()
        {
            Faction[] owners = new Faction[7];
            for (int i = 0; i < 7; i++) owners[i] = Owner(i);
            return owners;
        }

        private static float PlanarDistance(Vector3 a, float x, float z)
        {
            float dx = a.x - x;
            float dz = a.z - z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void AssertXz(Vector3 actual, float x, float z, string message)
        {
            Assert.AreEqual(x, actual.x, PosTolerance, message + "（x）實際=" + actual);
            Assert.AreEqual(z, actual.z, PosTolerance, message + "（z）實際=" + actual);
        }

        private static void AssertBody(GameObject root, bool expectedEnabled, string who)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            Assert.Greater(renderers.Length + colliders.Length, 0, who + " 身上沒有任何 Renderer／Collider（活性）");
            for (int i = 0; i < renderers.Length; i++)
                Assert.AreEqual(expectedEnabled, renderers[i].enabled, who + " 的 Renderer " + renderers[i].name);
            for (int i = 0; i < colliders.Length; i++)
                Assert.AreEqual(expectedEnabled, colliders[i].enabled, who + " 的 Collider " + colliders[i].name);
        }

        private int ActivePlayerWalls()
        {
            RuneWall[] pool = Object.FindObjectOfType<RuneCaster>().Pool;
            int alive = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) alive++;
            return alive;
        }

        // ───────────── V-B01 場景物件 ─────────────
        [UnityTest]
        public IEnumerator V_B01_BoardObjects_AreSevenOfEach_NoColliders_OffUntilCapture_AndKeepTheGridClear()
        {
            yield return Setup();

            Assert.IsFalse(_board.gameObject.activeInHierarchy, "Off 時 CaptureBoard 整組不得啟用");
            Assert.AreEqual(7, _board.FloorCount);
            Assert.AreEqual(7, _board.TowerCount);
            Assert.AreEqual(7, _board.RingCount);
            Assert.AreEqual(7, _board.ProgressDiscCount);
            for (int i = 0; i < 7; i++)
            {
                Assert.IsNotNull(_board.Floor(i));
                Assert.IsNotNull(_board.Tower(i));
                Assert.IsNotNull(_board.Ring(i));
                Assert.IsNotNull(_board.ProgressDisc(i));
            }
            Assert.AreEqual(28, _board.GetComponentsInChildren<Renderer>(true).Length, "CaptureBoard 底下恰好 7×4 個顯示物件");
            Assert.AreEqual(0, _board.GetComponentsInChildren<Collider>(true).Length, "板塊／塔／光圈／進度盤不得帶 Collider");
            Assert.AreEqual(0, _board.GetComponentsInChildren<CombatTargetBehaviour>(true).Length);

            for (int i = 0; i < 7; i++)
            {
                Vector3 tower = _board.Tower(i).transform.position;
                Assert.AreEqual(TowerCenters[i].x, tower.x, 0.001f, "塔 " + i + " 的 x");
                Assert.AreEqual(TowerCenters[i].y, tower.z, 0.001f, "塔 " + i + " 的 z");
            }

            int blockedBefore = _bootstrap.NavGrid.BlockedCount;
            TapCaptureButton();
            yield return null;
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Assert.IsTrue(_board.gameObject.activeInHierarchy, "Lobby 時 CaptureBoard 應啟用");
            Assert.AreEqual(blockedBefore, _bootstrap.NavGrid.BlockedCount, "進入 Lobby 前後被擋格數應相同");

            BlockGrid grid = _bootstrap.NavGrid;
            for (int t = 0; t < 7; t++)
            {
                int checkedCells = 0;
                for (int cz = 0; cz < grid.Rows; cz++)
                {
                    for (int cx = 0; cx < grid.Columns; cx++)
                    {
                        grid.CellCenter(cx, cz, out float x, out float z);
                        float dx = x - TowerCenters[t].x;
                        float dz = z - TowerCenters[t].y;
                        if (dx * dx + dz * dz > 2.5f * 2.5f) continue;
                        checkedCells++;
                        Assert.IsFalse(grid.IsBlocked(cx, cz), "塔 " + t + " 光圈內的格 (" + x + ", " + z + ") 被擋");
                    }
                }
                Assert.Greater(checkedCells, 0, "塔 " + t + " 光圈內一格都沒檢查到（活性）");
            }
        }

        // ───────────── V-B02 不按 CAPTURE＝v0.7.0 ─────────────
        [UnityTest]
        public IEnumerator V_B02_WithoutCapture_TapOpponentIsTheV070Duel_AndKnockoutResetsInTwoPointFiveSeconds()
        {
            yield return Setup();
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.AreEqual(1, _bootstrap.DuelStartCount);
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.IsFalse(_board.gameObject.activeInHierarchy);

            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            int frames = 0;
            while (_bootstrap.DuelState != DuelRoundState.Dormant && frames < 156)
            {
                yield return null;
                frames++;
            }
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState, "156 幀內應回單挑待機");
            Assert.GreaterOrEqual(frames, 150, "回單挑待機太早：" + frames + " 幀");
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            Assert.IsFalse(_opponent.IsBodyHidden, "單挑 KO 不得走佔領的倒地隱藏");
            Assert.AreEqual(0, _bootstrap.CaptureRespawnCount);
        }

        // ───────────── V-B03 CAPTURE 鈕走真實觸控 ─────────────
        [UnityTest]
        public IEnumerator V_B03_CaptureButton_TogglesThroughRealTouch_WithoutLeaking_AndIsInertDuringADuel()
        {
            yield return Setup();
            int moves = 0;
            _bootstrap.InputService.OnMoveDestinationSelected += _ => moves++;

            Vector3 before = _hero.transform.position;
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Assert.IsTrue(_board.gameObject.activeInHierarchy);
            Assert.AreEqual("CAPTURE: ON", _bootstrap.CaptureButtonLabel);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.Less(Vector3.Distance(before, _hero.transform.position), 0.01f, "點 CAPTURE 漏成移動（第 " + (i + 1) + " 幀）");
                Assert.AreEqual(PlayerState.Idle, _hero.StateMachine.CurrentState);
            }

            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.IsFalse(_board.gameObject.activeInHierarchy);
            Assert.AreEqual("CAPTURE", _bootstrap.CaptureButtonLabel);

            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            before = _hero.transform.position;
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState, "單挑中按 CAPTURE 不得切換模式");
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.IsFalse(_board.gameObject.activeInHierarchy);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.Less(Vector3.Distance(before, _hero.transform.position), 0.01f, "單挑中點 CAPTURE 漏成移動");
            Assert.IsNull(_hero.CurrentTarget);
            Assert.AreEqual(0, moves, "點 CAPTURE 不得滲透成移動指令");
        }

        // ───────────── V-B04 佔領開局 ─────────────
        [UnityTest]
        public IEnumerator V_B04_TappingTheOpponentInLobby_StartsCaptureWithTeleportAndTheV070Clear()
        {
            yield return Setup();
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);

            _bootstrap.PressElementWaterButton();
            Assert.GreaterOrEqual(_bootstrap.ElementField.ActiveZoneCount, 1, "開局前應先有一個水區（活性）");
            TestTurret turret = _bootstrap.Turret;
            Projectile[] bullets = Object.FindObjectsOfType<Projectile>();
            turret.SetFiring(true);
            yield return null;
            int flying = 0;
            for (int i = 0; i < bullets.Length; i++) if (bullets[i].IsActive) flying++;
            Assert.GreaterOrEqual(flying, 1, "開局前應先有一發子彈在路上（活性）");

            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(1, _bootstrap.CaptureStartCount);
            Assert.AreEqual(0, _bootstrap.DuelStartCount, "點對手不得開成單挑");
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.IsNull(_hero.CurrentTarget, "開局點擊不得同時送出普攻");
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            AssertXz(_hero.transform.position, 0f, -13.625f, "英雄開局應在藍方基地復活點");
            AssertXz(_opponent.transform.position, 0f, 13.625f, "對手開局應在紅方基地復活點");
            for (int i = 0; i < 7; i++) Assert.AreEqual(OpeningOwners[i], Owner(i), "開局歸屬 " + i);
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount, "開局應清掉元素區域");
            Assert.IsFalse(turret.IsFiring);
            for (int i = 0; i < bullets.Length; i++) Assert.IsFalse(bullets[i].IsActive, "池內子彈 " + i + " 仍作用中");
        }

        // ───────────── V-B05 真實點地引導 ─────────────
        [UnityTest]
        public IEnumerator V_B05_ARealGroundTapOnTowerFive_WalksThereAndFlipsItBlueInTime()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(_opponent.IsAlive);
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上傳送後的英雄，投影才是當幀位置

            Faction[] before = SnapshotOwners();
            TapWorld(new Vector3(-10.5f, 0f, -6.0625f), "5 號塔心地面投影點");
            bool sawProgress = false;
            int flipFrame = -1;
            for (int f = 1; f <= 420; f++)
            {
                yield return null;
                ICaptureMatchView view = _bootstrap.CaptureView;
                if (Owner(5) == Faction.BlueTeam)
                {
                    flipFrame = f;
                    break;
                }
                if (view.BlueChannelingTile == 5 && view.BlueChannelProgress > 0f) sawProgress = true;
            }
            Assert.Greater(flipFrame, 0, "點擊後 420 幀內 5 號沒有翻藍");
            Assert.GreaterOrEqual(flipFrame, 270, "5 號翻藍太早：" + flipFrame);
            Assert.IsTrue(sawProgress, "翻藍前應至少有一幀藍方引導進度 > 0");
            for (int i = 0; i < 7; i++)
                if (i != 5) Assert.AreEqual(before[i], Owner(i), "其他塊 " + i + " 不應變動");
        }

        // ───────────── V-B06 引導時間接線 ─────────────
        [UnityTest]
        public IEnumerator V_B06_StandingOnTowerZero_FlipsItBetweenThreePointFourAndThreePointSixSeconds()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            yield return null;

            _heroLocomotion.WarpTo(new Vector3(0f, 0f, 0f)); // f0
            for (int f = 1; f <= 204; f++) yield return null;
            Assert.AreEqual(Faction.Neutral, Owner(0), "f0＋204 幀（3.4 秒）時 0 號應仍中立");
            int f2 = 204;
            while (Owner(0) != Faction.BlueTeam && f2 < 216)
            {
                yield return null;
                f2++;
            }
            Assert.AreEqual(Faction.BlueTeam, Owner(0), "f0＋216 幀（3.6 秒）前 0 號應翻藍");
        }

        // ───────────── V-B07 受傷打斷接線（含護盾全額吸收）─────────────
        [UnityTest]
        public IEnumerator V_B07_HeroDamage_EvenFullyAbsorbedByShield_RestartsTheChannel()
        {
            for (int group = 0; group < 2; group++)
            {
                bool shieldGroup = group == 1;
                yield return Setup();
                EnterLobbyAndStart();
                _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
                yield return null;

                _heroLocomotion.WarpTo(new Vector3(0f, 0f, 0f)); // f0
                for (int f = 1; f <= 120; f++) yield return null;
                Assert.Greater(_bootstrap.CaptureView.BlueChannelProgress, 0f, "受傷前應在引導（活性）");
                if (shieldGroup)
                {
                    _bootstrap.Shield.Grant();
                    float shieldBefore = _bootstrap.Shield.Amount;
                    Assert.GreaterOrEqual(shieldBefore, 20f);
                    _hero.TakeDuelDamage(20f);
                    Assert.AreEqual(100f, _hero.Health, 0.01f, "護盾組：HP 應維持 100");
                    Assert.AreEqual(shieldBefore - 20f, _bootstrap.Shield.Amount, 0.01f, "護盾組：護盾應 −20");
                }
                else
                {
                    _hero.TakeDuelDamage(20f);
                    Assert.AreEqual(80f, _hero.Health, 0.01f);
                }

                for (int f = 1; f <= 204; f++) yield return null;
                Assert.AreEqual(Faction.Neutral, Owner(0),
                    (shieldGroup ? "護盾組" : "扣血組") + "：f0＋120＋204 幀時 0 號應仍中立（受傷應重新起算）");
                int f2 = 204;
                while (Owner(0) != Faction.BlueTeam && f2 < 216)
                {
                    yield return null;
                    f2++;
                }
                Assert.AreEqual(Faction.BlueTeam, Owner(0), (shieldGroup ? "護盾組" : "扣血組") + "：f0＋120＋216 幀前應翻藍");
            }
        }

        // ───────────── V-B08 對手被打中也會打斷 ─────────────
        [UnityTest]
        public IEnumerator V_B08_OpponentDamage_RestartsItsChannel()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponentLocomotion.WarpTo(new Vector3(10.5f, 0f, 6.0625f));
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f)); // f0
            for (int f = 1; f <= 120; f++) yield return null;
            Assert.Greater(_bootstrap.CaptureView.RedChannelProgress, 0f, "受傷前對手應在引導 2 號（活性）");
            _opponent.ReceiveDamage(60f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(240f, _opponent.Health, 0.01f);

            for (int f = 1; f <= 204; f++) yield return null;
            Assert.AreEqual(Faction.Neutral, Owner(2), "f0＋120＋204 幀時 2 號應仍中立");
            int f2 = 204;
            while (Owner(2) != Faction.RedTeam && f2 < 216)
            {
                yield return null;
                f2++;
            }
            Assert.AreEqual(Faction.RedTeam, Owner(2), "f0＋120＋216 幀前 2 號應翻紅");
        }

        // ───────────── V-B09 爭奪接線 ─────────────
        [UnityTest]
        public IEnumerator V_B09_BothInsideTowerZero_NobodyFlipsIt()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(0f, 0f, 0f));
            _opponentLocomotion.WarpTo(new Vector3(2f, 0f, 0f));
            int contestBefore = _bootstrap.CaptureContestTickCount;
            for (int f = 1; f <= 240; f++)
            {
                yield return null;
                Assert.AreEqual(Faction.Neutral, Owner(0), "雙方都在 0 號光圈內，第 " + f + " 幀有人翻塊");
            }
            Assert.GreaterOrEqual(_bootstrap.CaptureContestTickCount - contestBefore, 120, "爭奪 tick 數（活性）");
            Assert.IsTrue(_opponent.IsAlive);
        }

        // ───────────── V-B10 對手自己搶點 ─────────────
        [UnityTest]
        public IEnumerator V_B10_TheOpponentGoesForTowerTwo_AndFlipsItFirst()
        {
            yield return Setup();
            EnterLobbyAndStart();
            Faction[] opening = SnapshotOwners();
            bool sawWindup = false;
            int firstChangedTile = -1;
            int firstChangedFrame = -1;
            for (int f = 1; f <= 480 && firstChangedTile < 0; f++)
            {
                yield return null;
                if (_opponent.IsWarning) sawWindup = true;
                if (f == 60) Assert.AreEqual(2, _opponent.CaptureTargetTile, "第 60 幀時對手的目標塔應為 2");
                for (int i = 0; i < 7; i++)
                {
                    if (Owner(i) == opening[i]) continue;
                    firstChangedTile = i;
                    firstChangedFrame = f;
                    break;
                }
            }
            Assert.AreEqual(2, firstChangedTile, "第一塊變色的應是 2 號");
            Assert.AreEqual(Faction.RedTeam, Owner(2));
            Assert.GreaterOrEqual(firstChangedFrame, 300, "2 號翻紅太早：" + firstChangedFrame);
            Assert.LessOrEqual(firstChangedFrame, 480);
            Assert.IsFalse(sawWindup, "英雄不做任何輸入時，對手不應進入前搖");
        }

        // ───────────── V-B11 轉頭追打與放棄 ─────────────
        [UnityTest]
        public IEnumerator V_B11_TheOpponentTurnsOnANearbyHero_FinishesItsStrike_ThenGivesUp()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponentLocomotion.WarpTo(new Vector3(10.5f, 0f, 6.0625f));
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f));
            for (int f = 0; f < 30; f++) yield return null;
            Assert.IsFalse(_opponent.IsChasingHero);

            _heroLocomotion.WarpTo(new Vector3(15.5f, 0f, 6.0625f));
            int f1 = 0;
            while (!_opponent.IsChasingHero && f1 < 6)
            {
                yield return null;
                f1++;
            }
            Assert.IsTrue(_opponent.IsChasingHero, "英雄進入 5.0m，6 幀內對手應轉頭追打");
            int f2 = f1;
            while (!_opponent.IsWarning && f2 < 120)
            {
                yield return null;
                f2++;
            }
            Assert.IsTrue(_opponent.IsWarning, "120 幀內對手應進入前搖");

            int attacksBefore = _opponent.AttacksResolved;
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f));
            int f3 = 0;
            while (_opponent.IsChasingHero && f3 < 108)
            {
                yield return null;
                f3++;
            }
            Assert.IsFalse(_opponent.IsChasingHero, "英雄遠離後 108 幀內對手應放棄追打（f3=" + f3 + "）");
            Assert.GreaterOrEqual(_opponent.CaptureTargetTile, 0, "放棄後應回去搶塔");
            Assert.AreEqual(attacksBefore + 1, _opponent.AttacksResolved, "正在進行的前搖應照樣結算");
        }

        // ───────────── V-B12 倒地與復活 ─────────────
        [UnityTest]
        public IEnumerator V_B12_KnockoutsHideTheBody_AndRespawnAtTheRightPointInFiveSeconds()
        {
            // (a) 英雄倒地 → 隱藏 → 5 秒後回藍方基地
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            int blueAtKo = _bootstrap.CaptureView.BlueScore;
            int redAtKo = _bootstrap.CaptureView.RedScore;
            _hero.TakeDuelDamage(100f); // f0
            Assert.IsFalse(_hero.IsAlive);
            yield return null;
            AssertBody(_hero.gameObject, false, "倒地的英雄");
            Faction[] lastBefore = SnapshotOwners();
            int f = 1;
            while (!_hero.IsAlive && f < 306)
            {
                lastBefore = SnapshotOwners();
                yield return null;
                f++;
                if (f == 294) Assert.IsFalse(_hero.IsAlive, "f0＋294 幀時不應已復活");
            }
            Assert.IsTrue(_hero.IsAlive, "f0＋306 幀前英雄應復活");
            Assert.Greater(f, 294, "英雄復活太早：f0＋" + f);
            AssertXz(_hero.transform.position, 0f, -13.625f, "(a) 英雄應在藍方基地復活點");
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            AssertBody(_hero.gameObject, true, "復活的英雄");
            for (int i = 0; i < 7; i++) Assert.AreEqual(lastBefore[i], Owner(i), "(a) 復活前後歸屬 " + i);
            Assert.GreaterOrEqual(_bootstrap.CaptureView.BlueScore, blueAtKo);
            Assert.GreaterOrEqual(_bootstrap.CaptureView.RedScore, redAtKo);

            // (b) 英雄倒地後基地被搶 → 場邊復活
            yield return Setup();
            EnterLobbyAndStart();
            _hero.TakeDuelDamage(100f); // f0
            _opponentLocomotion.WarpTo(new Vector3(0f, 0f, -12.125f));
            f = 0;
            while (Owner(4) != Faction.RedTeam && f < 216)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(Faction.RedTeam, Owner(4), "(b) f0＋216 幀前 4 號應翻紅");
            while (!_hero.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(_hero.IsAlive, "(b) f0＋306 幀前英雄應復活");
            AssertXz(_hero.transform.position, -17f, -17f, "(b) 基地被搶，英雄應在場邊復活");

            // (c) 英雄趁對手倒地在對手復活點立牆 → 對手復活後被推出、不卡死
            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(4f, 0f, 13.625f));
            _hero.transform.rotation = Quaternion.Euler(0f, -90f, 0f); // 面向 −x：極速牆落在正前方 4m＝(0, 13.625)
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject); // f0
            for (f = 1; f <= 120; f++) yield return null;
            TapRuneButton(); // 英雄的符印施放入口（真實觸控 → 極速施放）
            RuneWall wall = null;
            RuneWall[] pool = Object.FindObjectOfType<RuneCaster>().Pool;
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) wall = pool[i];
            Assert.IsNotNull(wall, "(c) 符印施放入口沒有立出牆");
            AssertXz(wall.transform.position, 0f, 13.625f, "(c) 牆應立在對手的基地復活點上");
            f = 120;
            while (!_opponent.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(_opponent.IsAlive, "(c) f0＋306 幀前對手應復活");
            Assert.IsTrue(wall.IsAlive, "(c) 復活那一幀牆應仍作用中（活性）");
            Assert.Greater(wall.RemainingLifespan, 0f);
            for (int k = 0; k <= 3; k++)
            {
                Vector3 p = _opponent.transform.position;
                Assert.IsFalse(BlockGrid.CircleOverlapsBox(p.x, p.z, 0.35f, 0f, 13.625f, -1f, 0f, 2f, 0.3f),
                    "(c) 復活後第 " + k + " 幀對手身體仍與牆相交：" + p);
                if (k < 3) yield return null;
            }
            Vector3 afterRespawn = _opponent.transform.position;
            for (int k = 0; k < 120; k++) yield return null;
            Assert.Greater(Vector3.Distance(afterRespawn, _opponent.transform.position), 0.5f, "(c) 對手復活後卡死");

            // (d) 對手版本：回紅方基地、HP 300；基地被翻就去場邊
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject); // f0
            yield return null;
            AssertBody(_opponent.gameObject, false, "倒地的對手");
            f = 1;
            while (!_opponent.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(_opponent.IsAlive, "(d) f0＋306 幀前對手應復活");
            Assert.Greater(f, 294, "(d) 對手復活太早");
            AssertXz(_opponent.transform.position, 0f, 13.625f, "(d) 對手應在紅方基地復活點");
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            AssertBody(_opponent.gameObject, true, "復活的對手");

            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject); // f0
            _heroLocomotion.WarpTo(new Vector3(0f, 0f, 12.125f));
            f = 0;
            while (!_opponent.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(Faction.BlueTeam, Owner(1), "(d) 英雄站在 1 號塔心，對手復活前 1 號應已翻藍");
            Assert.IsTrue(_opponent.IsAlive, "(d) f0＋306 幀前對手應復活");
            AssertXz(_opponent.transform.position, 17f, 17f, "(d) 基地被搶，對手應在場邊復活");
        }

        // ───────────── V-B13 倒地者不能被鎖定、也沒人理 ─────────────
        [UnityTest]
        public IEnumerator V_B13_NobodyStrikesAKnockedOutHero_AndAKnockedOutOpponentCannotBeTargeted()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponentLocomotion.WarpTo(new Vector3(0f, 0f, -11.625f)); // 距英雄 2m：活著的話一定會追打
            _hero.TakeDuelDamage(100f);
            Assert.IsFalse(_hero.IsAlive);
            bool warned = false;
            bool hadTowerTarget = false;
            for (int f = 1; f <= 300; f++)
            {
                yield return null;
                if (_hero.IsAlive) break; // 5 秒到期復活：倒地窗口結束
                if (_opponent.IsWarning) warned = true;
                if (_opponent.CaptureTargetTile >= 0) hadTowerTarget = true;
            }
            Assert.IsFalse(warned, "英雄倒地期間對手不得對它出招");
            Assert.IsTrue(hadTowerTarget, "英雄倒地期間對手應去搶塔（活性）");

            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(3f, 0f, 9.625f));
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(_opponent.IsAlive);
            for (int f = 0; f < 30; f++) yield return null; // 鏡頭追上
            TapOpponent();
            yield return null;
            Assert.IsNull(_hero.CurrentTarget, "倒地的對手不得被鎖定");
            Collider[] colliders = _opponent.GetComponentsInChildren<Collider>(true);
            Assert.Greater(colliders.Length, 0);
            for (int i = 0; i < colliders.Length; i++) Assert.IsFalse(colliders[i].enabled, "倒地對手的 Collider 仍開著");
        }

        // ───────────── V-B14 英雄倒地時封鎖輸入（含延遲層與跨倒地的手勢）─────────────
        [UnityTest]
        public IEnumerator V_B14_AKnockedOutHeroIgnoresAllInput_IncludingDelayedCommandsAndHeldGestures()
        {
            // ① 延遲 OFF：倒地期間的點地（3m 外）、點對手、微滑步、極速施放全部不得生效
            yield return Setup();
            EnterLobbyAndStart();
            for (int i = 0; i < 30; i++) yield return null;
            _hero.TakeDuelDamage(100f); // f0
            for (int f = 1; f <= 60; f++) yield return null;
            Vector3 corpse = _hero.transform.position;
            TapWorld(corpse + Vector3.right * 3f, "倒地時的點地");
            _opponentLocomotion.WarpTo(new Vector3(6f, 0f, -5f));
            TapOpponent();
            Assert.IsTrue(_bootstrap.TryGetRuneButtonScreenPoint(out float runeX, out float runeY));
            Vector3 flickAt = Camera.main.WorldToScreenPoint(corpse + Vector3.left * 2f);
            _bootstrap.BeginScreenHold(flickAt.x, flickAt.y);
            _bootstrap.MoveScreenHold(flickAt.x + 120f, flickAt.y);
            _bootstrap.EndScreenHold();
            for (int f = 61; f <= 200; f++) yield return null;
            TapRuneButton(); // 極速施放放在倒地後段：漏過去的牆壽命 5 秒，一定撐過復活後 30 幀的檢查
            int frame = 200;
            while (!_hero.IsAlive && frame < 306)
            {
                yield return null;
                frame++;
            }
            Assert.IsTrue(_hero.IsAlive, "① f0＋306 幀前英雄應復活");
            Vector3 respawn = _hero.transform.position;
            AssertXz(respawn, 0f, -13.625f, "① 復活點");
            for (int i = 0; i < 30; i++) yield return null;
            AssertXz(_hero.transform.position, respawn.x, respawn.z, "① 復活後 30 幀英雄應還在復活點");
            Assert.IsNull(_hero.CurrentTarget, "① 倒地時點對手不得留下鎖定");
            Assert.AreEqual(0, ActivePlayerWalls(), "① 倒地時的極速施放不得成牆");

            // ② 80 ms：復活前最後 80 ms（f0＋296～300 幀）送出的點地與極速施放，復活後都不生效
            yield return Setup();
            EnterLobbyAndStart();
            for (int i = 0; i < 30; i++) yield return null;
            _bootstrap.SetDuelLatencyPreset(80);
            _hero.TakeDuelDamage(100f); // f0
            for (int f = 1; f <= 296; f++) yield return null;
            Assert.IsFalse(_hero.IsAlive, "② 送出時英雄應仍倒地");
            corpse = _hero.transform.position;
            TapWorld(corpse + Vector3.right * 3f, "復活前 80 ms 的點地");
            TapRuneButton();
            frame = 296;
            while (!_hero.IsAlive && frame < 306)
            {
                yield return null;
                frame++;
            }
            Assert.IsTrue(_hero.IsAlive, "② f0＋306 幀前英雄應復活");
            respawn = _hero.transform.position;
            yield return new WaitForSecondsRealtime(0.12f); // 讓 80 ms 佇列真的到期（若沒被清掉就會在這裡送出）
            for (int i = 0; i < 30; i++) yield return null;
            AssertXz(_hero.transform.position, respawn.x, respawn.z, "② 復活後英雄不得被倒地時的點地帶走");
            Assert.IsNull(_hero.CurrentTarget);
            Assert.AreEqual(0, ActivePlayerWalls(), "② 復活前 80 ms 的極速施放不得成牆");

            // ③ 倒地前開始、復活後才放手的符印手勢不會成牆；對照組（活著時同一個手勢）確實成牆
            yield return Setup();
            EnterLobbyAndStart();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.IsTrue(_bootstrap.TryGetRuneButtonScreenPoint(out runeX, out runeY));
            _bootstrap.BeginScreenHold(runeX, runeY);
            _bootstrap.MoveScreenHold(runeX, runeY + 150f);
            yield return null;
            _hero.TakeDuelDamage(100f); // f0：手指還按著
            frame = 0;
            while (!_hero.IsAlive && frame < 306)
            {
                yield return null;
                frame++;
            }
            Assert.IsTrue(_hero.IsAlive, "③ f0＋306 幀前英雄應復活");
            yield return null;
            yield return null;
            _bootstrap.EndScreenHold(); // 復活後才放手
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(0, ActivePlayerWalls(), "③ 倒地前開始、復活後才放手的手勢不得成牆");

            _bootstrap.BeginScreenHold(runeX, runeY);
            _bootstrap.MoveScreenHold(runeX, runeY + 150f);
            yield return null;
            _bootstrap.EndScreenHold();
            yield return null;
            Assert.AreEqual(1, ActivePlayerWalls(), "③ 對照組：活著時同一個手勢應成牆（活性）");
        }

        // ───────────── V-B15 元素與測試設施在佔領對局中停用 ─────────────
        [UnityTest]
        public IEnumerator V_B15_ElementsTurretAndEnemyWall_AreLockedInActiveKnockoutAndEnded_ThenUsableInLobby()
        {
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            PressAllTestButtonsAndAssertLocked("佔領 Active");

            _hero.TakeDuelDamage(100f);
            yield return null;
            PressAllTestButtonsAndAssertLocked("英雄倒地");

            _bootstrap.SeedCaptureScoresForTest(998, 0);
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && f < 66)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "種子 998 後一次計分應結束");
            PressAllTestButtonsAndAssertLocked("Ended");

            f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && f < 186)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(1, _bootstrap.ElementField.ActiveZoneCount, "回 Lobby 後 WATER 應可用（活性）");
        }

        private void PressAllTestButtonsAndAssertLocked(string when)
        {
            _bootstrap.PressElementWaterButton();
            _bootstrap.HudPanel.PressTurretButton();
            _bootstrap.HudPanel.PressEnemyWallButton();
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount, when + "：WATER 不得施放");
            Assert.IsFalse(_bootstrap.Turret.IsFiring, when + "：TURRET 不得開火");
            Assert.AreEqual(0, _bootstrap.EnemyWalls.AliveCount(), when + "：ENEMY WALL 不得生牆");
        }

        // ───────────── V-B16 佔領後回單挑不殘留 ─────────────
        [UnityTest]
        public IEnumerator V_B16_BackToDuelAfterCapture_TheOpponentChasesNotTowers_AndKnockoutIsTheV070Reset()
        {
            yield return Setup();
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);

            float startDistance = Vector3.Distance(_hero.transform.position, _opponent.transform.position);
            bool warned = false;
            for (int f = 1; f <= 120; f++)
            {
                yield return null;
                Assert.AreEqual(-1, _opponent.CaptureTargetTile, "單挑中對手不得找塔（第 " + f + " 幀）");
                if (_opponent.IsWarning) warned = true;
            }
            Assert.Less(Vector3.Distance(_hero.transform.position, _opponent.transform.position), startDistance,
                "單挑中對手應朝英雄逼近");
            int g = 0;
            while (_hero.Health >= 100f && g < 120)
            {
                yield return null;
                g++;
                if (_opponent.IsWarning) warned = true;
            }
            Assert.IsTrue(warned, "應出現預警");
            Assert.AreEqual(80f, _hero.Health, 0.01f, "應命中一次");
            Assert.AreEqual(1, _opponent.AttacksResolved);

            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            int k = 0;
            while (_bootstrap.DuelState != DuelRoundState.Dormant && k < 156)
            {
                yield return null;
                k++;
            }
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState, "應走 v0.7.0 的 2.5 秒重置");
            Assert.GreaterOrEqual(k, 150);
            Assert.AreEqual(0, _bootstrap.CaptureRespawnCount, "不得走 5 秒復活");
            Assert.IsFalse(_opponent.IsBodyHidden);
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
        }

        // ───────────── V-B17 顯示綁定正確的塊 ─────────────
        [UnityTest]
        public IEnumerator V_B17_TheBoardShowsTheRightTile_TheRightDiscRadius_AndOneDiscPerTower()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            yield return null;
            yield return null;
            Material[] floorsBefore = new Material[7];
            Material[] ringsBefore = new Material[7];
            for (int i = 0; i < 7; i++)
            {
                floorsBefore[i] = _board.Floor(i).sharedMaterial;
                ringsBefore[i] = _board.Ring(i).sharedMaterial;
            }

            _heroLocomotion.WarpTo(new Vector3(-10.5f, 0f, -6.0625f)); // f0
            int f = 0;
            bool radiusChecked = false;
            while (Owner(5) != Faction.BlueTeam && f < 240)
            {
                yield return null;
                f++;
                if (f == 105)
                {
                    Renderer disc = _board.ProgressDisc(5);
                    Assert.IsTrue(disc.enabled, "引導中 5 號進度盤應可見");
                    Assert.AreEqual(1.25f, disc.bounds.extents.x, 0.05f, "1.75 秒時 5 號進度盤半徑");
                    radiusChecked = true;
                }
            }
            Assert.IsTrue(radiusChecked);
            Assert.AreEqual(Faction.BlueTeam, Owner(5));
            yield return null; // 翻藍的下一幀
            for (int i = 0; i < 7; i++)
            {
                if (i == 5)
                {
                    Assert.AreSame(_board.BlueMaterial, _board.Floor(5).sharedMaterial, "5 號地板應換成藍色材質");
                    Assert.AreSame(_board.BlueMaterial, _board.Ring(5).sharedMaterial, "5 號光圈應換成藍色材質");
                    continue;
                }
                Assert.AreSame(floorsBefore[i], _board.Floor(i).sharedMaterial, "地板 " + i + " 不應變動");
                Assert.AreSame(ringsBefore[i], _board.Ring(i).sharedMaterial, "光圈 " + i + " 不應變動");
            }

            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(-10.5f, 0f, -6.0625f));
            _opponentLocomotion.WarpTo(new Vector3(10.5f, 0f, 6.0625f));
            for (int k = 0; k < 60; k++) yield return null;
            Renderer blueDisc = _board.ProgressDisc(5);
            Renderer redDisc = _board.ProgressDisc(2);
            Assert.IsTrue(blueDisc.enabled, "英雄引導 5 號：5 號進度盤應可見");
            Assert.IsTrue(redDisc.enabled, "對手引導 2 號：2 號進度盤應同時可見");
            Assert.AreSame(_board.BlueMaterial, blueDisc.sharedMaterial);
            Assert.AreSame(_board.RedMaterial, redDisc.sharedMaterial);
            AssertXz(blueDisc.transform.position, -10.5f, -6.0625f, "5 號進度盤應在 5 號塔上");
            AssertXz(redDisc.transform.position, 10.5f, 6.0625f, "2 號進度盤應在 2 號塔上");
            for (int i = 0; i < 7; i++)
                if (i != 5 && i != 2) Assert.IsFalse(_board.ProgressDisc(i).enabled, "進度盤 " + i + " 不應可見");
        }
    }
}
