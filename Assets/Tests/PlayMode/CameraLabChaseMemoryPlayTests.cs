#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;
using Vow.Input;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // camera-lab K4／K5：失去視野追擊記憶（HeroController.TickLostTargetMemory）的兩個組合情境。
    //   K4＝走到最後看見位置的途中被牆／旁人擋住；K5＝多個目標同時失去視野。
    // 斷言都是實測真實行為後才寫的（探針軌跡見 vow-toolchain/camera-lab-k45-probe*.log），不是照想像寫。
    public sealed class CameraLabChaseMemoryPlayTests
    {
        private CameraComparisonLab _lab;
        private PlayerInputService _input;
        private HeroController _hero;
        private Phase1Bootstrap _bootstrap;
        private TrainingOpponent _red;
        private float _health0;

        [TearDown]
        public void Cleanup()
        {
            if (_input != null) { _input.EndSimulatedHold(0); _input.EndSimulatedHold(1); }
            if (_lab != null) _lab.enabled = false;
            Time.captureDeltaTime = 0f;
        }

        private void TapAttack()
        {
            ScreenRegion r = _lab.ActionButtonLayout.Attack;
            _input.SendScreenTap((r.XMin + r.XMax) * .5f, (r.YMin + r.YMax) * .5f);
        }

        // 峽谷對局中（對手 Hold）、第三人稱；與 CameraLabCombatPlayTests.StartCanyonMatchThirdPerson 同一套起手。
        private IEnumerator StartCanyonMatchThirdPerson()
        {
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null; yield return null;
            _lab = Object.FindObjectOfType<CameraComparisonLab>();
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _input = Object.FindObjectOfType<PlayerInputService>();
            _hero = _lab.FollowedHero;
            yield return null;
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float sx, out float sy));
            _bootstrap.WorldTapInput.SendScreenTap(sx, sy);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            _red = Object.FindObjectOfType<TrainingOpponent>();
            Vector3 redScreen = Camera.main.WorldToScreenPoint(_red.transform.position + Vector3.up);
            _bootstrap.WorldTapInput.SendScreenTap(redScreen.x, redScreen.y);
            yield return null;
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "點紅開局");
            _bootstrap.HoldOpponentForTest(true);
            _lab.SetThirdPerson(true);
            yield return null; yield return null;
        }

        private IEnumerator PlaceCanyon(Vector3 heroAt, Vector3 redAt, bool aimAtRed)
        {
            _hero.GetComponent<HeroLocomotion>().WarpTo(heroAt);
            _red.RespawnAt(redAt);
            for (int i = 0; i < 4; i++) yield return null;
            Vector3 h = _hero.transform.position, r = _red.transform.position;
            float bearing = Mathf.Atan2(r.x - h.x, r.z - h.z) * Mathf.Rad2Deg;
            _lab.RotateThirdPerson(bearing + (aimAtRed ? 0f : 180f) - _lab.YawDegrees);
            for (int i = 0; i < 3; i++) yield return null;
        }

        private static Vector3 TileCenter(int tile)
        {
            CanyonTerrainSpec t = CanyonTerrainSpec.V0140;
            return new Vector3(t.Board.CenterX(tile), t.TileHeight(tile), t.Board.CenterZ(tile));
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static int CountAliveWalls()
        {
            int n = 0;
            foreach (RuneWall w in Object.FindObjectsOfType<RuneWall>()) if (w.IsAlive) n++;
            return n;
        }

        // 崖台 tile 5 對準谷底 tile 4 的對手鎖定 → 等到失去視野、進入追擊記憶（K1～K3 同一起手式）。
        private IEnumerator LockAndLose()
        {
            yield return StartCanyonMatchThirdPerson();
            yield return PlaceCanyon(TileCenter(5), TileCenter(4), true);
            _health0 = _red.HealthNormalized;
            TapAttack();
            Assert.AreSame(_red, _lab.LastAimTarget);
            for (int i = 0; i < 120 && _hero.CurrentTarget != null; i++) yield return null;
            Assert.IsNull(_hero.CurrentTarget, "前提：2 秒內因視野失去目標");
            Assert.AreSame(_red, _hero.LostTargetForTest, "前提：進入失去視野的追擊記憶");
        }

        private TrainingOpponent SpawnBystander(Vector3 position)
        {
            TrainingOpponent other = Object.Instantiate(_red);
            other.HoldForTest(true);
            other.RespawnAt(position);
            // RespawnAt 只在有 _hero 時才啟用；複製體沒接英雄，要手動設成「啟用」才算活的可打目標（Hold 中不會自己動）。
            typeof(TrainingOpponent).GetField("_active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(other, true);
            return other;
        }

        // 追擊記憶執行期間的共同觀測：卡死（記憶在、1 秒內位移 <0.5m）、記憶重複建立（null→非 null 的次數）、
        // 是否曾鎖到旁人、最遠走到哪、與旁人最近多近。
        private sealed class Watch
        {
            public int Stalls;
            public int LostRises;
            public bool LockedBystander;
            public float MaxZ = float.NegativeInfinity;
            public float MinDistToBystander = float.PositiveInfinity;
        }

        // 跑到對手受傷或逾時為止。
        private IEnumerator RunUntilHit(float seconds, Watch w, TrainingOpponent bystander)
        {
            Vector3 prev = _hero.transform.position;
            bool prevLost = _hero.LostTargetForTest != null;
            int frames = Mathf.RoundToInt(seconds * 60f);
            for (int f = 1; f <= frames && _red.HealthNormalized >= _health0; f++)
            {
                yield return null;
                bool lost = _hero.LostTargetForTest != null;
                if (lost && !prevLost) w.LostRises++;
                prevLost = lost;
                Vector3 h = _hero.transform.position;
                if (h.z > w.MaxZ) w.MaxZ = h.z;
                if (bystander != null)
                {
                    if (ReferenceEquals(_hero.CurrentTarget, bystander)) w.LockedBystander = true;
                    float d = Vector3.Distance(Flat(h), Flat(bystander.transform.position));
                    if (d < w.MinDistToBystander) w.MinDistToBystander = d;
                }
                if (f % 60 == 0)
                {
                    if (lost && Vector3.Distance(Flat(h), Flat(prev)) < 0.5f) w.Stalls++;
                    prev = h;
                }
            }
        }

        // K4a：途中一面石牆封住「回谷底的坡口」（牆 4m 寬＞坡 3.5m）→ 英雄繞走另一個坡口，不卡死，仍接回打到。
        // 實測：英雄原本走南坡口（0,-11.4），牆出現後改繞到北邊（z 最大約 14）從另一個坡口下谷底，約 9s 後在谷底重新看見並出手。
        [UnityTest]
        public IEnumerator K4a_WallSealsRampMouthOnTheWay_HeroDetoursAndStillReconnects()
        {
            yield return LockAndLose();
            for (int i = 0; i < 100; i++) yield return null;           // 英雄走到南坡口前（約 (-9.5,0,-10.6)）
            Assert.IsNull(_hero.CurrentTarget, "前提：仍看不見");
            _bootstrap.SpawnRuneWallForTest(0, 0f, -11.4f, 0f, 1f);
            Assert.AreEqual(1, CountAliveWalls(), "前提：牆立住了");
            var w = new Watch();
            yield return RunUntilHit(25f, w, null);
            Debug.Log("[CAMERA LAB TEST] K4a maxZ=" + w.MaxZ.ToString("F1") + " stalls=" + w.Stalls + " lostRises=" + w.LostRises);
            Assert.Less(_red.HealthNormalized, _health0, "25 秒內繞路後接回並打到對手");
            Assert.Greater(w.MaxZ, 5f, "繞到北邊坡口（沒有走被封的南坡口）");
            Assert.AreEqual(0, w.Stalls, "途中沒有卡死（任一秒位移 <0.5m）");
            Assert.AreEqual(0, w.LostRises, "追擊記憶沒有重複建立（無抖動）");
        }

        // K4b：旁人（另一個目標體）站在必經之路上 → 不擋路（沒有身體碰撞）、不被誤鎖、對手照樣接回打到。
        [UnityTest]
        public IEnumerator K4b_BystanderStandingOnTheRoute_DoesNotBlockOrHijack()
        {
            yield return LockAndLose();
            TrainingOpponent other = SpawnBystander(new Vector3(-9.0f, 0f, -10.9f));
            yield return null;
            var w = new Watch();
            yield return RunUntilHit(15f, w, other);
            Debug.Log("[CAMERA LAB TEST] K4b minDistToBystander=" + w.MinDistToBystander.ToString("F2") + " stalls=" + w.Stalls);
            Assert.Less(_red.HealthNormalized, _health0, "15 秒內接回並打到原目標");
            Assert.LessOrEqual(w.MinDistToBystander, 1.5f, "前提：路線確實從旁人身邊經過");
            Assert.IsFalse(w.LockedBystander, "旁人沒被誤鎖");
            Assert.AreEqual(1f, other.HealthNormalized, "旁人沒被打");
            Assert.AreEqual(0, w.Stalls);
            Assert.AreEqual(0, w.LostRises);
        }

        // K4c：兩個坡口都被牆封死、對手還在原地 → 英雄不走谷底，改到崖台邊緣（離最後位置最近的走得到處）重新看見，從崖台出手。
        [UnityTest]
        public IEnumerator K4c_BothRampMouthsSealed_HeroReSeesFromThePlateauEdgeAndHits()
        {
            yield return LockAndLose();
            _bootstrap.SpawnRuneWallForTest(0, 0f, -11.4f, 0f, 1f);
            _bootstrap.SpawnRuneWallForTest(0, 0f, 11.4f, 0f, 1f);
            Assert.AreEqual(2, CountAliveWalls(), "前提：兩面牆立住");
            var w = new Watch();
            yield return RunUntilHit(8f, w, null);
            Debug.Log("[CAMERA LAB TEST] K4c heroY=" + _hero.transform.position.y.ToString("F2") + " stalls=" + w.Stalls);
            Assert.Less(_red.HealthNormalized, _health0, "8 秒內重新看見並出手");
            Assert.Greater(_hero.transform.position.y, 0.9f, "仍在崖台上（谷底被封死沒下去）");
            Assert.AreEqual(0, w.Stalls);
            Assert.AreEqual(0, w.LostRises);
        }

        // K4d：兩個坡口被封死、且對手被搬到谷底另一端（看不見）→ 英雄走到崖台邊緣就放棄：回到 Idle、記憶清掉，
        // 之後牆到期也不復活、不抖動、不出手（抵達即取消的既定規則；不是缺陷，是記錄真實行為）。
        [UnityTest]
        public IEnumerator K4d_SealedAndTargetUnseen_HeroGivesUpAtPlateauEdge_NoJitter()
        {
            yield return LockAndLose();
            _red.RespawnAt(TileCenter(1));
            _bootstrap.SpawnRuneWallForTest(0, 0f, -11.4f, 0f, 1f);
            _bootstrap.SpawnRuneWallForTest(0, 0f, 11.4f, 0f, 1f);
            int guard = 0;
            while (_hero.LostTargetForTest != null && guard++ < 60 * 10) yield return null;
            Debug.Log("[CAMERA LAB TEST] K4d gaveUpAfter=" + guard + " frames heroPos=" + _hero.transform.position.ToString("F2") + " state=" + _hero.StateMachine.CurrentState);
            Assert.IsNull(_hero.LostTargetForTest, "10 秒內抵達並放棄（記憶清掉）");
            Assert.AreEqual(PlayerState.Idle, _hero.StateMachine.CurrentState);
            Assert.Greater(_hero.transform.position.y, 0.9f, "停在崖台上");
            Vector3 rest = _hero.transform.position;
            for (int i = 0; i < 60 * 8; i++)                            // 跨過牆的 5 秒壽命
            {
                yield return null;
                Assert.IsNull(_hero.LostTargetForTest, "牆到期後記憶不復活");
            }
            Assert.AreEqual(0f, Vector3.Distance(rest, _hero.transform.position), 0.05f, "放棄後不再移動（無抖動）");
            Assert.IsNull(_hero.CurrentTarget);
            Assert.AreEqual(_health0, _red.HealthNormalized, "沒有出手");
        }

        // K5a：兩個目標同時失去視野（鎖定的 A、另一個 B 都看不見）→ 只走向被鎖定的 A 的最後位置，不會改去 B、不抖動，接回後只打 A。
        [UnityTest]
        public IEnumerator K5a_TwoTargetsBothUnseen_HeroGoesOnlyForTheLockedOne()
        {
            yield return LockAndLose();
            TrainingOpponent b = SpawnBystander(new Vector3(0f, -1f, 0f));       // 谷底中央（tile 0）
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_red), "前提：A 看不見");
            Assert.IsFalse(_hero.CanEngage(b), "前提：B 也看不見");
            var w = new Watch();
            yield return RunUntilHit(20f, w, b);
            Debug.Log("[CAMERA LAB TEST] K5a stalls=" + w.Stalls + " lostRises=" + w.LostRises + " minDistToB=" + w.MinDistToBystander.ToString("F2"));
            Assert.Less(_red.HealthNormalized, _health0, "20 秒內接回 A 並打到");
            Assert.IsFalse(w.LockedBystander, "從沒鎖過 B");
            Assert.AreEqual(1f, b.HealthNormalized, "B 沒被打");
            Assert.AreEqual(0, w.Stalls, "沒卡死");
            Assert.AreEqual(0, w.LostRises, "記憶沒有在兩個目標間來回重建（無抖動）");
        }

        // K5b：A 失去視野，但另一個目標 B 仍看得見、在射程內 → 英雄不改打 B（不會被可見目標劫持），照走向 A 的最後位置。
        [UnityTest]
        public IEnumerator K5b_OtherTargetStaysVisible_DoesNotHijackTheChase()
        {
            yield return LockAndLose();
            TrainingOpponent b = SpawnBystander(new Vector3(-6.6f, 1f, -1.0f));  // 崖台上、離英雄約 3m
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_red), "前提：A 看不見");
            Assert.IsTrue(_hero.CanEngage(b), "前提：B 看得見");
            var w = new Watch();
            yield return RunUntilHit(20f, w, b);
            Debug.Log("[CAMERA LAB TEST] K5b stalls=" + w.Stalls + " lostRises=" + w.LostRises + " minDistToB=" + w.MinDistToBystander.ToString("F2"));
            Assert.Less(_red.HealthNormalized, _health0, "20 秒內接回 A 並打到");
            Assert.IsFalse(w.LockedBystander, "看得見的 B 沒有劫持追擊");
            Assert.AreEqual(1f, b.HealthNormalized, "B 沒被打");
            Assert.AreEqual(0, w.Stalls);
            Assert.AreEqual(0, w.LostRises);
        }
    }
}
#endif
