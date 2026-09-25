using System.Collections;
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
    // v0.8.0 對抗審查 r1 M1（REVIEW-v080-r1.md）：木樁 (0,0,6) 正好在 1 號（紅方基地）與 0 號塔的南北連線上，
    // 不在格點裡、NavMesh 烘焙時也還不存在。2、6 號是紅、0 號不是紅時，對手從紅方基地復活點 (0,13.625) 出發
    // 一定選 0 號，走的是 x＝0 的直線，正面撞上木樁膠囊。
    // captureDeltaTime＝1/60；「幀」＝一次 yield return null。期望值一律寫字面值。
    public sealed class CaptureDummyPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float EdgeMarginPixels = 8f; // GestureMath.EdgeDeadzonePixels
        private const float CircleRadius = 2.5f;   // E6（字面值，不讀 CaptureTuning）

        // 量測窗口寫死：13.625 → 0 號光圈邊 2.5 約 11.1m，對手 4 m/s 約 2.8 秒；窗口 360 幀＝6.0 秒。
        private const int WindowFrames = 360;
        private const int TraceEveryFrames = 30;
        // 準備階段（讓對手依序翻 2、6 號）每塊的上限：引導 3.5 秒＝210 幀，另留 30 幀。不屬於量測窗口。
        private const int FlipBudgetFrames = 240;

        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;
        private HeroLocomotion _heroLocomotion;
        private HeroLocomotion _opponentLocomotion;

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
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
        }

        // ───────────── 共用操作（全部走真實觸控路由，寫法同 CapturePlayTests）─────────────

        private void TapCaptureButton()
        {
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float x, out float y), "拿不到 CAPTURE 鈕的螢幕點");
            _bootstrap.WorldTapInput.SendScreenTap(x, y);
        }

        private void TapOpponent()
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
            Assert.Greater(screen.z, 0f, "對手在鏡頭後方");
            Assert.IsTrue(screen.x >= EdgeMarginPixels && screen.x <= Screen.width - EdgeMarginPixels
                          && screen.y >= EdgeMarginPixels && screen.y <= Screen.height - EdgeMarginPixels,
                "對手的螢幕投影 (" + screen.x + ", " + screen.y + ") 不在可點範圍內");
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

        private static float PlanarDistance(Vector3 a, float x, float z)
        {
            float dx = a.x - x;
            float dz = a.z - z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // 把對手放到塔心，讓它自己引導 3.5 秒翻成紅（走真實的 Tick 與對手 AI，不動純邏輯）。
        private IEnumerator LetTheOpponentFlip(int tile, Vector3 towerCenter)
        {
            _opponentLocomotion.WarpTo(towerCenter);
            int f = 0;
            while (Owner(tile) != Faction.RedTeam && f < FlipBudgetFrames)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(Faction.RedTeam, Owner(tile), "準備階段：對手站在 " + tile + " 號塔心 " + f + " 幀仍未翻紅");
        }

        // ───────────── M1 重現：紅方基地 → 0 號，途中的木樁 ─────────────
        [UnityTest]
        public IEnumerator M1_FromTheRedHomeWithTwoAndSixRed_TheOpponentReachesTowerZerosCircleWithinSixSeconds()
        {
            yield return Setup();
            EnterLobbyAndStart();
            _heroLocomotion.WarpTo(new Vector3(-17f, 0f, -17f)); // 遠處，不觸發追打（E9 6m）

            yield return LetTheOpponentFlip(2, new Vector3(10.5f, 0f, 6.0625f));
            yield return LetTheOpponentFlip(6, new Vector3(-10.5f, 0f, 6.0625f));
            Assert.AreEqual(Faction.RedTeam, Owner(1), "前提：1 號（紅方基地）是紅");
            Assert.AreEqual(Faction.RedTeam, Owner(2), "前提：2 號是紅");
            Assert.AreEqual(Faction.RedTeam, Owner(6), "前提：6 號是紅");
            Assert.AreNotEqual(Faction.RedTeam, Owner(0), "前提：0 號不是紅");
            Assert.IsTrue(_hero.IsAlive);
            Assert.IsTrue(_opponent.IsAlive);

            _opponentLocomotion.WarpTo(new Vector3(0f, 0f, 13.625f)); // 紅方基地復活點（E7）
            StringBuilder trace = new StringBuilder();
            trace.Append("f0 ").Append(_opponent.transform.position.ToString("F3"));
            int enteredFrame = -1;
            for (int f = 1; f <= WindowFrames; f++)
            {
                yield return null;
                Vector3 position = _opponent.transform.position;
                if (f == 1) Assert.AreEqual(0, _opponent.CaptureTargetTile, "前提：對手的目標塔應為 0");
                Assert.IsFalse(_opponent.IsChasingHero, "英雄在 (−17,−17)，第 " + f + " 幀對手不應追打");
                if (enteredFrame < 0 && PlanarDistance(position, 0f, 0f) <= CircleRadius) enteredFrame = f;
                if (f % TraceEveryFrames == 0)
                    trace.Append(" | f").Append(f).Append(' ').Append(position.ToString("F3"));
            }
            Debug.Log("[M1-PROBE] enteredFrame=" + enteredFrame + " trace: " + trace);
            Assert.GreaterOrEqual(enteredFrame, 1,
                "對手從 (0,13.625) 出發 " + WindowFrames + " 幀內沒有進入 0 號光圈（半徑 2.5）；軌跡：" + trace);
        }

        // ───────────── M1 修法：佔領模式停用木樁，回單挑恢復（使用者裁定 2026-09-25）─────────────
        [UnityTest]
        public IEnumerator M1_TheDummyIsOnInDuelMode_OffThroughLobbyActiveAndEnded_AndBackOnInDuelMode()
        {
            yield return Setup();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(dummy, "場景缺少木樁");
            // 頭頂血條是獨立物件（TargetOverheadDisplay 刻意不掛在目標底下）；停用後 GameObject.Find 找不到，所以先在 Off 抓住。
            GameObject overhead = GameObject.Find(dummy.name + "_Overhead");
            Assert.IsNotNull(overhead, "找不到木樁的頭頂血條物件");
            AssertDummy(dummy, overhead, true, "Off（起始）");

            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            AssertDummy(dummy, overhead, false, "Lobby");
            for (int i = 0; i < 30; i++) yield return null;
            AssertDummy(dummy, overhead, false, "Lobby 30 幀後");

            TapOpponent();
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            for (int i = 0; i < 30; i++) yield return null;
            AssertDummy(dummy, overhead, false, "Active");

            _bootstrap.SeedCaptureScoresForTest(998, 0);
            int f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Ended && f < 66)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Ended, _bootstrap.CaptureState, "種子 998 後 66 幀內應結算");
            AssertDummy(dummy, overhead, false, "Ended");
            f = 0;
            while (_bootstrap.CaptureState != CaptureMatchState.Lobby && f < 186)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "結算後 186 幀內應回 Lobby");
            AssertDummy(dummy, overhead, false, "結算後回 Lobby");

            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            AssertDummy(dummy, overhead, true, "回 Off");
            for (int i = 0; i < 30; i++) yield return null;
            AssertDummy(dummy, overhead, true, "回 Off 30 幀後");
            Assert.IsTrue(dummy.IsAlive);
            Assert.AreEqual(600f, dummy.Health, 0.01f, "木樁血量應維持滿血");
        }

        // 邊界①：木樁在單挑被打倒、2.5 秒復活倒數中就按 CAPTURE——倒數在 Lobby 裡到期時，照樣復活補血，但身體不得被打開。
        [UnityTest]
        public IEnumerator M1_ADummyThatRespawnsDuringCaptureMode_StaysDisabledUntilDuelMode()
        {
            yield return Setup();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(dummy, "場景缺少木樁");
            GameObject overhead = GameObject.Find(dummy.name + "_Overhead");
            Assert.IsNotNull(overhead, "找不到木樁的頭頂血條物件");

            dummy.ReceiveDamage(600f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(dummy.IsAlive, "前提：木樁應被打倒");
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            AssertDummy(dummy, overhead, false, "倒數中進 Lobby");

            int f = 0;
            while (!dummy.IsAlive && f < 180)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(dummy.IsAlive, "木樁 180 幀內應在 Lobby 裡倒數到期復活（活性）");
            AssertDummy(dummy, overhead, false, "Lobby 中倒數到期復活後");
            for (int i = 0; i < 30; i++) yield return null;
            AssertDummy(dummy, overhead, false, "Lobby 中復活 30 幀後");

            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            AssertDummy(dummy, overhead, true, "回 Off");
        }

        // 邊界②：倒數還沒到期就回單挑——恢復時仍在死亡狀態，身體維持倒地時的關閉，交給原本的復活流程打開。
        [UnityTest]
        public IEnumerator M1_ReturningToDuelModeWhileTheDummyIsStillDown_KeepsItHiddenUntilItsOwnRespawn()
        {
            yield return Setup();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(dummy, "場景缺少木樁");
            GameObject overhead = GameObject.Find(dummy.name + "_Overhead");
            Assert.IsNotNull(overhead, "找不到木樁的頭頂血條物件");

            dummy.ReceiveDamage(600f, DamageType.Physical, _hero.gameObject);
            Assert.IsFalse(dummy.IsAlive, "前提：木樁應被打倒");
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            for (int i = 0; i < 30; i++) yield return null;
            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
            Assert.IsFalse(dummy.IsAlive, "前提：回 Off 時木樁仍在倒數中");
            AssertDummyBody(dummy, false, "回 Off 時仍倒地");

            int f = 0;
            while (!dummy.IsAlive && f < 180)
            {
                yield return null;
                f++;
            }
            Assert.IsTrue(dummy.IsAlive, "木樁 180 幀內應復活（活性）");
            AssertDummy(dummy, overhead, true, "回 Off 後自己復活");
        }

        // r2 N1（使用者裁定 2026-09-25）：英雄正在打木樁時按 CAPTURE，進佔領模式就要清掉鎖定；之後 120 幀不得再命中。
        [UnityTest]
        public IEnumerator N1_EnteringCaptureModeWhileAttackingTheDummy_ClearsTheHerosTargetAndStopsTheHits()
        {
            yield return Setup();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(dummy, "場景缺少木樁");
            int hits = 0;
            _hero.OnAttackHitResolved += _ => hits++;

            // 走真實觸控路由點木樁（單挑待機 Off／Dormant）
            Vector3 screen = Camera.main.WorldToScreenPoint(dummy.transform.position + Vector3.up);
            Assert.Greater(screen.z, 0f, "木樁在鏡頭後方");
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
            int f = 0;
            while (hits < 1 && f < 300)
            {
                yield return null;
                f++;
            }
            Assert.GreaterOrEqual(hits, 1, "前提：點木樁 300 幀內英雄應至少命中一次（活性）");
            Assert.IsTrue(ReferenceEquals(dummy, _hero.CurrentTarget), "前提：英雄應鎖定木樁");
            Assert.IsTrue(dummy.IsAlive, "前提：木樁仍活著");
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);

            TapCaptureButton();
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            Assert.IsNull(_hero.CurrentTarget, "進佔領模式後英雄的鎖定應清掉");
            int hitsAtTap = hits;
            float healthAtTap = dummy.Health;
            for (int i = 0; i < 120; i++) yield return null;
            Assert.AreEqual(hitsAtTap, hits, "進佔領模式後 120 幀內英雄的命中數不得增加");
            Assert.AreEqual(healthAtTap, dummy.Health, 0.01f, "進佔領模式後 120 幀內木樁血量不得減少");
            Assert.IsNull(_hero.CurrentTarget, "120 幀後英雄仍不得鎖定任何目標");
        }

        private static bool IsDrawn(Renderer renderer)
        {
            return renderer.enabled && renderer.gameObject.activeInHierarchy;
        }

        private static void AssertDummyBody(DummyTarget dummy, bool expectedOn, string when)
        {
            Renderer[] renderers = dummy.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = dummy.GetComponentsInChildren<Collider>(true);
            Assert.Greater(renderers.Length, 0, when + "：木樁身上沒有 Renderer（活性）");
            Assert.Greater(colliders.Length, 0, when + "：木樁身上沒有 Collider（活性）");
            for (int i = 0; i < renderers.Length; i++)
                Assert.AreEqual(expectedOn, renderers[i].enabled, when + "：木樁 Renderer " + renderers[i].name + " 的 enabled");
            for (int i = 0; i < colliders.Length; i++)
                Assert.AreEqual(expectedOn, colliders[i].enabled, when + "：木樁 Collider " + colliders[i].name + " 的 enabled");
        }

        private static void AssertDummy(DummyTarget dummy, GameObject overhead, bool expectedOn, string when)
        {
            AssertDummyBody(dummy, expectedOn, when);

            // 頭頂血條：Off 時底色與血量條都畫得出來；停用時整組（含飄字）一個都畫不出來。
            Transform background = overhead.transform.Find("BarBackground");
            Transform fill = overhead.transform.Find("BarFill");
            Assert.IsNotNull(background, "血條缺 BarBackground");
            Assert.IsNotNull(fill, "血條缺 BarFill");
            Assert.AreEqual(expectedOn, IsDrawn(background.GetComponent<Renderer>()), when + "：血條底色是否畫出");
            Assert.AreEqual(expectedOn, IsDrawn(fill.GetComponent<Renderer>()), when + "：血量條是否畫出");
            if (expectedOn) return;
            Renderer[] overheadRenderers = overhead.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < overheadRenderers.Length; i++)
                Assert.IsFalse(IsDrawn(overheadRenderers[i]), when + "：血條物件 " + overheadRenderers[i].name + " 仍畫得出來");
        }
    }
}
