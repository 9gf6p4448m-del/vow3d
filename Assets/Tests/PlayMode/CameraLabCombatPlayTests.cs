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

    // 第三人稱戰鬥操作（GDD §貳.4 模式 C）試作凍結驗收 D（與 E 的真場景多指部分）（原試作計畫 docs/CAMERA_LAB_COMBAT_PLAN.md 只在 camera-lab-20261001 分支）。全部走真路由（SendScreenTap／SimulatedHold）。
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

        // ───────────── v0.16.0 武器灰盒（凍結檔 vow-toolchain/acceptance-weapons-20261002.md W3／W5／W6）─────────────

        private void TapWeapon() { Vector2 c = Center(_lab.ActionButtonLayout.Weapon); _input.SendScreenTap(c.x, c.y); }

        private void SelectWeapon(WeaponId id)
        {
            for (int i = 0; i < WeaponSelection.Count && _lab.CurrentWeapon != id; i++) TapWeapon();
            Assert.AreEqual(id, _lab.CurrentWeapon, "WPN 鈕切不到 " + id);
        }

        [UnityTest] // W3：弓對 10m 木樁按 ATK＝站著射；對照：Standard 同 10m 木樁要走近
        public IEnumerator W3_Bow_TenMetreDummy_ShootsInPlace_StandardWalksUp()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 10f);
            Assert.AreEqual(0f, _lab.YawDegrees);
            SelectWeapon(WeaponId.Bow);
            Vector3 start = Flat(_hero.transform.position);
            float health = dummy.Health;
            float damage = _hero.AttackDamage;
            Assert.AreEqual(60f, damage);
            TapAttack();
            Assert.AreSame(dummy, _lab.LastAimTarget, "弓：12° 錐內 10m 挑得到");
            float deadline = Time.time + 2f;
            while (dummy.Health >= health && Time.time < deadline) yield return null;
            Assert.AreEqual(health - damage, dummy.Health, 1e-3f, "2 秒內該木樁扣 AttackDamage");
            while (Time.time < deadline) yield return null;
            float moved = (Flat(_hero.transform.position) - start).magnitude;
            Debug.Log("[CAMERA LAB TEST] W3 bow moved=" + moved.ToString("F3"));
            Assert.LessOrEqual(moved, 0.5f, "弓：英雄水平位移 ≤ 0.5m（站著射）");

            // 對照（同場景重載、同 10m 木樁、Standard＝射程不覆寫）。
            yield return Load();
            dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 10f);
            Assert.AreEqual(WeaponId.Standard, _lab.CurrentWeapon);
            start = Flat(_hero.transform.position);
            TapAttack();
            Assert.IsNull(_lab.LastAimTarget, "Standard：10m 超出 8m 準星距離，ATK 不出手（凍結 W1 的 Standard 行為）");
            // ATK 挑到目標後送的就是這一個呼叫；直接送同一隻木樁，量 Standard 射程下英雄會不會走近。
            _input.SubmitCombatTarget(dummy);
            deadline = Time.time + 3f;
            while (Time.time < deadline) yield return null;
            moved = (Flat(_hero.transform.position) - start).magnitude;
            Debug.Log("[CAMERA LAB TEST] W3 standard moved=" + moved.ToString("F3"));
            Assert.Greater(moved, 3f, "Standard：射程 5m，英雄須走近（位移 > 3m）");
        }

        [UnityTest] // W5：WPN 鈕循環切換、不觸發 ATK／世界點擊；預設 Standard；弓射程覆寫只在第三人稱
        public IEnumerator W5_WeaponButton_CyclesWeapons_WithoutAttackOrMove()
        {
            yield return Load();
            Assert.AreEqual(WeaponId.Standard, _lab.CurrentWeapon, "預設 Standard");
            Assert.AreEqual(0f, _hero.AttackRangeOverride, "Standard 不覆寫射程");
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);
            Vector3 start = Flat(_hero.transform.position);
            WeaponId[] expected = { WeaponId.Sword, WeaponId.Bow, WeaponId.Hammer, WeaponId.Standard };
            for (int i = 0; i < expected.Length; i++)
            {
                TapWeapon();
                Assert.AreEqual(expected[i], _lab.CurrentWeapon, "第 " + (i + 1) + " 下");
                Assert.AreEqual(expected[i] == WeaponId.Bow ? 12f : 0f, _hero.AttackRangeOverride, "射程覆寫 " + expected[i]);
                yield return null;
            }
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(4, _lab.WeaponSwitchCount);
            Assert.AreEqual(0, _lab.AimAttackCount, "WPN 不觸發 ATK");
            Assert.IsNull(_hero.CurrentTarget, "WPN 不鎖定木樁");
            Assert.Less((Flat(_hero.transform.position) - start).magnitude, 0.05f, "WPN 不讓英雄移動");

            SelectWeapon(WeaponId.Bow);
            Assert.AreEqual(12f, _hero.AttackRangeOverride);
            _lab.SetThirdPerson(false);
            Assert.AreEqual(0f, _hero.AttackRangeOverride, "回俯視清掉射程覆寫");
            _lab.SetThirdPerson(true);
            Assert.AreEqual(12f, _hero.AttackRangeOverride, "回第三人稱重新套用");
        }

        [UnityTest] // W6：WPN 切換＋各武器 ATK 的 Update／LateUpdate 零配置（附正向對照）
        public IEnumerator W6_WeaponSwitchAndWeaponAttacks_AllocateNothing_PositiveControlCatchesAllocation()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            dummy.Configure(100000f, dummy.TargetFaction);   // 窗口內不死，鎖定才有鑑別力
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);

            GameObject driverObject = new GameObject("WeaponButtonDriver");
            WeaponButtonDriver driver = driverObject.AddComponent<WeaponButtonDriver>();
            driver.Lab = _lab;
            driver.Input = _input;
            for (int i = 0; i < 130; i++) yield return null; // 暖機：四把武器各按過 ATK

            AllocationProbe.Reset();
            GameObject rig = new GameObject("WeaponProbeRig");
            rig.AddComponent<AllocationProbeBegin>();
            rig.AddComponent<AllocationProbeEnd>();
            yield return null;
            int switchesBefore = _lab.WeaponSwitchCount, bowBefore = driver.BowLocks, swordBefore = driver.SwordLocks;
            int sweepsBefore = _lab.SweepResolveCount;
            AllocationProbe.Measuring = true;
            for (int i = 0; i < 180; i++) yield return null;
            AllocationProbe.Measuring = false;
            long updateBytes = AllocationProbe.UpdateBytes, lateBytes = AllocationProbe.LateUpdateBytes;
            int frames = AllocationProbe.Frames;

            // 正向對照：同一套探針＋同一個 driver，多掛一個每幀配置的元件，必須量到 > 0。
            GameObject allocator = new GameObject("DeliberateAllocator");
            allocator.AddComponent<DeliberateAllocator>();
            AllocationProbe.Reset();
            yield return null;
            AllocationProbe.Measuring = true;
            for (int i = 0; i < 30; i++) yield return null;
            AllocationProbe.Measuring = false;
            long controlBytes = AllocationProbe.UpdateBytes;
            Object.Destroy(allocator);
            Object.Destroy(rig);
            Object.Destroy(driverObject);

            Assert.GreaterOrEqual(frames, 180);
            Assert.Greater(_lab.WeaponSwitchCount, switchesBefore + 4, "窗口內 WPN 切換不足一輪");
            Assert.Greater(driver.BowLocks, bowBefore, "窗口內沒有弓的鎖定，0 byte 沒有鑑別力");
            Assert.Greater(driver.SwordLocks, swordBefore, "窗口內沒有劍的鎖定，0 byte 沒有鑑別力");
            Assert.Greater(_lab.SweepResolveCount, sweepsBefore, "窗口內沒有錘的橫掃結算，0 byte 沒有鑑別力");
            Assert.Greater(controlBytes, 0L, "正向對照量不到配置：探針失效");
            Assert.AreEqual(0L, updateBytes, "武器路徑 Update 在 " + frames + " 幀內配置了 " + updateBytes + " bytes");
            Assert.AreEqual(0L, lateBytes, "武器路徑 LateUpdate 在 " + frames + " 幀內配置了 " + lateBytes + " bytes");
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        [UnityTest] // W4：錘橫掃——錐內沒人也出手；半徑、全角、陣營、冷卻
        public IEnumerator W4_Hammer_SweepsTowardAim_RadiusAngleFactionAndCooldown()
        {
            yield return Load();
            DummyTarget front = Object.FindObjectOfType<DummyTarget>();
            front.Configure(1000f, front.TargetFaction);
            yield return WarpAndSettle(front.transform.position + Vector3.back * 3f);   // 木樁在正前 3m
            SelectWeapon(WeaponId.Hammer);

            // ① 準星轉向背面（3.5m 內沒有任何目標）：照樣起手、結算、零命中。
            _lab.RotateThirdPerson(180f);
            float frontStart = front.Health;
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "錐內沒人也出手");
            Assert.IsNull(_lab.LastAimTarget, "錘不鎖定目標");
            Assert.IsNull(_hero.CurrentTarget, "錘不走單目標普攻");
            yield return WaitSeconds(0.35f);
            Assert.AreEqual(1, _lab.SweepResolveCount, "前搖後結算");
            Assert.AreEqual(0, _lab.LastSweepHits);
            Assert.AreEqual(frontStart, front.Health, "背後的木樁不受傷");
            _lab.RotateThirdPerson(180f);
            Assert.AreEqual(0f, _lab.YawDegrees, 1e-3f);
            yield return WaitSeconds(0.6f);   // 等冷卻

            DummyTarget second = SpawnDummyAt(front, -30f, 2.5f);   // 扇形內第二隻
            DummyTarget behind = SpawnDummyAt(front, 180f, 3f);     // 正後 3m
            DummyTarget far = SpawnDummyAt(front, 0f, 5f);          // 正前 5m（超半徑）
            DummyTarget side = SpawnDummyAt(front, 70f, 3f);        // 70°：全角誤當半角時會被掃到
            DummyTarget ally = SpawnDummyAt(front, 25f, 2f);        // 己陣營
            ally.Configure(1000f, _hero.HeroFaction);
            DummyTarget[] enemies = { second, behind, far, side };
            foreach (DummyTarget d in enemies) d.Configure(1000f, front.TargetFaction);
            Vector3 h = _hero.transform.position;
            TestWallTarget wall = PlaceOwnTestWall(new Vector3(h.x - 1.0f, h.y, h.z + 1.6f));   // 己方石牆，牆心在扇形內
            yield return null;
            float f0 = front.Health, s0 = second.Health, b0 = behind.Health, r0 = far.Health, d0 = side.Health;
            float a0 = ally.Health, w0 = wall.Health;

            // ② 正式一掃。
            float damage = _hero.AttackDamage;
            TapAttack();
            Assert.AreEqual(2, _lab.SweepStartCount);
            // 覆審 r1 L5：不靠牆鐘餘裕——Time.captureDeltaTime 固定 1/60，Time.time 逐幀走遊戲時間；
            // 逐幀等到結算，斷言結算那一幀距起手落在 [0.25, 0.25+1 幀]，結算前木樁血量不變（比原本只看 0.2s 時未結算更嚴）。
            float pressedAt = Time.time;
            for (int i = 0; i < 60 && _lab.SweepResolveCount == 1; i++)
            {
                Assert.AreEqual(f0, front.Health, "前搖未結算前不扣血");
                yield return null;
            }
            Assert.AreEqual(2, _lab.SweepResolveCount);
            float windup = Time.time - pressedAt;
            Assert.GreaterOrEqual(windup, 0.25f - 1e-3f, "前搖 0.25s 未到不結算");
            Assert.LessOrEqual(windup, 0.25f + 1f / 60f + 1e-3f, "前搖到點那一幀結算");
            yield return WaitSeconds(0.1f);
            Assert.AreEqual(f0 - damage, front.Health, 1e-3f, "正前 3m 受 60");
            Assert.AreEqual(s0 - damage, second.Health, 1e-3f, "扇形內第二隻也受 60");
            Assert.AreEqual(b0, behind.Health, "正後 3m 0 傷");
            Assert.AreEqual(r0, far.Health, "正前 5m（超半徑）0 傷");
            Assert.AreEqual(d0, side.Health, "70°（扇形外）0 傷");
            Assert.AreEqual(a0, ally.Health, "己陣營 0 傷");
            Assert.AreEqual(w0, wall.Health, "己方石牆 0 傷");

            // ③ 冷卻內（起手後約 0.4s）再按：不起手、不結算。
            TapAttack();
            Assert.AreEqual(2, _lab.SweepStartCount, "0.8s 冷卻內不起手");
            yield return WaitSeconds(0.35f);   // 起手後約 0.75s（若誤起手，0.65s 時會結算）
            Assert.AreEqual(2, _lab.SweepResolveCount, "冷卻內不結算");
            Assert.AreEqual(f0 - damage, front.Health, 1e-3f, "冷卻內木樁不再扣血");
            yield return WaitSeconds(0.15f);

            // ④ 冷卻後再按：可再結算；同時把牆改中立、己陣營木樁改敵方——同一位置改受傷（陣營過濾的鑑別力）。
            SetOwner(wall, Faction.Neutral);
            ally.Configure(1000f, front.TargetFaction);
            a0 = ally.Health;
            TapAttack();
            Assert.AreEqual(3, _lab.SweepStartCount, "冷卻後可再起手");
            yield return WaitSeconds(0.35f);
            Assert.AreEqual(3, _lab.SweepResolveCount);
            Assert.AreEqual(f0 - 2f * damage, front.Health, 1e-3f, "冷卻後再結算一次");
            Assert.AreEqual(a0 - damage, ally.Health, 1e-3f, "改成敵方後同位置受傷");
            Assert.AreEqual(w0 - damage, wall.Health, 1e-3f, "改成中立牆後同位置受傷");
        }

        [UnityTest] // 覆審 r1 M1：先鎖目標再切錘——只吃橫掃一次、不再普攻；弓鎖 10m 後切錘不追
        public IEnumerator W4b_LockedTarget_ThenSwitchToHammer_OnlySweepDamage_NoChase()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            dummy.Configure(1000f, dummy.TargetFaction);
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);
            float health = dummy.Health;
            TapAttack();
            Assert.AreSame(dummy, _hero.CurrentTarget, "Standard 先鎖住木樁");
            float deadline = Time.time + 1f;
            while (dummy.Health >= health && Time.time < deadline) yield return null;
            Assert.Less(dummy.Health, health, "先真的打到一下");
            SelectWeapon(WeaponId.Hammer);
            float h0 = dummy.Health;
            TapAttack();
            yield return WaitSeconds(1.2f);   // 普攻週期 0.8s：若普攻還在，窗口內至少多一下
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "1.2s 內只吃一次橫掃傷害");
            Assert.AreEqual(1, _lab.SweepResolveCount);
            Assert.IsNull(_hero.CurrentTarget, "錘下不留普攻目標");

            yield return Load();
            dummy = Object.FindObjectOfType<DummyTarget>();
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 10f);
            SelectWeapon(WeaponId.Bow);
            TapAttack();
            Assert.AreSame(dummy, _hero.CurrentTarget, "弓鎖住 10m 木樁");
            yield return WaitSeconds(0.5f);
            SelectWeapon(WeaponId.Hammer);
            Vector3 start = Flat(_hero.transform.position);
            yield return WaitSeconds(1.5f);
            float moved = (Flat(_hero.transform.position) - start).magnitude;
            Debug.Log("[CAMERA LAB TEST] W4b bow->hammer moved=" + moved.ToString("F3"));
            Assert.Less(moved, 0.5f, "弓切錘：英雄不去追 10m 外的舊目標");
            Assert.IsNull(_hero.CurrentTarget);
        }

        // 經 DuelInputRouter 的移動鎖查詢把英雄輸入鎖住（與通風口飛行同一個入口），false＝還原成場景原本的查詢。
        private System.Func<bool> _savedLock;
        private void SetHeroInputLockedForTest(bool locked)
        {
            object router = typeof(Phase1Bootstrap).GetField("_duelInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(_bootstrap);
            System.Reflection.FieldInfo field = router.GetType().GetField("_movementInputLocked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (locked) { _savedLock = (System.Func<bool>)field.GetValue(router); field.SetValue(router, (System.Func<bool>)(() => true)); }
            else field.SetValue(router, _savedLock);
        }

        [UnityTest] // 覆審 r1 M2：天賦盤顯示時 WPN 讓開（不畫、不收路由），關閉後恢復原位
        public IEnumerator W5b_TalentPanelVisible_WeaponButtonYields_RestoresAfterClose()
        {
            yield return Load(false);
            _bootstrap.UseFlatCaptureSpecForTest();
            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float cx, out float cy), "拿不到 CAPTURE 鈕");
            _bootstrap.WorldTapInput.SendScreenTap(cx, cy);
            yield return null;
            TrainingOpponent opponent = Object.FindObjectOfType<TrainingOpponent>();
            Vector3 os = Camera.main.WorldToScreenPoint(opponent.transform.position + Vector3.up);
            _bootstrap.WorldTapInput.SendScreenTap(os.x, os.y);
            yield return null;
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState);
            _lab.SetThirdPerson(true);
            yield return null;
            Assert.IsTrue(_lab.ActionButtonLayout.WeaponVisible, "天賦盤未顯示：WPN 在");
            Vector2 wpn = Center(_lab.ActionButtonLayout.Weapon);
            _bootstrap.SeedCaptureScoresForTest(250, 0);
            yield return null; yield return null;
            Assert.IsTrue(_bootstrap.TalentPanelVisible, "天賦盤應顯示");
            int tier = _bootstrap.TalentPendingTier;
            Debug.Log("[CAMERA LAB TEST] W5b screen=" + Screen.width + "x" + Screen.height + " wpn=" + wpn + " tier=" + tier);
            _input.SendScreenTap(wpn.x, wpn.y);
            yield return null;
            Assert.AreEqual(WeaponId.Standard, _lab.CurrentWeapon, "天賦盤顯示時點 WPN 位置不切武器");
            Assert.AreEqual(0, _lab.WeaponSwitchCount);
            Assert.IsTrue(_bootstrap.TalentPanelVisible, "不誤選天賦（盤仍開著）");
            Assert.AreEqual(tier, _bootstrap.TalentPendingTier, "不誤選天賦（待選階不變）");
            Assert.IsFalse(_lab.ActionButtonLayout.WeaponVisible, "天賦盤顯示：WPN 讓開");

            Assert.IsTrue(_bootstrap.TryGetTalentButtonScreenPoint(0, out float tx, out float ty));
            _bootstrap.WorldTapInput.SendScreenTap(tx, ty);
            yield return null; yield return null;
            Assert.IsFalse(_bootstrap.TalentPanelVisible, "選完天賦盤關閉");
            Assert.IsTrue(_lab.ActionButtonLayout.WeaponVisible, "天賦盤關閉：WPN 恢復");
            Assert.AreEqual(wpn, Center(_lab.ActionButtonLayout.Weapon), "恢復原位");
            TapWeapon();
            Assert.AreEqual(WeaponId.Sword, _lab.CurrentWeapon, "關閉後可切");
        }

        [UnityTest] // 覆審 r1 L1：TOP/THIRD 不重置錘冷卻
        public IEnumerator W4c_Hammer_ToggleViewKeepsCooldown()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            dummy.Configure(1000f, dummy.TargetFaction);
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);
            SelectWeapon(WeaponId.Hammer);
            float h0 = dummy.Health;

            // L1：起手後立刻 TOP→THIRD，再按 ATK 仍在冷卻內。
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount);
            _lab.SetThirdPerson(false);
            _lab.SetThirdPerson(true);
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "切視角不得重置 0.8s 冷卻");
            yield return WaitSeconds(0.9f);
            Assert.AreEqual(h0, dummy.Health, "切到俯視已取消那一掃");
        }

        [UnityTest] // 覆審 r1 L2：前搖中切走武器或輸入被鎖→取消這一掃；解鎖後下一掃照常（正向對照）
        public IEnumerator W4d_Hammer_SwitchOrInputLockDuringWindup_CancelsSweep()
        {
            yield return Load();
            DummyTarget dummy = Object.FindObjectOfType<DummyTarget>();
            dummy.Configure(1000f, dummy.TargetFaction);
            yield return WarpAndSettle(dummy.transform.position + Vector3.back * 3f);
            SelectWeapon(WeaponId.Hammer);
            float h0 = dummy.Health;

            // L2-a：前搖中切走武器→不結算。
            TapAttack();
            Assert.AreEqual(1, _lab.SweepStartCount);
            TapWeapon();
            Assert.AreEqual(WeaponId.Standard, _lab.CurrentWeapon);
            yield return WaitSeconds(0.4f);
            Assert.AreEqual(h0, dummy.Health, "前搖中切走武器：這一掃取消");
            Assert.AreEqual(0, _lab.SweepResolveCount);
            yield return WaitSeconds(0.5f);

            // L2-b：前搖中輸入被鎖（HeroInputBlockedForLab）→不結算；解鎖後下一掃照常。
            SelectWeapon(WeaponId.Hammer);
            TapAttack();
            Assert.AreEqual(2, _lab.SweepStartCount);
            SetHeroInputLockedForTest(true);
            Assert.IsTrue(_bootstrap.HeroInputBlockedForLab);
            yield return WaitSeconds(0.4f);
            SetHeroInputLockedForTest(false);
            Assert.IsFalse(_bootstrap.HeroInputBlockedForLab);
            Assert.AreEqual(h0, dummy.Health, "前搖中輸入被鎖：這一掃取消");
            yield return WaitSeconds(0.5f);
            TapAttack();
            Assert.AreEqual(3, _lab.SweepStartCount);
            yield return WaitSeconds(0.35f);
            Assert.AreEqual(h0 - _hero.AttackDamage, dummy.Health, 1e-3f, "正向對照：正常一掃照樣扣血");
        }
    }

    // W6：在 Update 夾區內每 30 幀切一次武器、隔 10 幀按一次 ATK（四把武器輪流）。
    public sealed class WeaponButtonDriver : MonoBehaviour
    {
        internal PlayerInputService Input;
        internal CameraComparisonLab Lab;
        public int Frame;
        public int BowLocks, SwordLocks;

        private void Update()
        {
            if (Input == null) return;
            Frame++;
            if (Frame % 30 == 1)
            {
                ScreenRegion w = Lab.ActionButtonLayout.Weapon;
                Input.SendScreenTap((w.XMin + w.XMax) * .5f, (w.YMin + w.YMax) * .5f);
            }
            else if (Frame % 30 == 11)
            {
                ScreenRegion a = Lab.ActionButtonLayout.Attack;
                Input.SendScreenTap((a.XMin + a.XMax) * .5f, (a.YMin + a.YMax) * .5f);
                if (Lab.LastAimTarget != null && Lab.CurrentWeapon == WeaponId.Bow) BowLocks++;
                if (Lab.LastAimTarget != null && Lab.CurrentWeapon == WeaponId.Sword) SwordLocks++;
            }
        }
    }
}
#endif
