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
    // D4：按鈕路徑在 Update 夾區內送出（同 ZeroAllocationTests 的 H2 規則）。
    public sealed class LabButtonDriver : MonoBehaviour
    {
        internal PlayerInputService Input;
        internal CameraComparisonLab Lab;
        public int Frame;
        public int Locks;

        private void Update()
        {
            if (Input == null) return;
            Frame++;
            if (Frame % 40 == 1)
            {
                ScreenRegion a = Lab.ActionButtonLayout.Attack;
                Input.SendScreenTap((a.XMin + a.XMax) * .5f, (a.YMin + a.YMax) * .5f);
                if (Lab.LastAimTarget != null) Locks++;
            }
            else if (Frame % 40 == 21)
            {
                ScreenRegion d = Lab.ActionButtonLayout.Dash;
                Input.SendScreenTap((d.XMin + d.XMax) * .5f, (d.YMin + d.YMax) * .5f);
            }
        }
    }

    // docs/CAMERA_LAB_COMBAT_PLAN.md §3 凍結驗收 D（與 E 的真場景多指部分）。全部走真路由（SendScreenTap／SimulatedHold）。
    public sealed class CameraLabCombatPlayTests
    {
        private CameraComparisonLab _lab;
        private PlayerInputService _input;
        private HeroController _hero;
        private Phase1Bootstrap _bootstrap;

        private IEnumerator Load(bool third = true)
        {
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null; yield return null;
            _lab = Object.FindObjectOfType<CameraComparisonLab>();
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _input = Object.FindObjectOfType<PlayerInputService>();
            _hero = _lab.FollowedHero;
            if (third) _lab.SetThirdPerson(true);
            yield return null;
        }

        [TearDown]
        public void Cleanup()
        {
            if (_input != null) { _input.EndSimulatedHold(0); _input.EndSimulatedHold(1); }
            if (_lab != null) _lab.enabled = false;
            Time.captureDeltaTime = 0f;
            AllocationProbe.Measuring = false;
        }

        private static Vector2 Center(ScreenRegion r) => new Vector2((r.XMin + r.XMax) * .5f, (r.YMin + r.YMax) * .5f);
        private void TapAttack() { Vector2 c = Center(_lab.ActionButtonLayout.Attack); _input.SendScreenTap(c.x, c.y); }
        private void TapDash() { Vector2 c = Center(_lab.ActionButtonLayout.Dash); _input.SendScreenTap(c.x, c.y); }

        private IEnumerator WarpAndSettle(Vector3 position)
        {
            _hero.GetComponent<HeroLocomotion>().WarpTo(position);
            yield return null; yield return null;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        [UnityTest] // D1
        public IEnumerator D1_AttackButton_LocksDummyAhead_AndHitsIt()
        {
            yield return Load();
            Assert.IsTrue(_lab.ActionButtonsActive);
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);
            Assert.AreEqual(0f, _lab.YawDegrees);
            float health = dummy.Health;
            TapAttack();
            Assert.AreEqual(1, _lab.AimAttackCount);
            Assert.AreSame(dummy, _lab.LastAimTarget, "準星錐應挑正前方木樁");
            Assert.AreSame(dummy, _hero.CurrentTarget, "按下當下就走原普攻鎖定");
            float deadline = Time.time + 1f;
            while (dummy.Health >= health && Time.time < deadline) yield return null;
            Assert.Less(dummy.Health, health, "1.0s 內必須真的命中");
        }

        [UnityTest] // F3：木樁在背後 3m（錐外）→退回最近者，真的命中；背後 9m→不出手
        public IEnumerator F3_AttackButton_ConeEmpty_FallsBackToNearestWithinEightMetres()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.forward * 9f);
            Assert.AreEqual(0f, _lab.YawDegrees);
            TapAttack();
            Assert.AreEqual(1, _lab.AimAttackCount);
            Assert.IsNull(_lab.LastAimTarget, "背後 9m 超出 8m：不出手");

            yield return WarpAndSettle(dummy.transform.position + Vector3.forward * 3f);
            float health = dummy.Health;
            TapAttack();
            Assert.AreEqual(2, _lab.AimAttackCount);
            Assert.AreSame(dummy, _lab.LastAimTarget, "錐內無目標→退回背後 3m 的木樁");
            Assert.AreSame(dummy, _hero.CurrentTarget, "按下當下就走原普攻鎖定");
            float deadline = Time.time + 1f;
            while (dummy.Health >= health && Time.time < deadline) yield return null;
            Assert.Less(dummy.Health, health, "1.0s 內必須真的命中（含轉身）");
        }

        // 與英雄同高度、離英雄 distance 公尺、相對 +z 偏 degrees 度（往 +x）的複製木樁，已登記進名冊。
        private DummyTarget SpawnDummyAt(DummyTarget template, float degrees, float distance)
        {
            Vector3 h = _hero.transform.position;
            float r = degrees * Mathf.Deg2Rad;
            Vector3 p = new Vector3(h.x + Mathf.Sin(r) * distance, template.transform.position.y, h.z + Mathf.Cos(r) * distance);
            DummyTarget clone = Object.Instantiate(template, p, template.transform.rotation);
            _bootstrap.RegisterElementTarget(clone);
            return clone;
        }

        [UnityTest] // G5：兩隻木樁——黏性、瞄準覆寫、預覽＝按下結果
        public IEnumerator G5_TwoDummies_StickyKeepsCurrentTarget_SwitchesWhenAimMovesFar_PreviewMatchesAttack()
        {
            yield return Load();
            DummyTarget a = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(a.transform.position + Vector3.back * 2f);
            float d = _hero.AttackRange * 0.6f;
            Vector3 h = _hero.transform.position;
            a.transform.position = new Vector3(h.x, a.transform.position.y, h.z + d);   // 在攻擊範圍內：英雄只打、不走位
            DummyTarget b = SpawnDummyAt(a, 25f, d);
            yield return null; yield return null;
            Assert.AreEqual(0f, _lab.YawDegrees);

            Assert.AreSame(a, _lab.PreviewTarget, "yaw 0：A 夾角 0°、B 25° → 預覽 A");
            TapAttack();
            Assert.AreSame(a, _lab.LastAimTarget);
            Assert.AreSame(_lab.PreviewTarget, _lab.LastAimTarget, "預覽＝按下結果（第 1 次）");
            Assert.AreSame(a, _hero.CurrentTarget);

            _lab.RotateThirdPerson(17f);   // A 17°、B 8°：B 只靠準星 9° < 15° → 黏住 A
            yield return null;
            Assert.AreSame(a, _lab.PreviewTarget, "黏性：預覽仍是 A");
            TapAttack();
            Assert.AreSame(a, _lab.LastAimTarget, "黏性：按下仍打 A");
            Assert.AreSame(_lab.PreviewTarget, _lab.LastAimTarget, "預覽＝按下結果（第 2 次）");

            _lab.RotateThirdPerson(8f);    // yaw 25：B 0°、A 25° → B 靠準星 25° > 15° → 換 B
            yield return null;
            Assert.AreSame(b, _lab.PreviewTarget, "瞄準覆寫：預覽換成 B");
            TapAttack();
            Assert.AreSame(b, _lab.LastAimTarget, "瞄準覆寫：按下打 B");
            Assert.AreSame(_lab.PreviewTarget, _lab.LastAimTarget, "預覽＝按下結果（第 3 次）");
            Assert.AreSame(b, _hero.CurrentTarget);
        }

        [UnityTest] // G6：預覽只在第三人稱有值；切回俯視清掉
        public IEnumerator G6_PreviewOnlyInThirdPerson_ClearedWhenSwitchingToTop()
        {
            yield return Load();
            DummyTarget a = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(a.transform.position + Vector3.back * 3f);
            yield return null;
            Assert.AreSame(a, _lab.PreviewTarget, "第三人稱正前方木樁→有預覽");
            Assert.IsTrue(_lab.PreviewInCone);
            _lab.SetThirdPerson(false);
            yield return null;
            Assert.IsNull(_lab.PreviewTarget, "俯視不畫標記");
            _lab.SetThirdPerson(true);
            yield return WarpAndSettle(a.transform.position + Vector3.forward * 3f);   // 木樁在背後：錐外退回
            yield return null;
            Assert.AreSame(a, _lab.PreviewTarget);
            Assert.IsFalse(_lab.PreviewInCone, "錐外退回→標記改淡黃");
        }

        private static void SetOwner(CombatTargetBehaviour target, Faction owner)
        {
            typeof(CombatTargetBehaviour).GetField("_ownerFaction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(target, owner);
        }

        // 把場景的一面測試石牆（4×2.5×0.6m，長軸沿 x）搬到 center，設成己方：不當目標、但仍是石牆會擋視線（H 修訂）。
        private TestWallTarget PlaceOwnTestWall(Vector3 center)
        {
            TestWallTarget wall = null;
            foreach (TestWallTarget w in Object.FindObjectsOfType<TestWallTarget>())
                if (w.IsAlive && !w.IsCaptureSuppressed) { wall = w; break; }
            Assert.IsNotNull(wall, "場景要有可用的測試石牆");
            SetOwner(wall, _hero.HeroFaction);
            wall.transform.SetPositionAndRotation(new Vector3(center.x, center.y + 1.25f, center.z), Quaternion.identity);
            Physics.SyncTransforms();
            return wall;
        }

        [UnityTest] // H2(a)：錐外，被擋的木樁較近、沒被擋的較遠→打沒被擋的
        public IEnumerator H2a_ConeEmpty_SkipsDummyBehindBlocker_PicksClearOne()
        {
            yield return Load();
            DummyTarget a = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(a.transform.position + Vector3.forward * 3f);   // A 在背後 3m
            PlaceOwnTestWall(a.transform.position + Vector3.forward * 1.5f);
            DummyTarget b = SpawnDummyAt(a, 110f, 5f);   // 110°：視線在牆端外 1m 以上
            yield return null; yield return null;
            Assert.AreSame(b, _lab.PreviewTarget, "預覽：跳過牆後的 A、改標 B");
            Assert.IsFalse(_lab.PreviewInCone);
            TapAttack();
            Assert.AreSame(b, _lab.LastAimTarget, "按下打 B");
        }

        [UnityTest] // H2(b)：錐外只剩被擋的→不出手
        public IEnumerator H2b_ConeEmpty_OnlyBlockedDummy_NoAttack()
        {
            yield return Load();
            DummyTarget a = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(a.transform.position + Vector3.forward * 3f);
            PlaceOwnTestWall(a.transform.position + Vector3.forward * 1.5f);
            yield return null; yield return null;
            Assert.IsNull(_lab.PreviewTarget, "被擋→沒有預覽標記");
            TapAttack();
            Assert.AreEqual(1, _lab.AimAttackCount);
            Assert.IsNull(_lab.LastAimTarget, "被擋→不出手");
        }

        [UnityTest] // H2(c)：錐內被擋仍打（瞄準優先）
        public IEnumerator H2c_InCone_BlockedDummy_StillPicked()
        {
            yield return Load();
            DummyTarget a = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(a.transform.position + Vector3.back * 3f);    // A 在正前方 3m
            PlaceOwnTestWall(a.transform.position + Vector3.back * 1.5f);
            yield return null; yield return null;
            Assert.AreSame(a, _lab.PreviewTarget);
            Assert.IsTrue(_lab.PreviewInCone);
            TapAttack();
            Assert.AreSame(a, _lab.LastAimTarget, "錐內不看視線");
        }

        // H2(d) 加嚴：測試石牆長 4m，牆心一定比牆後木樁近——牆若是候選，「不選木樁」沒有視線檢查也成立（零鑑別力）。
        // 所以把它設成己方牆：不當目標（與點擊同規則）、但仍是石牆會擋視線 → 唯一候選是牆後木樁，應不出手。
        [UnityTest] // H2(d)：場景既有測試石牆（設為己方）擋在中間（錐外）→不選牆後木樁
        public IEnumerator H2d_RealTestWall_BlocksFallbackToDummyBehindIt()
        {
            yield return Load();
            TestWallTarget wall = null;
            foreach (TestWallTarget w in Object.FindObjectsOfType<TestWallTarget>())
                if (w.IsAlive && !w.IsCaptureSuppressed) { wall = w; break; }
            Assert.IsNotNull(wall, "場景要有可用的測試石牆");
            SetOwner(wall, _hero.HeroFaction);
            BoxCollider box = wall.GetComponent<BoxCollider>();
            Vector3 size = Vector3.Scale(box.size, wall.transform.lossyScale);
            Vector3 n = Mathf.Abs(size.x) < Mathf.Abs(size.z) ? wall.transform.right : wall.transform.forward;   // 牆的薄軸
            n.y = 0f; n.Normalize();
            Vector3 c = wall.transform.TransformPoint(box.center);
            DummyTarget a = Object.FindObjectOfType<DummyTarget>();
            float thin = Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.z));
            yield return WarpAndSettle(new Vector3(c.x, _hero.transform.position.y, c.z) + n * (thin * 0.5f + 1.2f));
            a.transform.position = new Vector3(c.x, a.transform.position.y, c.z) - n * (thin * 0.5f + 1.2f);
            Physics.SyncTransforms();
            _lab.RotateThirdPerson(Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg);   // 準星背對牆：木樁與牆都在錐外
            yield return null; yield return null;
            Assert.IsNull(_lab.PreviewTarget, "牆後木樁不得被預覽、己方牆不是目標");
            TapAttack();
            Assert.IsNull(_lab.LastAimTarget, "牆後木樁不得被選");
        }

        // I1：佔領模式停用的木樁／測試牆（看不見、點不到）不得被 ATK／預覽挑中（峽谷佔領大廳探針發現）。
        [UnityTest]
        public IEnumerator I1_CaptureLobby_SuppressedDummiesAndWalls_AreNeverAimed()
        {
            yield return Load(false);
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float sx, out float sy));
            _bootstrap.WorldTapInput.SendScreenTap(sx, sy);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState);
            TrainingOpponent red = Object.FindObjectOfType<TrainingOpponent>();
            _bootstrap.HoldOpponentForTest(true);
            _lab.SetThirdPerson(true);
            yield return null; yield return null;

            CanyonTerrainSpec t = CanyonTerrainSpec.V0140;
            CaptureBoardSpec board = t.Board;
            HeroLocomotion move = _hero.GetComponent<HeroLocomotion>();
            Vector3 hp = new Vector3(board.CenterX(0), t.TileHeight(0), board.CenterZ(0));
            Vector3 rp = new Vector3(board.CenterX(1), t.TileHeight(1), board.CenterZ(1));
            move.WarpTo(hp);
            red.RespawnAt(rp);
            for (int i = 0; i < 4; i++) yield return null;
            Vector3 h = _hero.transform.position, r = red.transform.position;
            _lab.RotateThirdPerson(Mathf.Atan2(r.x - h.x, r.z - h.z) * Mathf.Rad2Deg - _lab.YawDegrees);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreSame(red, _lab.PreviewTarget, "對準對手→預覽是對手，不是隱形木樁");
            TapAttack();
            Assert.AreSame(red, _lab.LastAimTarget);

            Vector3[] spots = { hp, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -6f) };
            for (int s = 0; s < spots.Length; s++)
            {
                move.WarpTo(spots[s]);
                for (int i = 0; i < 3; i++) yield return null;
                for (int k = 0; k < 12; k++)
                {
                    _lab.RotateThirdPerson(30f);
                    yield return null;
                    ICombatTarget p = _lab.PreviewTarget;
                    Assert.IsFalse(p is DummyTarget, "位置 " + s + " 方向 " + k * 30 + "：預覽挑到停用的木樁");
                    Assert.IsFalse(p is TestWallTarget, "位置 " + s + " 方向 " + k * 30 + "：預覽挑到停用的測試牆");
                }
            }
        }

        // 峽谷對局中（對手 Hold）、第三人稱。大廳裡鎖定紅色＝點紅開局（英雄會被送回出生點），所以先在俯視下點紅開局。
        private TrainingOpponent _red;
        private IEnumerator StartCanyonMatchThirdPerson()
        {
            yield return Load(false);
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

        // 英雄放 heroAt、對手放 redAt，準星對準（aimAtRed）或背對對手。
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

        [UnityTest] // J1：崖台→谷底 7.58m（超出射程、跨崖走不到），錐外不挑
        public IEnumerator J1_Canyon_PlateauToValleyOutOfRange_FallbackDoesNotPick()
        {
            yield return StartCanyonMatchThirdPerson();
            yield return PlaceCanyon(TileCenter(5), TileCenter(4), false);
            Assert.AreEqual(1f, _hero.transform.position.y, 0.05f, "英雄在崖台");
            Assert.AreEqual(-1f, _red.transform.position.y, 0.05f, "對手在谷底");
            Assert.IsTrue(_hero.CanEngage(_red), "前提：視野規則允許交戰");
            Assert.IsFalse(_hero.IsTargetInAttackRange(_red), "前提：超出射程");
            Assert.IsNull(_lab.PreviewTarget, "打不到也走不到→錐外不挑");
            TapAttack();
            Assert.IsNull(_lab.LastAimTarget);
        }

        [UnityTest] // J2：崖下 3.4m（射程內），錐外照挑，且真的打得到
        public IEnumerator J2_Canyon_ValleyTargetBelowCliffWithinRange_FallbackPicksAndHits()
        {
            yield return StartCanyonMatchThirdPerson();
            CanyonTerrainSpec t = CanyonTerrainSpec.V0140;
            Vector3 a = TileCenter(5), c = TileCenter(4);
            a.y = 0f; c.y = 0f;
            Vector3 dir = (c - a).normalized;
            float edge = -1f;
            for (float s = 0f; s < Vector3.Distance(a, c); s += 0.02f)
            {
                Vector3 q = a + dir * s;
                if (t.HeightAt(q.x, q.z, 0) < 0.5f) { edge = s; break; }
            }
            Assert.Greater(edge, 0f, "找得到崖緣");
            Vector3 hp = a + dir * (edge - 0.4f); hp.y = 1f;
            Vector3 rp = a + dir * (edge + 3.0f); rp.y = -1f;
            yield return PlaceCanyon(hp, rp, false);
            Assert.IsTrue(_hero.IsTargetInAttackRange(_red), "前提：崖下 3.4m 在射程內");
            Assert.AreSame(_red, _lab.PreviewTarget, "射程內→錐外照挑（崖壁不擋視線）");
            Assert.IsFalse(_lab.PreviewInCone);
            float health = _red.HealthNormalized;
            TapAttack();
            Assert.AreSame(_red, _lab.LastAimTarget);
            float deadline = Time.time + 2f;
            while (_red.HealthNormalized >= health && Time.time < deadline) yield return null;
            Assert.Less(_red.HealthNormalized, health, "2s 內真的打到");
        }

        [UnityTest] // J3：J1 同位置但準星對準（錐內）→ 照挑（錐內不受 J 限制）
        public IEnumerator J3_Canyon_PlateauToValleyOutOfRange_InConeStillPicks()
        {
            yield return StartCanyonMatchThirdPerson();
            yield return PlaceCanyon(TileCenter(5), TileCenter(4), true);
            Assert.AreSame(_red, _lab.PreviewTarget);
            Assert.IsTrue(_lab.PreviewInCone);
        }

        // 鎖定後等到「失去視野、目標被清掉」那一幀（最多 2 秒）。
        private IEnumerator WaitForLoss()
        {
            for (int i = 0; i < 120 && _hero.CurrentTarget != null; i++) yield return null;
            Assert.IsNull(_hero.CurrentTarget, "前提：2 秒內因視野失去目標");
        }

        [UnityTest] // K1：崖台鎖定谷底對手 → 走到最後看見的位置（經斜坡）→ 重新看見就接回並打到
        public IEnumerator K1_Canyon_LostSightChase_WalksRampAndHits()
        {
            yield return StartCanyonMatchThirdPerson();
            yield return PlaceCanyon(TileCenter(5), TileCenter(4), true);
            float health = _red.HealthNormalized;
            TapAttack();
            Assert.AreSame(_red, _lab.LastAimTarget);
            float deadline = Time.time + 20f;
            while (_red.HealthNormalized >= health && Time.time < deadline) yield return null;
            Debug.Log("[CAMERA LAB TEST] K1 heroY=" + _hero.transform.position.y.ToString("F2") + " t=" + (20f - (deadline - Time.time)).ToString("F1"));
            Assert.Less(_red.HealthNormalized, health, "20 秒內經斜坡下到谷底打到對手");
            Assert.Less(_hero.transform.position.y, 0.9f, "英雄已離開崖台（崖壁擋路，只能經斜坡）；途中看見即接回，可能在斜坡上就出手");
        }

        [UnityTest] // K2：失去視野後不追即時位置——對手被搬走，英雄停在舊的最後位置、不出手
        public IEnumerator K2_Canyon_LostSight_GoesToLastSeenNotLivePosition()
        {
            yield return StartCanyonMatchThirdPerson();
            Vector3 lastSeen = TileCenter(4);
            yield return PlaceCanyon(TileCenter(5), lastSeen, true);
            float health = _red.HealthNormalized;
            TapAttack();
            yield return WaitForLoss();
            _red.RespawnAt(TileCenter(1));   // 谷底另一端，離舊位置約 15m：英雄抵達舊位置時也看不見
            yield return null;
            float deadline = Time.time + 25f;
            while (_hero.LostTargetForTest != null && Time.time < deadline)
            {
                Assert.IsNull(_hero.CurrentTarget, "看不見期間不得重新鎖定（不追即時位置）");
                yield return null;
            }
            Vector3 h = _hero.transform.position;
            float off = new Vector2(h.x - lastSeen.x, h.z - lastSeen.z).magnitude;
            Debug.Log("[CAMERA LAB TEST] K2 stopOffset=" + off.ToString("F2") + " heroY=" + h.y.ToString("F2"));
            Assert.LessOrEqual(off, 1.5f, "停在舊的最後看見位置附近");
            Assert.IsNull(_hero.CurrentTarget);
            Assert.AreEqual(health, _red.HealthNormalized, "沒有出手");
        }

        [UnityTest] // K3：失去視野後玩家推搖桿＝新指令 → 取消記憶，不自動接回
        public IEnumerator K3_Canyon_LostSight_PlayerMoveCancelsMemory()
        {
            yield return StartCanyonMatchThirdPerson();
            yield return PlaceCanyon(TileCenter(5), TileCenter(4), true);
            float health = _red.HealthNormalized;
            TapAttack();
            yield return WaitForLoss();
            Assert.AreSame(_red, _hero.LostTargetForTest, "前提：進入失去視野的追擊記憶");
            Vector2 stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
            _input.BeginSimulatedHold(0, stick.x, stick.y);
            _input.MoveSimulatedHold(0, stick.x, stick.y + 40f);
            for (int i = 0; i < 10; i++) yield return null;
            _input.EndSimulatedHold(0);
            Assert.IsNull(_hero.LostTargetForTest, "搖桿新指令取消追擊記憶");
            for (int i = 0; i < 180; i++) yield return null;
            Assert.IsNull(_hero.CurrentTarget, "不自動接回");
            Assert.AreEqual(health, _red.HealthNormalized);
        }

        [UnityTest] // D1／E：搖桿按住推動中同時按 ATK
        public IEnumerator D1_StickHeldWhileAttack_StillLocksDummy_AndKeepsStick()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3.5f);
            Vector2 stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
            _input.BeginSimulatedHold(0, stick.x, stick.y);
            _input.MoveSimulatedHold(0, stick.x + 8f, stick.y);
            yield return null;
            Assert.IsTrue(_input.ContinuousRouter.MoveHeld);
            float moveX = _input.ContinuousRouter.MoveX;
            Assert.Greater(moveX, 0f);
            TapAttack();
            Assert.AreSame(dummy, _hero.CurrentTarget);
            Assert.IsTrue(_input.ContinuousRouter.MoveHeld, "ATK 不得搶走搖桿");
            Assert.AreEqual(moveX, _input.ContinuousRouter.MoveX);
            _input.EndSimulatedHold(0);
        }

        [UnityTest] // D2
        public IEnumerator D2_DashButton_Moves1_4mAlongCameraForward_AndFollowsYaw()
        {
            yield return Load();
            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            int charges = _hero.Mover.CurrentCharges;
            Assert.AreEqual(3, charges);
            Vector3 start = Flat(_hero.transform.position);
            TapDash();
            Assert.AreEqual(ActiveDashOutcome.FreeDash, _lab.LastDashOutcome);
            for (int i = 0; i < 18; i++) yield return null;
            Vector3 delta = Flat(_hero.transform.position) - start;
            Debug.Log("[CAMERA LAB TEST] D2 yaw0 delta=" + delta.ToString("F4"));
            Assert.That(delta.z, Is.EqualTo(1.4f).Within(.05f));
            Assert.Less(Mathf.Abs(delta.x), .05f);
            Assert.AreEqual(charges - 1, _hero.Mover.CurrentCharges);

            for (int i = 0; i < 66; i++) yield return null; // 讓 1.0s 連段窗口過期
            _lab.RotateThirdPerson(90f);
            start = Flat(_hero.transform.position);
            int before = _hero.Mover.CurrentCharges;
            TapDash();
            for (int i = 0; i < 18; i++) yield return null;
            delta = Flat(_hero.transform.position) - start;
            Debug.Log("[CAMERA LAB TEST] D2 yaw90 delta=" + delta.ToString("F4"));
            Assert.That(delta.x, Is.EqualTo(1.4f).Within(.05f));
            Assert.Less(Mathf.Abs(delta.z), .05f);
            Assert.AreEqual(before - 1, _hero.Mover.CurrentCharges);
        }

        [UnityTest] // D2：搖桿推著時按 DASH——方向跟搖桿，滑步期間不疊步行
        public IEnumerator D2_DashWhileStickPushed_FollowsStick_AndDoesNotStackWalking()
        {
            yield return Load();
            yield return WarpAndSettle(new Vector3(-2f, 0f, -4f));
            Vector2 stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
            _input.BeginSimulatedHold(0, stick.x, stick.y);
            _input.MoveSimulatedHold(0, stick.x + _input.ContinuousRouter.JoystickRadiusPixels, stick.y);
            for (int i = 0; i < 4; i++) yield return null;
            Vector3 walkStart = Flat(_hero.transform.position);
            yield return null;
            Assert.Greater((Flat(_hero.transform.position) - walkStart).x, .01f, "前置：搖桿確實在推（往 +x 步行）");

            Vector3 start = Flat(_hero.transform.position);
            TapDash();
            Assert.AreEqual(ActiveDashOutcome.FreeDash, _lab.LastDashOutcome);
            Assert.IsTrue(_hero.Mover.IsDashing);
            int frames = 0;
            while (_hero.Mover.IsDashing && frames++ < 60) yield return null;
            Vector3 delta = Flat(_hero.transform.position) - start;
            Debug.Log("[CAMERA LAB TEST] D2 stick delta=" + delta.ToString("F4") + " frames=" + frames);
            Assert.That(delta.x, Is.EqualTo(1.4f).Within(.05f), "滑步期間只有滑步位移，不疊步行");
            Assert.Less(Mathf.Abs(delta.z), .05f);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Greater(Flat(_hero.transform.position).x, start.x + 1.4f + .01f, "滑步結束後搖桿步行恢復");
            _input.EndSimulatedHold(0);
        }

        [UnityTest] // D2：追擊導航中按 DASH——滑步期間不疊導航步進
        public IEnumerator D2_DashWhileChasing_DoesNotStackNavigation()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 7.5f);
            Assert.Greater(7.5f, _hero.AttackRange, "前置：木樁在射程外");
            TapAttack();
            Assert.AreSame(dummy, _hero.CurrentTarget);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.AreEqual(PlayerState.Moving, _hero.StateMachine.CurrentState, "前置：正在導航追擊");
            Vector3 walk = Flat(_hero.transform.position);
            yield return null;
            Assert.Greater(Flat(_hero.transform.position).z - walk.z, .01f, "前置：導航確實在走");

            Vector3 start = Flat(_hero.transform.position);
            TapDash();
            Assert.AreEqual(ActiveDashOutcome.FreeDash, _lab.LastDashOutcome);
            int frames = 0;
            while (_hero.Mover.IsDashing && frames++ < 60) yield return null;
            Vector3 delta = Flat(_hero.transform.position) - start;
            Debug.Log("[CAMERA LAB TEST] D2 chase delta=" + delta.ToString("F4") + " frames=" + frames);
            Assert.That(delta.z, Is.EqualTo(1.4f).Within(.05f), "滑步期間只有滑步位移，不疊導航");
            Assert.Less(Mathf.Abs(delta.x), .05f);
        }

        [UnityTest] // E（真場景）：轉頭按住拖曳中同時按 DASH
        public IEnumerator E_LookHeldWhileDash_BothWork()
        {
            yield return Load();
            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            _input.BeginSimulatedHold(1, Screen.width * .6f, Screen.height * .55f);
            _input.MoveSimulatedHold(1, Screen.width * .7f, Screen.height * .55f);
            yield return null;
            Assert.Greater(_lab.YawDegrees, 0f);
            float yaw = _lab.YawDegrees;
            Vector3 start = Flat(_hero.transform.position);
            TapDash();
            Assert.AreEqual(1, _lab.ActiveDashCount);
            _input.MoveSimulatedHold(1, Screen.width * .75f, Screen.height * .55f);
            for (int i = 0; i < 18; i++) yield return null;
            Assert.Greater(_lab.YawDegrees, yaw, "DASH 不得中斷轉頭");
            Assert.That((Flat(_hero.transform.position) - start).magnitude, Is.EqualTo(1.4f).Within(.05f));
            _input.EndSimulatedHold(1);
        }

        private static RuneWall AliveWall(RuneCaster caster)
        {
            foreach (RuneWall wall in caster.Pool) if (wall.IsAlive) return wall;
            return null;
        }

        private IEnumerator DragRuneAndRelease(Vector2 screenDirection, RuneGhostPreview ghost, float yaw)
        {
            RuneButtonLayout layout = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter);
            Vector2 rune = Center(layout.Button);
            float reach = _input.RuneSaturationPixels * 1.2f;
            _input.BeginSimulatedHold(0, rune.x, rune.y);
            _input.MoveSimulatedHold(0, rune.x + screenDirection.x * reach, rune.y + screenDirection.y * reach);
            yield return null;
            RuneCaster caster = Object.FindObjectOfType<RuneCaster>();
            Assert.IsNull(AliveWall(caster), "放手前不得成牆");
            Vector3 ghostPosition = ghost.transform.position;
            _input.EndSimulatedHold(0);
            RuneWall wall = AliveWall(caster);
            Assert.IsNotNull(wall, "放手成牆");
            CameraLabAim.GroundForward(yaw, out float ax, out float az);
            Vector3 aim = new Vector3(ax, 0f, az);
            Vector3 expected = Flat(_hero.transform.position) + aim * 8f;
            Debug.Log("[CAMERA LAB TEST] D3 yaw" + yaw + " wall=" + wall.transform.position.ToString("F3") + " expected=" + expected.ToString("F3"));
            Assert.Less(Vector3.Distance(Flat(wall.transform.position), expected), .1f, "牆在準星前方 8m");
            Assert.GreaterOrEqual(Vector3.Dot(Flat(wall.transform.forward).normalized, aim), .999f, "牆正對鏡頭前方");
            Assert.Less(Vector3.Distance(Flat(ghostPosition), Flat(wall.transform.position)), .05f, "虛影與實牆同一換算");
        }

        [UnityTest] // D3
        public IEnumerator D3_RuneDragInThirdPerson_PlacesWallAlongCameraForward()
        {
            yield return Load();
            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            RuneGhostPreview ghost = Object.FindObjectOfType<RuneGhostPreview>();
            RuneCaster caster = Object.FindObjectOfType<RuneCaster>();
            yield return DragRuneAndRelease(new Vector2(0f, 1f), ghost, 0f);
            caster.ResetForRound();
            yield return null;
            _lab.RotateThirdPerson(90f);
            yield return WarpAndSettle(new Vector3(-6f, 0f, -2f));
            yield return DragRuneAndRelease(new Vector2(-1f, 0f), ghost, 90f);
        }

        [UnityTest] // D5
        public IEnumerator D5_TopDown_ButtonsInactive_TapThereStaysWorld_RuneUsesScreenDirection()
        {
            yield return Load(false);
            Assert.IsFalse(_lab.IsThirdPerson);
            Assert.IsFalse(_lab.ActionButtonsActive);
            int moves = 0, ui = 0, buttons = 0;
            _input.OnMoveDestinationSelected += _ => moves++;
            _input.OnUiRegionTapped += _ => ui++;
            _input.OnLabActionButton += _ => buttons++;
            Vector2 atk = Center(_lab.ActionButtonLayout.Attack);
            Vector2 dash = Center(_lab.ActionButtonLayout.Dash);
            TouchRoute atkRoute = _input.Routing.Route(atk.x, atk.y, Screen.width, Screen.height, _input.ActiveMode, out _);
            TouchRoute dashRoute = _input.Routing.Route(dash.x, dash.y, Screen.width, Screen.height, _input.ActiveMode, out _);
            Debug.Log("[CAMERA LAB TEST] D5 route at ATK=" + atkRoute + " DASH=" + dashRoute);
            _input.SendScreenTap(atk.x, atk.y);
            Assert.AreEqual(1, moves, "俯視點 ATK 位置＝原點地移動");
            _input.SendScreenTap(dash.x, dash.y);
            Assert.AreEqual(2, moves, "俯視點 DASH 位置＝原點地移動");
            Assert.AreEqual(TouchRoute.World, atkRoute, "俯視下 ATK 位置是世界路由");
            Assert.AreEqual(TouchRoute.World, dashRoute, "俯視下 DASH 位置是世界路由");
            Assert.AreEqual(0, buttons, "俯視路由層不得送出按鈕事件");
            Assert.AreEqual(0, _lab.AimAttackCount);
            Assert.AreEqual(0, _lab.ActiveDashCount);
            Assert.AreEqual(0, ui);

            yield return AssertTopRuneUsesScreenDirection();
        }

        [UnityTest] // D5：THIRD → TOP 之後覆寫必須清掉
        public IEnumerator D5_ThirdThenTop_RuneBackToScreenDirection_AndButtonsOff()
        {
            yield return Load();
            Assert.IsTrue(_lab.ActionButtonsActive);
            _lab.RotateThirdPerson(90f);
            _lab.SetThirdPerson(false);
            yield return null;
            Assert.IsFalse(_lab.ActionButtonsActive);
            Assert.IsFalse(_input.ContinuousRouter.ActionButtonsEnabled);
            yield return AssertTopRuneUsesScreenDirection();
        }

        private IEnumerator AssertTopRuneUsesScreenDirection()
        {
            yield return WarpAndSettle(new Vector3(2f, 0f, -4f));
            RuneButtonLayout layout = RuneButtonLayout.Compute(Screen.width, Screen.height, _input.PixelsPerMillimeter);
            Vector2 rune = Center(layout.Button);
            _input.BeginSimulatedHold(0, rune.x, rune.y);
            _input.MoveSimulatedHold(0, rune.x - _input.RuneSaturationPixels * 1.2f, rune.y);
            yield return null;
            Vector3 ghostPosition = Object.FindObjectOfType<RuneGhostPreview>().transform.position;
            _input.EndSimulatedHold(0);
            RuneWall wall = AliveWall(Object.FindObjectOfType<RuneCaster>());
            Assert.IsNotNull(wall);
            Assert.Less(Vector3.Distance(Flat(ghostPosition), Flat(wall.transform.position)), .05f, "TOP 虛影與實牆同一換算");
            Vector3 dir = RuneCaster.ScreenToWorldGroundDirection(new Vector2(-1f, 0f), Camera.main.transform);
            RuneCastLogic logic = new RuneCastLogic(new RuneTuning());
            Vector3 hero = _hero.transform.position;
            Assert.IsTrue(logic.TryDragPlacement(hero.x, hero.z, dir.x, dir.z, 1f, out RuneWallPlacement expected));
            Assert.That(wall.transform.position.x, Is.EqualTo(expected.CenterX).Within(.01f));
            Assert.That(wall.transform.position.z, Is.EqualTo(expected.CenterZ).Within(.01f));
        }

        [UnityTest] // D4
        public IEnumerator D4_ThirdPersonButtons_UpdateAndLateUpdate_AllocateNothing()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);

            GameObject driverObject = new GameObject("LabButtonDriver");
            LabButtonDriver driver = driverObject.AddComponent<LabButtonDriver>();
            driver.Lab = _lab;
            driver.Input = _input;
            for (int i = 0; i < 90; i++) yield return null; // 暖機：兩種按鈕各走過至少一次
            Assert.Greater(_lab.ActiveDashCount, 0, "暖機沒有真的滑步");
            Assert.Greater(driver.Locks, 0, "暖機沒有真的鎖定");

            AllocationProbe.Reset();
            GameObject rig = new GameObject("LabProbeRig");
            rig.AddComponent<AllocationProbeBegin>();
            rig.AddComponent<AllocationProbeEnd>();
            yield return null;
            int dashesBefore = _lab.ActiveDashCount, locksBefore = driver.Locks;
            AllocationProbe.Measuring = true;
            for (int i = 0; i < 180; i++) yield return null;
            AllocationProbe.Measuring = false;
            Object.Destroy(rig);
            Object.Destroy(driverObject);

            Assert.GreaterOrEqual(AllocationProbe.Frames, 180);
            Assert.Greater(_lab.ActiveDashCount, dashesBefore, "窗口內沒有滑步起手，0 byte 沒有鑑別力");
            Assert.Greater(driver.Locks, locksBefore, "窗口內沒有準星鎖定，0 byte 沒有鑑別力");
            Assert.AreEqual(0L, AllocationProbe.UpdateBytes,
                "THIRD 按鈕 Update 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.UpdateBytes + " bytes");
            Assert.AreEqual(0L, AllocationProbe.LateUpdateBytes,
                "THIRD 按鈕 LateUpdate 在 " + AllocationProbe.Frames + " 幀內配置了 " + AllocationProbe.LateUpdateBytes + " bytes");
        }
    }
}
#endif
