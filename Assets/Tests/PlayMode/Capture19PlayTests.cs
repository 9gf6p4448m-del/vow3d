using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // v0.9.0 步驟 B 的驗收（V090_ENCIRCLE_PLAN.md §3-B，V9-B01～V9-B20），以及 §2.6 退役條文的 19 塊版（V9-C01～C03）。
    // captureDeltaTime＝1/60（TearDown 還原）；「幀」＝一次 yield return null；「開局」＝真實點 CAPTURE 再真實點對手。
    // 期望值一律寫字面值，不讀 CaptureTuning／CaptureBoardSpec／HexBoardLayout。
    //
    // §2.6 退役對照（使用者 2026-09-25 同意，§5 Q8；舊條從原檔刪除）：
    //   CapturePlayTests.V_B01 → V9_B01    V_B04 → V9_B03    V_B05 → V9_B04    V_B06 → V9_B05
    //   V_B07 → V9_B06    V_B08 → V9_B07    V_B10 → V9_B18    V_B11 → V9_B19    V_B12 → V9_B14
    //   V_B13 → V9_B15    V_B14 → V9_B16    V_B17 → V9_B17    V_C01 → V9_C01    V_C02 → V9_C02
    //   V_C03 → V9_C03    CaptureDummyPlayTests.M1 重現 → V9_B20
    public sealed class Capture19PlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float PosTolerance = 0.05f;
        private const float EdgeMarginPixels = 8f;   // GestureMath.EdgeDeadzonePixels
        private const float TapMarginPixels = 16f;   // §3 共同遵守：點擊前投影離四邊都 ≥ 16px
        private const int TileCount = 19;
        private const int BlueCode = 0, RedCode = 1, NeutralCode = 2; // CaptureMatchLogic 陣營代碼（字面值）

        // E3 的 19 個塔心（字面值）。
        private static readonly Vector2[] TowerCenters =
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 7.578125f), new Vector2(6.5625f, 3.7890625f), new Vector2(6.5625f, -3.7890625f),
            new Vector2(0f, -7.578125f), new Vector2(-6.5625f, -3.7890625f), new Vector2(-6.5625f, 3.7890625f),
            new Vector2(0f, 15.15625f), new Vector2(6.5625f, 11.3671875f), new Vector2(13.125f, 7.578125f),
            new Vector2(13.125f, 0f), new Vector2(13.125f, -7.578125f), new Vector2(6.5625f, -11.3671875f),
            new Vector2(0f, -15.15625f), new Vector2(-6.5625f, -11.3671875f), new Vector2(-13.125f, -7.578125f),
            new Vector2(-13.125f, 0f), new Vector2(-13.125f, 7.578125f), new Vector2(-6.5625f, 11.3671875f)
        };

        private static readonly int[] OpeningBlue = { 12, 13, 14 };   // E7
        private static readonly int[] OpeningRed = { 7, 8, 18 };

        private static string ToolchainDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "vow-toolchain"));

        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;
        private HeroLocomotion _heroLocomotion;
        private HeroLocomotion _opponentLocomotion;
        private CaptureBoardView _board;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; AllocationProbe.Measuring = false; }

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

        // 條文指定的點地點：投影離四邊都 ≥ 16px，不成立就紅，不得換點（v0.8.0 V-B05 的教訓）。
        private void TapGroundStrict(Vector3 world, string who)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            Debug.Log("[CAPTURE19] " + who + " tap " + world + " → screen (" + screen.x + ", " + screen.y + ", depth " + screen.z
                      + ") of " + Screen.width + "x" + Screen.height);
            Assert.Greater(screen.z, 0f, who + "：點擊點在鏡頭後方");
            Assert.IsTrue(screen.x >= TapMarginPixels && screen.x <= Screen.width - TapMarginPixels
                          && screen.y >= TapMarginPixels && screen.y <= Screen.height - TapMarginPixels,
                who + "：點擊點投影 (" + screen.x + ", " + screen.y + ") 離畫面邊緣不到 16px");
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
        }

        private void TapRuneButton()
        {
            Assert.IsTrue(_bootstrap.TryGetRuneButtonScreenPoint(out float x, out float y));
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

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
            Faction[] owners = new Faction[TileCount];
            for (int i = 0; i < TileCount; i++) owners[i] = Owner(i);
            return owners;
        }

        private static Faction OpeningOwner(int tile)
        {
            for (int i = 0; i < OpeningBlue.Length; i++) if (OpeningBlue[i] == tile) return Faction.BlueTeam;
            for (int i = 0; i < OpeningRed.Length; i++) if (OpeningRed[i] == tile) return Faction.RedTeam;
            return Faction.Neutral;
        }

        private void AssertOpeningOwners(string when)
        {
            for (int i = 0; i < TileCount; i++) Assert.AreEqual(OpeningOwner(i), Owner(i), when + "：開局歸屬 " + i);
        }

        private int CountOwned(Faction faction)
        {
            int count = 0;
            for (int i = 0; i < TileCount; i++) if (Owner(i) == faction) count++;
            return count;
        }

        private void SeedBoard(int[] blue, int[] red)
        {
            int[] owners = new int[TileCount];
            for (int i = 0; i < TileCount; i++) owners[i] = NeutralCode;
            for (int i = 0; i < blue.Length; i++) owners[blue[i]] = BlueCode;
            for (int i = 0; i < red.Length; i++) owners[red[i]] = RedCode;
            _bootstrap.SeedCaptureOwnershipForTest(owners);
        }

        private static Vector3 Tower(int tile) { return new Vector3(TowerCenters[tile].x, 0f, TowerCenters[tile].y); }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static bool InAnyCircle(Vector3 p)
        {
            for (int i = 0; i < TileCount; i++)
            {
                float dx = p.x - TowerCenters[i].x;
                float dz = p.z - TowerCenters[i].y;
                if (dx * dx + dz * dz <= 2.5f * 2.5f) return true;
            }
            return false;
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

        private static bool Drawn(Renderer r) { return r != null && r.enabled && r.gameObject.activeInHierarchy; }

        // V9-B09 盤面：藍 {12,13,14,4,0}、紅 {7,8,18,1,2,3}；英雄 (−17,0,−17)、對手塔心 4（f0）；等 4 號翻紅（f0＋216 前）。
        // 回傳翻紅那一幀的 f（相對 f0）；翻紅那一幀檢查 0 號同時中立。
        private IEnumerator CutBlueAtTowerFour(int blueScore, int redScore, int[] flipFrameOut)
        {
            EnterLobbyAndStart();
            SeedBoard(new[] { 12, 13, 14, 4, 0 }, new[] { 7, 8, 18, 1, 2, 3 });
            _bootstrap.SeedCaptureScoresForTest(blueScore, redScore);
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f));
            _opponentLocomotion.WarpTo(Tower(4)); // f0
            int f = 0;
            while (Owner(4) != Faction.RedTeam && f < 216)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(Faction.RedTeam, Owner(4), "f0＋216 幀前 4 號應翻紅（f=" + f + "）");
            Assert.AreEqual(Faction.Neutral, Owner(0), "4 號翻紅的同一幀 0 號應已中立（斷線）");
            flipFrameOut[0] = f;
        }

        // ═════════════════════════ V9-B01 場景物件與測試牆 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B01_NineteenOfEachBoardObject_AndTheTestWallsAreOffInCaptureMode()
        {
            yield return Setup();

            Assert.IsFalse(_board.gameObject.activeInHierarchy, "Off 時 CaptureBoard 整組不得啟用");
            Assert.AreEqual(19, _board.FloorCount);
            Assert.AreEqual(19, _board.TowerCount);
            Assert.AreEqual(19, _board.RingCount);
            Assert.AreEqual(19, _board.ProgressDiscCount);
            for (int i = 0; i < 19; i++)
            {
                Assert.IsNotNull(_board.Floor(i));
                Assert.IsNotNull(_board.Tower(i));
                Assert.IsNotNull(_board.Ring(i));
                Assert.IsNotNull(_board.ProgressDisc(i));
            }
            Assert.AreEqual(76, _board.GetComponentsInChildren<Renderer>(true).Length, "CaptureBoard 底下恰好 19×4 個顯示物件");
            Assert.AreEqual(0, _board.GetComponentsInChildren<Collider>(true).Length, "板塊／塔／光圈／進度盤不得帶 Collider");
            Assert.AreEqual(0, _board.GetComponentsInChildren<CombatTargetBehaviour>(true).Length);
            for (int i = 0; i < 19; i++)
            {
                Vector3 tower = _board.Tower(i).transform.position;
                Assert.AreEqual(TowerCenters[i].x, tower.x, 0.001f, "塔 " + i + " 的 x");
                Assert.AreEqual(TowerCenters[i].y, tower.z, 0.001f, "塔 " + i + " 的 z");
            }

            GameObject[] walls = { GameObject.Find("TestWall_A"), GameObject.Find("TestWall_B") };
            GameObject[] overheads = { GameObject.Find("TestWall_A_Overhead"), GameObject.Find("TestWall_B_Overhead") };
            for (int w = 0; w < 2; w++)
            {
                Assert.IsNotNull(walls[w], "場景缺少測試牆 " + w);
                Assert.IsNotNull(overheads[w], "測試牆 " + w + " 缺少頭頂血條");
                AssertBody(walls[w], true, "Off 時的 " + walls[w].name);
            }
            Renderer[][] overheadRenderers =
            {
                overheads[0].GetComponentsInChildren<Renderer>(true), overheads[1].GetComponentsInChildren<Renderer>(true)
            };
            bool anyBarDrawn = false;
            for (int w = 0; w < 2; w++) for (int k = 0; k < overheadRenderers[w].Length; k++) anyBarDrawn |= Drawn(overheadRenderers[w][k]);
            Assert.IsTrue(anyBarDrawn, "Off 時測試牆頭頂血條應畫得出來（活性）");

            BlockGrid grid = _bootstrap.NavGrid;
            int blockedBefore = grid.BlockedCount;
            TapCaptureButton(); // 進 Lobby 的那一幀
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            for (int frame = 0; frame < 2; frame++)
            {
                for (int w = 0; w < 2; w++)
                {
                    Renderer[] rs = walls[w].GetComponentsInChildren<Renderer>(true);
                    for (int k = 0; k < rs.Length; k++) Assert.IsFalse(Drawn(rs[k]), walls[w].name + " 在 Lobby 仍畫得出來（第 " + frame + " 幀）");
                    Collider[] cs = walls[w].GetComponentsInChildren<Collider>(true);
                    for (int k = 0; k < cs.Length; k++) Assert.IsFalse(cs[k].enabled, walls[w].name + " 的 Collider 在 Lobby 仍開著");
                    for (int k = 0; k < overheadRenderers[w].Length; k++)
                        Assert.IsFalse(Drawn(overheadRenderers[w][k]), walls[w].name + " 的頭頂血條在 Lobby 仍畫得出來");
                }
                AssertNoBlockedCellInsideAnyCircle(grid, "Lobby 第 " + frame + " 幀");
                if (frame == 0) yield return null;
            }
            Assert.IsTrue(_board.gameObject.activeInHierarchy, "Lobby 時 CaptureBoard 應啟用");
            Assert.AreEqual(4.375f * 0.96f, _board.Floor(0).bounds.extents.x, 0.02f, "0 號地板的網格尺寸（R5：不得沿用 7m 資產）");

            TapCaptureButton(); // 回 Off
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            yield return null;
            Assert.AreEqual(blockedBefore, grid.BlockedCount, "回 Off 後被擋格數應等於進 Lobby 前");
            for (int w = 0; w < 2; w++) AssertBody(walls[w], true, "回 Off 後的 " + walls[w].name);

            // 邊界：Off 時先把 TestWall_A 打碎（6 秒重生倒數中）→ Lobby 420 幀仍畫不出來、不擋格 → 回 Off 維持碎裂，420 幀內由自己的重生流程打開。
            yield return Setup();
            GameObject wallA = GameObject.Find("TestWall_A");
            TestWallTarget targetA = wallA.GetComponent<TestWallTarget>();
            Renderer wallRenderer = wallA.GetComponent<Renderer>();
            Collider wallCollider = wallA.GetComponent<Collider>();
            grid = _bootstrap.NavGrid;
            Assert.IsTrue(targetA.NavBlockerStamped, "打碎前 TestWall_A 應擋格（活性）");
            targetA.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(targetA.IsAlive, "TestWall_A 應被打碎");
            Assert.IsFalse(targetA.NavBlockerStamped);
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            for (int f = 1; f <= 420; f++)
            {
                yield return null;
                Assert.IsFalse(Drawn(wallRenderer), "Lobby 第 " + f + " 幀 TestWall_A 畫出來了");
                Assert.IsFalse(wallCollider.enabled, "Lobby 第 " + f + " 幀 TestWall_A 的 Collider 開著");
                Assert.IsFalse(targetA.NavBlockerStamped, "Lobby 第 " + f + " 幀 TestWall_A 擋格");
            }
            AssertNoBlockedCellInsideAnyCircle(grid, "邊界組 Lobby 420 幀");
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.IsFalse(targetA.IsAlive, "回 Off 當下 TestWall_A 應維持碎裂");
            Assert.IsFalse(Drawn(wallRenderer), "回 Off 當下 TestWall_A 不應立刻打開");
            int reopened = -1;
            for (int f = 1; f <= 420; f++)
            {
                yield return null;
                if (targetA.IsAlive && Drawn(wallRenderer) && wallCollider.enabled && targetA.NavBlockerStamped)
                {
                    reopened = f;
                    break;
                }
            }
            Debug.Log("[CAPTURE19] V9-B01 TestWall_A reopened " + reopened + " frames after returning to Off");
            Assert.Greater(reopened, 0, "回 Off 後 420 幀內 TestWall_A 應由重生流程打開");
        }

        private static void AssertNoBlockedCellInsideAnyCircle(BlockGrid grid, string when)
        {
            for (int t = 0; t < 19; t++)
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
                        Assert.IsFalse(grid.IsBlocked(cx, cz), when + "：塔 " + t + " 光圈內的格 (" + x + ", " + z + ") 被擋");
                    }
                }
                Assert.Greater(checkedCells, 0, when + "：塔 " + t + " 光圈內一格都沒檢查到（活性）");
            }
        }

        // ═════════════════════════ V9-B03 開局 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B03_ARealStart_TeleportsBothToTheirFirstMotherTile_WithTheNineteenTileOpening()
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
            AssertXz(_hero.transform.position, 0f, -16.65625f, "英雄開局應在 13 號母板塊的復活點");
            AssertXz(_opponent.transform.position, 0f, 16.65625f, "對手開局應在 7 號母板塊的復活點");
            AssertOpeningOwners("開局");
            Assert.AreEqual(0, _bootstrap.CaptureView.BlueScore);
            Assert.AreEqual(0, _bootstrap.CaptureView.RedScore);
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount, "開局應清掉元素區域");
            Assert.IsFalse(turret.IsFiring);
            for (int i = 0; i < bullets.Length; i++) Assert.IsFalse(bullets[i].IsActive, "池內子彈 " + i + " 仍作用中");
        }

        // ═════════════════════════ V9-B04 真實點地搶 4 號 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B04_ARealGroundTapSouthOfTowerFour_WalksThereAndFlipsItBlueInTime()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(_opponent.IsAlive);
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上傳送後的英雄

            Faction[] before = SnapshotOwners();
            TapGroundStrict(new Vector3(0f, 0f, -9.578125f), "V9-B04");
            bool sawProgress = false;
            int flipFrame = -1;
            for (int f = 1; f <= 390; f++)
            {
                yield return null;
                ICaptureMatchView view = _bootstrap.CaptureView;
                if (Owner(4) == Faction.BlueTeam)
                {
                    flipFrame = f;
                    break;
                }
                if (view.BlueChannelingTile == 4 && view.BlueChannelProgress > 0f) sawProgress = true;
            }
            Debug.Log("[CAPTURE19] V9-B04 flipped at frame " + flipFrame + " after the tap");
            Assert.Greater(flipFrame, 0, "點擊後 390 幀內 4 號沒有翻藍");
            Assert.GreaterOrEqual(flipFrame, 270, "4 號翻藍太早：" + flipFrame);
            Assert.IsTrue(sawProgress, "翻藍前應至少有一幀藍方引導進度 > 0");
            for (int i = 0; i < TileCount; i++)
                if (i != 4) Assert.AreEqual(before[i], Owner(i), "其他塊 " + i + " 不應變動");
        }

        // ═════════════════════════ V9-B05 引導時間接線 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B05_StandingOnTowerFour_FlipsItBetweenThreePointFourAndThreePointSixSeconds()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            yield return null;

            _heroLocomotion.WarpTo(Tower(4)); // f0
            for (int f = 1; f <= 204; f++) yield return null;
            Assert.AreEqual(Faction.Neutral, Owner(4), "f0＋204 幀（3.4 秒）時 4 號應仍中立");
            int f2 = 204;
            while (Owner(4) != Faction.BlueTeam && f2 < 216)
            {
                yield return null;
                f2++;
            }
            Assert.AreEqual(Faction.BlueTeam, Owner(4), "f0＋216 幀（3.6 秒）前 4 號應翻藍");
        }

        // ═════════════════════════ V9-B06 受傷打斷 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B06_HeroDamage_EvenFullyAbsorbedByShield_RestartsTheChannelOnTowerFour()
        {
            for (int group = 0; group < 2; group++)
            {
                bool shieldGroup = group == 1;
                string tag = shieldGroup ? "護盾組" : "扣血組";
                yield return Setup();
                EnterLobbyAndStart();
                _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
                yield return null;

                _heroLocomotion.WarpTo(Tower(4)); // f0
                for (int f = 1; f <= 120; f++) yield return null;
                Assert.Greater(_bootstrap.CaptureView.BlueChannelProgress, 0f, tag + "：受傷前應在引導（活性）");
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
                Assert.AreEqual(Faction.Neutral, Owner(4), tag + "：f0＋120＋204 幀時 4 號應仍中立（受傷應重新起算）");
                int f2 = 204;
                while (Owner(4) != Faction.BlueTeam && f2 < 216)
                {
                    yield return null;
                    f2++;
                }
                Assert.AreEqual(Faction.BlueTeam, Owner(4), tag + "：f0＋120＋216 幀前應翻藍");
            }
        }

        // ═════════════════════════ V9-B07 對手被打中也會打斷 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B07_OpponentDamage_RestartsItsChannelOnTowerOne()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponentLocomotion.WarpTo(Tower(1));
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f)); // f0
            for (int f = 1; f <= 120; f++) yield return null;
            Assert.Greater(_bootstrap.CaptureView.RedChannelProgress, 0f, "受傷前對手應在引導 1 號（活性）");
            _opponent.ReceiveDamage(60f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(240f, _opponent.Health, 0.01f);

            for (int f = 1; f <= 204; f++) yield return null;
            Assert.AreEqual(Faction.Neutral, Owner(1), "f0＋120＋204 幀時 1 號應仍中立");
            int f2 = 204;
            while (Owner(1) != Faction.RedTeam && f2 < 216)
            {
                yield return null;
                f2++;
            }
            Assert.AreEqual(Faction.RedTeam, Owner(1), "f0＋120＋216 幀前 1 號應翻紅");
        }

        // ═════════════════════════ V9-B08 孤島翻塊的顯示 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B08_AnIslandFlipOnTowerZero_NeverShowsBlue()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            yield return null;

            int flipsBefore = _bootstrap.CaptureFlipCount;
            int neutralizedBefore = _bootstrap.CaptureBlueNeutralizedCount;
            Material blue = _board.BlueMaterial;
            _heroLocomotion.WarpTo(Tower(0)); // f0
            int activeFrame = -1;
            for (int f = 1; f <= 240; f++)
            {
                yield return null;
                Assert.AreEqual(Faction.Neutral, Owner(0), "第 " + f + " 幀 0 號不是中立");
                Assert.AreNotSame(blue, _board.Floor(0).sharedMaterial, "第 " + f + " 幀 0 號地板是藍色材質");
                if (activeFrame < 0 && _bootstrap.CaptureFlipCount - flipsBefore >= 1
                    && _bootstrap.CaptureBlueNeutralizedCount - neutralizedBefore >= 1)
                    activeFrame = f;
            }
            Debug.Log("[CAPTURE19] V9-B08 island flip+neutralize at frame " + activeFrame);
            Assert.Greater(activeFrame, 0, "240 幀內應有一次翻塊＋一次藍方中立化（活性）");
            Assert.Less(activeFrame, 216, "翻塊＋中立化應在 f0＋216 幀前發生：" + activeFrame);
        }

        // ═════════════════════════ V9-B09 斷線接線與 HUD ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B09_CuttingTowerZero_NeutralizesItOnTheBoard_AndShowsRageTwelveOnlyWhenBehind()
        {
            // B 組 (0,100)：觸發
            yield return Setup();
            int[] flip = new int[1];
            yield return CutBlueAtTowerFour(0, 100, flip);
            yield return null; // 下一幀
            Assert.AreSame(_board.NeutralMaterial, _board.Floor(0).sharedMaterial, "0 號地板應換成中立材質");
            Assert.AreSame(_board.RedMaterial, _board.Floor(4).sharedMaterial, "4 號地板應換成紅色材質");
            Assert.Greater(_bootstrap.CaptureView.BlueRageRemaining, 11.9f, "落後 100 分被斷線應觸發藍方狂怒");
            Assert.AreEqual("RAGE 12", _bootstrap.CaptureRageLabel);

            // 對照組 (100,100)：不觸發
            yield return Setup();
            yield return CutBlueAtTowerFour(100, 100, flip);
            yield return null;
            Assert.AreEqual(1, _bootstrap.CaptureBlueNeutralizedCount, "對照組也應斷線（活性）");
            Assert.AreEqual(0f, _bootstrap.CaptureView.BlueRageRemaining, "同分不得觸發狂怒");
            Assert.IsNull(_bootstrap.CaptureRageLabel, "沒有狂怒時 RAGE 列不得顯示");
        }

        // ═════════════════════════ V9-B10 英雄移速（A／B 對照）═════════════════════════
        [UnityTest]
        public IEnumerator V9_B10_ARagingHeroWalksFifteenPercentFaster()
        {
            float[] d = new float[2];
            float rageMultiplier = 0f;
            for (int group = 0; group < 2; group++)
            {
                bool rage = group == 1;
                yield return Setup();
                int[] flip = new int[1];
                yield return CutBlueAtTowerFour(rage ? 0 : 100, 100, flip);
                Assert.AreEqual(rage, _bootstrap.CaptureView.BlueRageRemaining > 0f, (rage ? "B" : "A") + " 組狂怒前提");
                TapGroundStrict(new Vector3(-17f, 0f, -9f), (rage ? "B" : "A") + " 組點地");
                Vector3 p31 = Vector3.zero;
                for (int f = 1; f <= 60; f++)
                {
                    yield return null;
                    if (f == 31) p31 = _hero.transform.position;
                }
                d[group] = PlanarDistance(_hero.transform.position, p31);
                if (rage) rageMultiplier = _heroLocomotion.SpeedMultiplier;
            }
            Debug.Log("[CAPTURE19] V9-B10 dA=" + d[0] + " dB=" + d[1] + " ratio=" + (d[1] / d[0]) + " multB=" + rageMultiplier);
            Assert.GreaterOrEqual(d[0], 2.60f, "dA");
            Assert.LessOrEqual(d[0], 2.80f, "dA");
            Assert.GreaterOrEqual(d[1] / d[0], 1.13f, "dB/dA");
            Assert.LessOrEqual(d[1] / d[0], 1.17f, "dB/dA");
            Assert.AreEqual(1.15f, rageMultiplier, 1e-4f, "B 組 SpeedMultiplier");
        }

        // ═════════════════════════ V9-B11 對手移速（A／B 對照）═════════════════════════
        [UnityTest]
        public IEnumerator V9_B11_ARagingOpponentWalksFifteenPercentFaster()
        {
            float[] e = new float[2];
            for (int group = 0; group < 2; group++)
            {
                bool rage = group == 1;
                string tag = rage ? "B 組" : "A 組";
                yield return Setup();
                EnterLobbyAndStart();
                SeedBoard(new[] { 12, 13, 14, 3, 2 }, new[] { 7, 8, 18, 9, 10 });
                _bootstrap.SeedCaptureScoresForTest(100, rage ? 0 : 100);
                _heroLocomotion.WarpTo(Tower(9));
                int f = 0;
                while (Owner(9) != Faction.BlueTeam && f < 240)
                {
                    yield return null;
                    f++;
                }
                Assert.AreEqual(Faction.BlueTeam, Owner(9), tag + "：英雄應翻下 9 號");
                Assert.AreEqual(Faction.Neutral, Owner(10), tag + "：10 號應被斷線中立化");
                Assert.AreEqual(rage, _bootstrap.CaptureView.RedRageRemaining > 0f, tag + "：紅方狂怒前提");

                _opponentLocomotion.WarpTo(new Vector3(0f, 0f, 16.65625f));
                Vector3 p31 = Vector3.zero;
                for (int k = 1; k <= 60; k++)
                {
                    yield return null;
                    if (k == 31) p31 = _opponent.transform.position;
                }
                Assert.AreEqual(1, _opponent.CaptureTargetTile, tag + "：對手的目標應是 1 號");
                e[group] = PlanarDistance(_opponent.transform.position, p31);
            }
            Debug.Log("[CAPTURE19] V9-B11 eA=" + e[0] + " eB=" + e[1] + " ratio=" + (e[1] / e[0]));
            Assert.GreaterOrEqual(e[0], 1.90f, "eA");
            Assert.LessOrEqual(e[0], 2.10f, "eA");
            Assert.GreaterOrEqual(e[1] / e[0], 1.13f, "eB/eA");
            Assert.LessOrEqual(e[1] / e[0], 1.17f, "eB/eA");
        }

        // ═════════════════════════ V9-B12 狂怒跨倒地、準時結束 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B12_RageSurvivesAKnockout_AndEndsOnTime()
        {
            yield return Setup();
            int[] flip = new int[1];
            yield return CutBlueAtTowerFour(0, 100, flip);
            Assert.Greater(_bootstrap.CaptureView.BlueRageRemaining, 0f, "觸發幀（f_r）藍方狂怒應 > 0");
            Renderer aura = _bootstrap.RageAuras.BlueAura;
            int fr = 0; // 相對 f_r 的幀數
            for (; fr < 60; fr++) yield return null;
            _hero.TakeDuelDamage(100f); // f_r＋60
            Assert.IsFalse(_hero.IsAlive);
            while (!_hero.IsAlive && fr < 420)
            {
                yield return null;
                fr++;
            }
            Assert.IsTrue(_hero.IsAlive, "英雄應在 5 秒後復活");
            yield return null;
            yield return null;
            fr += 2;
            Assert.AreEqual(1.15f, _heroLocomotion.SpeedMultiplier, 1e-4f, "復活後第 2 幀狂怒倍率應恢復（R3）");
            Assert.IsTrue(Drawn(aura), "復活後第 2 幀光環應可見");

            while (fr < 714)
            {
                yield return null;
                fr++;
            }
            Assert.AreEqual(1.15f, _heroLocomotion.SpeedMultiplier, 1e-4f, "f_r＋714 幀時仍在狂怒中");
            int ended = -1;
            while (fr < 726)
            {
                yield return null;
                fr++;
                if (Mathf.Abs(_heroLocomotion.SpeedMultiplier - 1f) < 1e-4f && !Drawn(aura) && _bootstrap.CaptureRageLabel == null)
                {
                    ended = fr;
                    break;
                }
            }
            Debug.Log("[CAPTURE19] V9-B12 rage ended at f_r+" + ended + " (flip f=" + flip[0] + ")");
            Assert.Greater(ended, 0, "f_r＋726 幀前倍率應回 1.0、光環關閉、RAGE 列消失");
        }

        // ═════════════════════════ V9-B13 狂怒「人看得出來」═════════════════════════
        [UnityTest]
        public IEnumerator V9_B13_TheRageAuraIsActuallyVisibleAroundTheHero()
        {
            yield return Setup();
            int[] flip = new int[1];
            yield return CutBlueAtTowerFour(0, 100, flip);
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject); // 隱藏對手
            for (int i = 0; i < 30; i++) yield return null;
            Assert.Greater(_bootstrap.CaptureView.BlueRageRemaining, 0f, "前提：藍方狂怒中");

            // (a) 狀態
            Renderer aura = _bootstrap.RageAuras.BlueAura;
            Assert.IsNotNull(aura);
            Assert.IsTrue(Drawn(aura), "藍方光環應開著");
            Vector3 heroPos = _hero.transform.position;
            Assert.LessOrEqual(Mathf.Abs(aura.bounds.center.x - heroPos.x), 0.05f, "光環中心 x");
            Assert.LessOrEqual(Mathf.Abs(aura.bounds.center.z - heroPos.z), 0.05f, "光環中心 z");
            Assert.GreaterOrEqual(aura.bounds.extents.x, 1.1f, "光環半徑");
            Assert.IsFalse(aura.transform.IsChildOf(_hero.transform), "光環不得掛在英雄底下");
            Assert.IsFalse(aura.transform.IsChildOf(_opponent.transform), "光環不得掛在對手底下");

            // (b) 實際渲染
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType,
                "graphicsDeviceType＝Null：無法實際渲染，判紅（不得略過）");
            Camera cam = Camera.main;
            RenderTexture rt = new RenderTexture(640, 480, 24);
            RenderTexture previousTarget = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                Vector3 hs = cam.WorldToScreenPoint(heroPos);
                Texture2D on = RenderToTexture(cam, rt);
                aura.enabled = false;
                Texture2D off = RenderToTexture(cam, rt);
                Texture2D off2 = RenderToTexture(cam, rt);
                aura.enabled = true;

                int diff = CountDiffPixels(on, off, hs, 80);
                int noise = CountDiffPixels(off, off2, hs, 80);
                Directory.CreateDirectory(ToolchainDir);
                File.WriteAllBytes(Path.Combine(ToolchainDir, "v090-rage-on.png"), EncodePng(on));
                File.WriteAllBytes(Path.Combine(ToolchainDir, "v090-rage-off.png"), EncodePng(off));
                Debug.Log("[CAPTURE19] V9-B13 device=" + SystemInfo.graphicsDeviceType + " heroScreen=(" + hs.x + ", " + hs.y
                          + ") diffPixels=" + diff + " noisePixels=" + noise + " png=" + ToolchainDir);
                Object.Destroy(on);
                Object.Destroy(off);
                Object.Destroy(off2);
                Assert.GreaterOrEqual(diff, 800, "光環開／關在英雄周圍 ±80px 的差異像素數");
                Assert.LessOrEqual(noise, 20, "雜訊對照（兩次都關）的差異像素數");
            }
            finally
            {
                cam.targetTexture = previousTarget;
                RenderTexture.active = null;
                rt.Release();
                Object.Destroy(rt);
            }

            // (a) 續：英雄倒地時光環關閉
            _hero.TakeDuelDamage(100f);
            yield return null;
            yield return null;
            Assert.IsFalse(Drawn(aura), "英雄倒地時光環應關閉");
            Assert.Greater(_bootstrap.CaptureView.BlueRageRemaining, 0f, "倒地期間狂怒仍在（活性）");
        }

        private static Texture2D RenderToTexture(Camera cam, RenderTexture rt)
        {
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex;
        }

        // 專案沒有啟用 ImageConversion 模組（Packages/manifest.json），Texture2D.EncodeToPNG 不可用；
        // 這裡寫一個最小的 PNG 編碼（RGB8、每列 filter 0、deflate stored 區塊），只給測試存圖用。
        private static byte[] EncodePng(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            byte[] raw = new byte[(w * 3 + 1) * h];
            int p = 0;
            for (int y = h - 1; y >= 0; y--) // PNG 由上往下；Texture2D 的 y＝0 在下方
            {
                raw[p++] = 0;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = tex.GetPixel(x, y);
                    raw[p++] = c.r;
                    raw[p++] = c.g;
                    raw[p++] = c.b;
                }
            }

            MemoryStream z = new MemoryStream();
            z.WriteByte(0x78);
            z.WriteByte(0x01);
            int offset = 0;
            while (offset < raw.Length)
            {
                int len = Mathf.Min(65535, raw.Length - offset);
                z.WriteByte((byte)(offset + len >= raw.Length ? 1 : 0));
                z.WriteByte((byte)(len & 0xff));
                z.WriteByte((byte)(len >> 8));
                z.WriteByte((byte)(~len & 0xff));
                z.WriteByte((byte)((~len >> 8) & 0xff));
                z.Write(raw, offset, len);
                offset += len;
            }
            uint a = 1, b = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                a = (a + raw[i]) % 65521;
                b = (b + a) % 65521;
            }
            uint adler = (b << 16) | a;
            z.WriteByte((byte)(adler >> 24));
            z.WriteByte((byte)(adler >> 16));
            z.WriteByte((byte)(adler >> 8));
            z.WriteByte((byte)adler);

            MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            byte[] ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, (uint)w);
            WriteBigEndian(ihdr, 4, (uint)h);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 2;  // color type RGB
            WritePngChunk(png, "IHDR", ihdr);
            WritePngChunk(png, "IDAT", z.ToArray());
            WritePngChunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        private static void WriteBigEndian(byte[] buffer, int at, uint value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        private static void WritePngChunk(MemoryStream png, string type, byte[] data)
        {
            byte[] header = new byte[8];
            WriteBigEndian(header, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) header[4 + i] = (byte)type[i];
            png.Write(header, 0, 8);
            png.Write(data, 0, data.Length);
            uint crc = 0xffffffffu;
            for (int i = 4; i < 8; i++) crc = Crc32Step(crc, header[i]);
            for (int i = 0; i < data.Length; i++) crc = Crc32Step(crc, data[i]);
            crc ^= 0xffffffffu;
            byte[] tail = new byte[4];
            WriteBigEndian(tail, 0, crc);
            png.Write(tail, 0, 4);
        }

        private static uint Crc32Step(uint crc, byte value)
        {
            crc ^= value;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            return crc;
        }

        private static int CountDiffPixels(Texture2D a, Texture2D b, Vector3 center, int half)
        {
            int x0 = Mathf.Max(0, Mathf.RoundToInt(center.x) - half);
            int x1 = Mathf.Min(a.width - 1, Mathf.RoundToInt(center.x) + half);
            int y0 = Mathf.Max(0, Mathf.RoundToInt(center.y) - half);
            int y1 = Mathf.Min(a.height - 1, Mathf.RoundToInt(center.y) + half);
            int count = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Color ca = a.GetPixel(x, y);
                    Color cb = b.GetPixel(x, y);
                    if (Mathf.Abs(ca.r - cb.r) > 0.2f || Mathf.Abs(ca.g - cb.g) > 0.2f || Mathf.Abs(ca.b - cb.b) > 0.2f) count++;
                }
            }
            return count;
        }

        // ═════════════════════════ V9-B14 復活地點 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B14_RespawnPoints_FollowTheMotherTilePriority_AndTheEdgeWhenAllAreLost()
        {
            // (a) 英雄倒地 → 5 秒後在 13 號復活點
            yield return Setup();
            EnterLobbyAndStart();
            yield return null;
            _hero.TakeDuelDamage(100f); // f0
            Assert.IsFalse(_hero.IsAlive);
            yield return null;
            AssertBody(_hero.gameObject, false, "倒地的英雄");
            int f = 1;
            while (!_hero.IsAlive && f < 306)
            {
                yield return null;
                f++;
                if (f == 294) Assert.IsFalse(_hero.IsAlive, "(a) f0＋294 幀時不應已復活");
            }
            Assert.IsTrue(_hero.IsAlive, "(a) f0＋306 幀前英雄應復活");
            Assert.Greater(f, 294, "(a) 英雄復活太早：f0＋" + f);
            AssertXz(_hero.transform.position, 0f, -16.65625f, "(a) 英雄應在 13 號母板塊復活點");
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            AssertBody(_hero.gameObject, true, "復活的英雄");

            // (b) 優先序：13 號不在手上 → 12 號復活點
            yield return Setup();
            EnterLobbyAndStart();
            SeedBoard(new[] { 12, 14 }, new[] { 7, 8, 18 });
            _hero.TakeDuelDamage(100f); // f0
            f = 0;
            while (!_hero.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(_hero.IsAlive, "(b) f0＋306 幀前英雄應復活");
            AssertXz(_hero.transform.position, 6.5625f, -12.8671875f, "(b) 英雄應在 12 號母板塊復活點");

            // (c) 母板塊全失 → 場邊
            yield return Setup();
            EnterLobbyAndStart();
            SeedBoard(new[] { 13, 4, 0 }, new[] { 7, 8, 18, 1, 2, 3, 12, 6, 5, 14 });
            _hero.TakeDuelDamage(100f); // f0
            _opponentLocomotion.WarpTo(Tower(13));
            f = 0;
            while (Owner(13) != Faction.RedTeam && f < 216)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(Faction.RedTeam, Owner(13), "(c) f0＋216 幀前 13 號應翻紅");
            Assert.AreEqual(Faction.Neutral, Owner(4), "(c) 13 號翻紅的同一幀 4 號應中立");
            Assert.AreEqual(Faction.Neutral, Owner(0), "(c) 13 號翻紅的同一幀 0 號應中立");
            while (!_hero.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(_hero.IsAlive, "(c) f0＋306 幀前英雄應復活");
            AssertXz(_hero.transform.position, -17f, -17f, "(c) 母板塊全失，英雄應在場邊復活");

            // (d) 牆壓紅方復活點 → 對手復活後被推出、不卡死（其餘斷言照 v0.8.0 V-B12(c)）
            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(4f, 0f, 16.65625f));
            _hero.transform.rotation = Quaternion.Euler(0f, -90f, 0f); // 面向 −x：極速牆落在正前方 4m＝(0, 16.65625)
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject); // f0
            for (f = 1; f <= 120; f++) yield return null;
            TapRuneButton();
            RuneWall wall = null;
            RuneWall[] pool = Object.FindObjectOfType<RuneCaster>().Pool;
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) wall = pool[i];
            Assert.IsNotNull(wall, "(d) 符印施放入口沒有立出牆");
            AssertXz(wall.transform.position, 0f, 16.65625f, "(d) 牆應立在紅方 7 號復活點上");
            f = 120;
            while (!_opponent.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(_opponent.IsAlive, "(d) f0＋306 幀前對手應復活");
            Assert.IsTrue(wall.IsAlive, "(d) 復活那一幀牆應仍作用中（活性）");
            Assert.Greater(wall.RemainingLifespan, 0f);
            for (int k = 0; k <= 3; k++)
            {
                Vector3 p = _opponent.transform.position;
                Assert.IsFalse(BlockGrid.CircleOverlapsBox(p.x, p.z, 0.35f, 0f, 16.65625f, -1f, 0f, 2f, 0.3f),
                    "(d) 復活後第 " + k + " 幀對手身體仍與牆相交：" + p);
                if (k < 3) yield return null;
            }
            Vector3 afterRespawn = _opponent.transform.position;
            for (int k = 0; k < 120; k++) yield return null;
            Assert.Greater(Vector3.Distance(afterRespawn, _opponent.transform.position), 0.5f, "(d) 對手復活後卡死");

            // (e) 對手：7 號復活點、HP 300；紅方母板塊全失 → 場邊 (17,17)
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
            Assert.IsTrue(_opponent.IsAlive, "(e) f0＋306 幀前對手應復活");
            Assert.Greater(f, 294, "(e) 對手復活太早");
            AssertXz(_opponent.transform.position, 0f, 16.65625f, "(e) 對手應在 7 號母板塊復活點");
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            AssertBody(_opponent.gameObject, true, "復活的對手");

            yield return Setup();
            EnterLobbyAndStart();
            SeedBoard(new[] { 12, 13, 14, 4, 0, 1 }, new[] { 7 });
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject); // f0
            _heroLocomotion.WarpTo(Tower(7));
            f = 0;
            while (!_opponent.IsAlive && f < 306)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(Faction.BlueTeam, Owner(7), "(e) 英雄站在 7 號塔心，對手復活前 7 號應已翻藍");
            Assert.IsTrue(_opponent.IsAlive, "(e) f0＋306 幀前對手應復活");
            AssertXz(_opponent.transform.position, 17f, 17f, "(e) 紅方母板塊全失，對手應在場邊復活");
        }

        // ═════════════════════════ V9-B15 倒地者不能被鎖定、也沒人理 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B15_NobodyStrikesAKnockedOutHero_AndAKnockedOutOpponentCannotBeTargeted()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponentLocomotion.WarpTo(new Vector3(0f, 0f, -14.65625f)); // 距英雄 2.0m：活著的話一定會追打
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

        // ═════════════════════════ V9-B16 英雄倒地時封鎖輸入 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B16_AKnockedOutHeroIgnoresAllInput_IncludingDelayedCommandsAndHeldGestures()
        {
            // ① 延遲 OFF：倒地期間的點地（3m 外）、點對手、微滑步、極速施放全部不得生效
            yield return Setup();
            EnterLobbyAndStart();
            for (int i = 0; i < 30; i++) yield return null;
            _hero.TakeDuelDamage(100f); // f0
            for (int f = 1; f <= 60; f++) yield return null;
            Vector3 corpse = _hero.transform.position;
            TapWorld(corpse + Vector3.right * 3f, "倒地時的點地");
            // 偏離 v0.8.0 測試碼的字面座標（非凍結條文字面）：v0.8.0 把對手放在 (6,−5)，即屍體 (0,−13.625) 的 (+6,+8.625)；
            // 19 塊的屍體在 (0,−16.65625)，(6,−5) 的投影 y≈516 已在 480 高的畫面外，任何實作都點不到。
            // 這裡保留相同的相對位置 (+6,+8.625)＝(6,−8.03125)，投影與 v0.8.0 相同；斷言一字不動。
            _opponentLocomotion.WarpTo(new Vector3(6f, 0f, -8.03125f));
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
            AssertXz(respawn, 0f, -16.65625f, "① 復活點");
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
            // 同 v0.8.0 V-B14：延遲層用真實時間計時，先讓 80 ms 佇列確實到期，再開始 30 幀的觀察。
            yield return new WaitForSecondsRealtime(0.12f);
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

        // ═════════════════════════ V9-B17 顯示綁定正確的塊 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B17_TheBoardShowsTheRightTile_TheRightDiscRadius_AndOneDiscPerTower()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            yield return null;
            yield return null;
            Material[] floorsBefore = new Material[TileCount];
            Material[] ringsBefore = new Material[TileCount];
            for (int i = 0; i < TileCount; i++)
            {
                floorsBefore[i] = _board.Floor(i).sharedMaterial;
                ringsBefore[i] = _board.Ring(i).sharedMaterial;
            }

            _heroLocomotion.WarpTo(Tower(4)); // f0
            int f = 0;
            bool radiusChecked = false;
            while (Owner(4) != Faction.BlueTeam && f < 240)
            {
                yield return null;
                f++;
                if (f == 105)
                {
                    Renderer disc = _board.ProgressDisc(4);
                    Assert.IsTrue(disc.enabled, "引導中 4 號進度盤應可見");
                    Assert.AreEqual(1.25f, disc.bounds.extents.x, 0.05f, "1.75 秒時 4 號進度盤半徑");
                    radiusChecked = true;
                }
            }
            Assert.IsTrue(radiusChecked);
            Assert.AreEqual(Faction.BlueTeam, Owner(4));
            yield return null; // 翻藍的下一幀
            for (int i = 0; i < TileCount; i++)
            {
                if (i == 4)
                {
                    Assert.AreSame(_board.BlueMaterial, _board.Floor(4).sharedMaterial, "4 號地板應換成藍色材質");
                    Assert.AreSame(_board.BlueMaterial, _board.Ring(4).sharedMaterial, "4 號光圈應換成藍色材質");
                    continue;
                }
                Assert.AreSame(floorsBefore[i], _board.Floor(i).sharedMaterial, "地板 " + i + " 不應變動");
                Assert.AreSame(ringsBefore[i], _board.Ring(i).sharedMaterial, "光圈 " + i + " 不應變動");
            }

            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(Tower(4));
            _opponentLocomotion.WarpTo(Tower(1));
            for (int k = 0; k < 60; k++) yield return null;
            Renderer blueDisc = _board.ProgressDisc(4);
            Renderer redDisc = _board.ProgressDisc(1);
            Assert.IsTrue(blueDisc.enabled, "英雄引導 4 號：4 號進度盤應可見");
            Assert.IsTrue(redDisc.enabled, "對手引導 1 號：1 號進度盤應同時可見");
            Assert.AreSame(_board.BlueMaterial, blueDisc.sharedMaterial);
            Assert.AreSame(_board.RedMaterial, redDisc.sharedMaterial);
            AssertXz(blueDisc.transform.position, 0f, -7.578125f, "4 號進度盤應在 4 號塔上");
            AssertXz(redDisc.transform.position, 0f, 7.578125f, "1 號進度盤應在 1 號塔上");
            for (int i = 0; i < TileCount; i++)
                if (i != 4 && i != 1) Assert.IsFalse(_board.ProgressDisc(i).enabled, "進度盤 " + i + " 不應可見");
        }

        // ═════════════════════════ V9-B18 對手自己搶點 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B18_TheOpponentGoesForTowerOne_AndFlipsItFirst()
        {
            yield return Setup();
            EnterLobbyAndStart();
            Faction[] opening = SnapshotOwners();
            bool sawWindup = false;
            int firstChangedTile = -1;
            int firstChangedFrame = -1;
            for (int f = 1; f <= 420 && firstChangedTile < 0; f++)
            {
                yield return null;
                if (_opponent.IsWarning) sawWindup = true;
                if (f == 60) Assert.AreEqual(1, _opponent.CaptureTargetTile, "第 60 幀時對手的目標塔應為 1");
                for (int i = 0; i < TileCount; i++)
                {
                    if (Owner(i) == opening[i]) continue;
                    firstChangedTile = i;
                    firstChangedFrame = f;
                    break;
                }
            }
            Debug.Log("[CAPTURE19] V9-B18 first change tile " + firstChangedTile + " at frame " + firstChangedFrame);
            Assert.AreEqual(1, firstChangedTile, "第一塊變色的應是 1 號");
            Assert.AreEqual(Faction.RedTeam, Owner(1));
            Assert.GreaterOrEqual(firstChangedFrame, 300, "1 號翻紅太早：" + firstChangedFrame);
            Assert.LessOrEqual(firstChangedFrame, 420);
            Assert.IsFalse(sawWindup, "英雄不做任何輸入時，對手不應進入前搖");
        }

        // ═════════════════════════ V9-B19 轉頭追打與放棄 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B19_TheOpponentTurnsOnANearbyHero_FinishesItsStrike_ThenGivesUp()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _opponentLocomotion.WarpTo(Tower(1));
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f));
            for (int f = 0; f < 30; f++) yield return null;
            Assert.IsFalse(_opponent.IsChasingHero);

            _heroLocomotion.WarpTo(new Vector3(5.0f, 0f, 7.578125f)); // 距 1 號塔心 5.0m
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

        // ═════════════════════════ V9-B20 對手不卡住 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_B20_LeftAlone_TheOpponentKeepsFlippingTilesForThirtySeconds()
        {
            yield return Setup();
            EnterLobbyAndStart();
            Faction[] opening = SnapshotOwners();
            int redFlipsBefore = _bootstrap.CaptureRedFlipCount;
            int lastRedFlipCount = redFlipsBefore;
            int lastRedFlipFrame = -1;
            int maxGap = 0;
            int firstChangedTile = -1;
            Vector3 windowStart = _opponent.transform.position;
            System.Text.StringBuilder trace = new System.Text.StringBuilder();
            for (int f = 1; f <= 1800; f++)
            {
                yield return null;
                if (firstChangedTile < 0)
                {
                    for (int i = 0; i < TileCount; i++)
                    {
                        if (Owner(i) == opening[i]) continue;
                        firstChangedTile = i;
                        break;
                    }
                }
                int redFlips = _bootstrap.CaptureRedFlipCount;
                if (redFlips != lastRedFlipCount)
                {
                    if (lastRedFlipFrame >= 0 && f - lastRedFlipFrame > maxGap) maxGap = f - lastRedFlipFrame;
                    trace.Append(" flip@").Append(f).Append(" tgt").Append(_opponent.CaptureTargetTile);
                    lastRedFlipCount = redFlips;
                    lastRedFlipFrame = f;
                }
                if (f % 120 == 0)
                {
                    Vector3 now = _opponent.transform.position;
                    trace.Append(" | f").Append(f).Append(' ').Append(now.ToString("F2"));
                    if (!InAnyCircle(now))
                        Assert.Greater(PlanarDistance(now, windowStart), 0.5f,
                            "第 " + (f - 120) + "～" + f + " 幀對手不在任何光圈內卻幾乎沒動：" + now + " 軌跡：" + trace);
                    windowStart = now;
                }
            }
            int total = _bootstrap.CaptureRedFlipCount - redFlipsBefore;
            Debug.Log("[CAPTURE19] V9-B20 redFlips=" + total + " maxGap=" + maxGap + " first=" + firstChangedTile + " trace:" + trace);
            Assert.AreEqual(1, firstChangedTile, "第一塊變色的應是 1 號");
            Assert.GreaterOrEqual(total, 5, "30 秒內紅方翻塊次數；軌跡：" + trace);
            Assert.LessOrEqual(maxGap, 480, "兩次紅方翻塊的間隔；軌跡：" + trace);
        }

        // ═════════════════════════ V9-C01 真實輸入完整一局（藍勝）═════════════════════════
        [UnityTest]
        public IEnumerator V9_C01_ARealInputFullMatch_BlueWins_FreezesThreeSeconds_ThenLobby_AndTheSecondMatchResets()
        {
            yield return Setup();
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(1, _bootstrap.CaptureStartCount);
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上傳送後的英雄

            // 真實點 (0,−9.578125) → 270～390 幀內 4 號翻藍
            TapGroundStrict(new Vector3(0f, 0f, -9.578125f), "V9-C01");
            int f = 0;
            while (Owner(4) != Faction.BlueTeam && f < 390)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(Faction.BlueTeam, Owner(4), "點 4 號南側地面後 390 幀內應翻藍");
            Assert.GreaterOrEqual(f, 270, "4 號翻藍太早：" + f);

            // 真實點對手；投影不在畫面內就先真實點地朝它走（每次 4m），直到投影進入畫面再點。
            bool tappedOpponent = false;
            for (int attempt = 0; attempt < 40 && !tappedOpponent; attempt++)
            {
                if (_opponent.IsAlive)
                {
                    Vector3 screen = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
                    if (screen.z > 0f && screen.x >= 16f && screen.x <= Screen.width - 16f
                        && screen.y >= 16f && screen.y <= Screen.height - 16f)
                    {
                        _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
                        tappedOpponent = true;
                        break;
                    }
                }
                if (_hero.IsAlive)
                {
                    Vector3 toward = _opponent.transform.position - _hero.transform.position;
                    toward.y = 0f;
                    if (toward.sqrMagnitude > 1e-4f) TapWorld(_hero.transform.position + toward.normalized * 4f, "朝對手走的點地");
                }
                for (int i = 0; i < 30; i++) yield return null;
            }
            Assert.IsTrue(tappedOpponent, "一直沒能讓對手進入畫面並點到它");
            int g = 0;
            while (_opponent.Health >= 300f && g < 600)
            {
                yield return null;
                g++;
            }
            Assert.Less(_opponent.Health, 300f, "點對手後 600 幀內普攻沒有命中");

            // 終局：種子前藍方 ≥ 2 塊、紅分 < 962 → 種子 (996, 目前紅分) → 126 幀內 Ended、藍勝
            Assert.GreaterOrEqual(CountOwned(Faction.BlueTeam), 2, "種子前藍方應至少有 2 塊");
            Assert.Less(_bootstrap.CaptureView.RedScore, 962, "種子前紅分應 < 962");
            _bootstrap.SeedCaptureScoresForTest(996, _bootstrap.CaptureView.RedScore);
            g = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && g < 126)
            {
                yield return null;
                g++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "種子後 126 幀內應結束");
            Assert.AreEqual(CaptureMatchResult.BlueWins, _bootstrap.CaptureView.Result);
            Assert.AreEqual("BLUE WINS", _bootstrap.MatchStatusLabel);

            int ended = 0;
            Vector3 frozenAt = _hero.transform.position;
            TapWorld(frozenAt + Vector3.right * 3f, "結算停頓中的點地");
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount, "結算停頓中 WATER 應無效");
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                ended++;
                Assert.Less(Vector3.Distance(frozenAt, _hero.transform.position), 0.01f, "結算停頓中英雄不得移動（第 " + ended + " 幀）");
            }
            while (ended < 174)
            {
                yield return null;
                ended++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "進入 Ended 後 174 幀時應仍在 Ended");
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && ended < 186)
            {
                yield return null;
                ended++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "進入 Ended 後 186 幀前應回到 Lobby");
            AssertXz(_hero.transform.position, 0f, 0f, "回 Lobby 時英雄應在出生點");
            AssertXz(_opponent.transform.position, -4f, 8f, "回 Lobby 時對手應在出生點");
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            Assert.AreEqual("LAST: BLUE WINS", _bootstrap.MatchStatusLabel);
            Assert.GreaterOrEqual(_bootstrap.CaptureView.BlueScore, 1000, "回 Lobby 後比分應保留");
            yield return null;
            Assert.AreEqual(0f, _bootstrap.CaptureView.BlueRageRemaining, "回待機後不得殘留藍方狂怒");
            Assert.AreEqual(0f, _bootstrap.CaptureView.RedRageRemaining, "回待機後不得殘留紅方狂怒");
            Assert.IsFalse(Drawn(_bootstrap.RageAuras.BlueAura), "回待機後不得殘留藍方光環");
            Assert.IsFalse(Drawn(_bootstrap.RageAuras.RedAura), "回待機後不得殘留紅方光環");
            Assert.AreEqual(1f, _heroLocomotion.SpeedMultiplier, 1e-4f, "回待機後英雄移速倍率應為 1");
            Assert.AreEqual(1f, _opponentLocomotion.SpeedMultiplier, 1e-4f, "回待機後對手移速倍率應為 1");

            // 第二局：真實點對手 → 開局次數 2、19 塊歸屬回開局、比分 0／0
            for (int i = 0; i < 30; i++) yield return null;
            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            Assert.AreEqual(2, _bootstrap.CaptureStartCount);
            AssertOpeningOwners("第二局");
            Assert.AreEqual(0, _bootstrap.CaptureView.BlueScore);
            Assert.AreEqual(0, _bootstrap.CaptureView.RedScore);
        }

        // ═════════════════════════ V9-C02 紅勝與平手 ═════════════════════════
        [UnityTest]
        public IEnumerator V9_C02_RedWins_AndASimultaneousThousandFourIsADraw()
        {
            yield return Setup();
            EnterLobbyAndStart();
            Assert.GreaterOrEqual(CountOwned(Faction.RedTeam), 1, "開局後紅方應至少有 1 塊");
            _bootstrap.SeedCaptureScoresForTest(0, 996);
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && f < 126)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "種子 (0, 996) 後 126 幀內應結束");
            Assert.AreEqual(CaptureMatchResult.RedWins, _bootstrap.CaptureView.Result);
            Assert.AreEqual("RED WINS", _bootstrap.MatchStatusLabel);

            yield return Setup();
            EnterLobbyAndStart();
            for (f = 1; f <= 30; f++) yield return null;
            AssertOpeningOwners("第 30 幀");
            _bootstrap.SeedCaptureScoresForTest(998, 998);
            f = 30;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && f < 66)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "第 66 幀前應結束");
            Assert.GreaterOrEqual(f, 60, "結束太早：第 " + f + " 幀");
            Assert.AreEqual(CaptureMatchResult.Draw, _bootstrap.CaptureView.Result);
            Assert.AreEqual("DRAW", _bootstrap.MatchStatusLabel);
            Assert.AreEqual(1004, _bootstrap.CaptureView.BlueScore);
            Assert.AreEqual(1004, _bootstrap.CaptureView.RedScore);
        }

        // ═════════════════════════ V9-C03 佔領對局零配置（含 BFS 與狂怒）═════════════════════════
        // 量法同 v0.8.0 V-C03（AllocationProbe 前後夾住全場 Update／LateUpdate，ProfilerRecorder 讀數）。
        [UnityTest]
        public IEnumerator V9_C03_ACaptureMatchWithACutRageAKnockoutAndARespawn_AllocatesNothingInUpdateOrLateUpdate()
        {
            yield return Setup();

            // 暖機局（v0.8.0 M2 三條約束＋R14）：只碰 2、3、9、10 號與紅方光環；英雄在 9 號翻塊切斷 10 號、觸發紅方狂怒，
            // 接著英雄倒地（倒地路徑第一次 JIT），種子 (998,0) 結束並回 Lobby。量測局用 0、4、15 號與藍方光環。
            EnterLobbyAndStart();
            SeedBoard(new[] { 12, 13, 14, 3, 2 }, new[] { 7, 8, 18, 9, 10 });
            _bootstrap.SeedCaptureScoresForTest(100, 0);
            _heroLocomotion.WarpTo(Tower(9));
            int warm = 0;
            while (Owner(9) != Faction.BlueTeam && warm < 240)
            {
                yield return null;
                warm++;
            }
            Assert.AreEqual(Faction.BlueTeam, Owner(9), "暖機局：英雄應翻下 9 號");
            Assert.AreEqual(Faction.Neutral, Owner(10), "暖機局：10 號應被斷線");
            Assert.Greater(_bootstrap.CaptureView.RedRageRemaining, 0f, "暖機局：紅方狂怒（活性）");
            yield return null;
            _hero.TakeDuelDamage(100f);
            _bootstrap.SeedCaptureScoresForTest(998, 0);
            warm = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && warm < 300)
            {
                yield return null;
                warm++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "暖機局應結束並回 Lobby");
            Assert.AreEqual(1, _bootstrap.CaptureFlipCount, "暖機局只翻 9 號一塊");
            Assert.AreEqual(0, _bootstrap.CaptureBlueRageTriggerCount, "暖機局不得觸發藍方狂怒（藍方光環留給量測局）");
            for (int i = 0; i < 30; i++) yield return null; // 鏡頭追上回到出生點的英雄

            AllocationProbe.Reset();
            GameObject rig = new GameObject("Capture19ProbeRig");
            rig.AddComponent<AllocationProbeBegin>();
            CaptureKnockoutDriver driver = rig.AddComponent<CaptureKnockoutDriver>();
            driver.Hero = _hero;
            driver.KnockoutAtFrame = 160;
            rig.AddComponent<AllocationProbeEnd>();

            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "量測局應開局");
            SeedBoard(new[] { 12, 13, 14, 4, 0 }, new[] { 7, 8, 18, 1, 2, 3 });
            _bootstrap.SeedCaptureScoresForTest(0, 100);
            _heroLocomotion.WarpTo(Tower(15));
            _opponentLocomotion.WarpTo(Tower(4));
            for (int i = 0; i < 60; i++) yield return null; // 開局後 60 幀開始量測

            int flipsBefore = _bootstrap.CaptureFlipCount;
            int neutralizedBefore = _bootstrap.CaptureBlueNeutralizedCount;
            int rageTriggersBefore = _bootstrap.CaptureBlueRageTriggerCount;
            int auraTogglesBefore = _bootstrap.RageAuras.ToggleCount;
            int rageLabelsBefore = _bootstrap.CaptureRageLabelRecomputeCount;
            int respawnsBefore = _bootstrap.CaptureRespawnCount;
            int scoreTicksBefore = _bootstrap.CaptureScoreTickCount;
            int swapsBefore = _board.MaterialSwapCount;

            AllocationProbe.Measuring = true;
            for (int i = 0; i < 480; i++) yield return null;
            AllocationProbe.Measuring = false;
            Object.Destroy(rig);

            int flips = _bootstrap.CaptureFlipCount - flipsBefore;
            int neutralized = _bootstrap.CaptureBlueNeutralizedCount - neutralizedBefore;
            int rageTriggers = _bootstrap.CaptureBlueRageTriggerCount - rageTriggersBefore;
            int auraToggles = _bootstrap.RageAuras.ToggleCount - auraTogglesBefore;
            int rageLabels = _bootstrap.CaptureRageLabelRecomputeCount - rageLabelsBefore;
            int respawns = _bootstrap.CaptureRespawnCount - respawnsBefore;
            int scoreTicks = _bootstrap.CaptureScoreTickCount - scoreTicksBefore;
            int swaps = _board.MaterialSwapCount - swapsBefore;
            Debug.Log("[CAPTURE19] V9-C03 frames=" + AllocationProbe.Frames + " update=" + AllocationProbe.UpdateBytes
                      + " late=" + AllocationProbe.LateUpdateBytes + " flips=" + flips + " neutralized=" + neutralized
                      + " rageTriggers=" + rageTriggers + " auraToggles=" + auraToggles + " rageLabels=" + rageLabels
                      + " respawns=" + respawns + " scoreTicks=" + scoreTicks + " swaps=" + swaps);
            Assert.GreaterOrEqual(AllocationProbe.Frames, 480);
            Assert.IsTrue(driver.Fired, "驅動元件沒有在窗口第 160 幀擊倒英雄");
            Assert.GreaterOrEqual(flips, 2, "翻塊（活性：英雄 15、對手 4）");
            Assert.GreaterOrEqual(neutralized, 1, "中立化（活性：0 號）");
            Assert.GreaterOrEqual(rageTriggers, 1, "藍方狂怒觸發（活性）");
            Assert.GreaterOrEqual(auraToggles, 2, "光環切換（活性：開→倒地關→復活開）");
            Assert.GreaterOrEqual(rageLabels, 1, "RAGE 字串重算（活性）");
            Assert.GreaterOrEqual(respawns, 1, "復活（活性）");
            Assert.IsTrue(_hero.IsAlive, "窗口結束時英雄應已復活");
            Assert.GreaterOrEqual(scoreTicks, 7, "計分（活性）");
            Assert.GreaterOrEqual(swaps, 1, "換色（活性）");
            Assert.AreEqual(0L, AllocationProbe.UpdateBytes,
                "佔領對局 Update 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.UpdateBytes + " bytes");
            Assert.AreEqual(0L, AllocationProbe.LateUpdateBytes,
                "佔領對局 LateUpdate 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.LateUpdateBytes + " bytes");
        }
    }
}
