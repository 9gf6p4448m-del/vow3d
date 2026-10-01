using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // v0.15.0 整合驗收 I5：失去視野追擊記憶（HeroController.TickLostTargetMemory）在「蒸氣遮蔽」下的行為。
    // 原有 K1-K5 只測崖台地形視野；這裡補蒸氣（ElementField.IsConcealedFrom → CanEngage=false）。俯視（單挑）模式、木樁。
    // 斷言照探針實測寫（vow-toolchain/integrate-i5-probe0.log）：鎖定木樁（10m 外）後起霧 → 英雄當幀失去目標、
    // 沿直線走向「最後看見位置」→ 踏進同一團霧（規則②同霧可見）就接回並打到。木樁在霧中搬走時英雄路徑不偏（不洩漏即時位置）。
    public sealed class ChaseMemorySteamPlayTests
    {
        private const float Fps = 60f;
        private static readonly Vector3 HeroStart = new Vector3(0f, 0f, -4f);
        private static readonly Vector3 DummyAt = new Vector3(0f, 1f, 6f);        // 鎖定當下的位置＝最後看見位置
        private static readonly Vector3 SteamCentre = new Vector3(0f, 0f, 9f);    // 半徑 4 → 霧覆蓋 z 5~13，英雄起點在霧外
        private static readonly Vector3 DummyHidden = new Vector3(-3f, 1f, 9f);   // 霧內另一點（距霧心 3m）
        private const float LateralTolerance = 0.3f;

        private HeroController _hero;
        private ElementField _field;
        private DummyTarget _dummy;
        private ScriptedInput _input;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; }

        private IEnumerator Setup()
        {
            Time.captureDeltaTime = 1f / Fps;
            SceneManager.LoadScene("VOW_Phase1_Greybox", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _hero = Object.FindObjectOfType<HeroController>();
            Phase1Bootstrap bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            Assert.IsNotNull(_hero, "場景裡找不到 HeroController");
            Assert.IsNotNull(bootstrap, "場景缺少 Phase1Bootstrap");
            _field = bootstrap.ElementField;
            Assert.IsNotNull(_field, "場景缺少 ElementField");
            _dummy = Object.FindObjectOfType<DummyTarget>();
            Assert.IsNotNull(_dummy, "場景缺少木樁");
            _input = new ScriptedInput();
            _hero.Initialize(_input, Object.FindObjectOfType<CombatFeedbackService>(), Camera.main,
                Object.FindObjectOfType<HapticFeedbackService>());
            _hero.StopMoving();
            Assert.IsTrue(_hero.GetComponent<NavMeshAgent>().Warp(HeroStart), "無法把英雄放到起點");
            _hero.transform.position = HeroStart;
            _hero.transform.rotation = Quaternion.identity;
            _dummy.transform.position = DummyAt;
            yield return null;
        }

        // 鎖定（10m 外，超出 5m 射程 → 英雄開始走近）→ 下一幀起霧蓋住木樁。
        private IEnumerator LockThenRaiseSteam()
        {
            _input.TapTarget(_dummy);
            yield return null;
            Assert.AreSame(_dummy, _hero.CurrentTarget, "起霧前應該鎖得到木樁");
            int before = _field.CountZonesOfKind(ElementZoneKind.Steam);
            _field.CastWater(SteamCentre, (int)Faction.BlueTeam);
            _field.CastFire(SteamCentre, (int)Faction.BlueTeam);
            Assert.AreEqual(before + 1, _field.CountZonesOfKind(ElementZoneKind.Steam), "蒸氣沒有成形");
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        [UnityTest]
        public IEnumerator L1_SteamHidesLockedTarget_HeroWalksToLastSeen_ThenReacquiresInsideTheSameFog()
        {
            yield return Setup();
            float health0 = _dummy.Health;
            yield return LockThenRaiseSteam();
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_dummy), "起霧後霧外英雄不該看得到霧內木樁（前提）");

            // 行為：1.0 秒後英雄已朝最後看見位置走了 ≥4m，且路線是直線（x 不偏）。
            for (int i = 0; i < Mathf.RoundToInt(1f * Fps); i++) yield return null;
            Vector3 p = _hero.transform.position;
            Assert.GreaterOrEqual(p.z - HeroStart.z, 4f,
                "失去視野後英雄沒有繼續走向最後看見位置（停在 z=" + p.z.ToString("F2") + "）");
            Assert.LessOrEqual(Mathf.Abs(p.x), LateralTolerance, "英雄沒有走直線向最後看見位置，x=" + p.x.ToString("F2"));
            Assert.AreEqual(health0, _dummy.Health, 0.01f, "還沒進霧就打到了霧內木樁");

            // 行為：踏進同一團霧後接回並打到；第一刀命中時英雄在霧內。
            float steamRadius = SteamRadius();
            bool hit = false;
            float heroToSteamAtHit = -1f;
            for (int i = 0; i < Mathf.RoundToInt(2f * Fps) && !hit; i++)
            {
                yield return null;
                if (_dummy.Health < health0 - 0.01f) { hit = true; heroToSteamAtHit = PlanarDistance(_hero.transform.position, SteamCentre); }
            }
            Assert.IsTrue(hit, "起霧後 3 秒內英雄沒有接回並打到木樁（停在 " + _hero.transform.position + "）");
            Assert.Less(heroToSteamAtHit, steamRadius, "接回命中時英雄不在同一團霧裡（不該看得到）");
            Assert.AreSame(_dummy, _hero.CurrentTarget, "接回後目標應為原木樁");
        }

        [UnityTest]
        public IEnumerator L2_TargetMovesInsideSteam_HeroStillHeadsToLastSeen_NotTheLivePosition()
        {
            yield return Setup();
            float health0 = _dummy.Health;
            yield return LockThenRaiseSteam();
            yield return null;
            _dummy.transform.position = DummyHidden;   // 木樁在霧中橫移 3m（仍被蒸氣遮住）
            yield return null;
            Assert.IsFalse(_hero.CanEngage(_dummy), "木樁搬到霧內另一點後仍不該被看到（前提）");

            // 行為：重新看見之前，英雄每一幀都在「起點→最後看見位置」那條直線上（x≈0），不往木樁的即時位置偏。
            float steamRadius = SteamRadius();
            float maxLateral = 0f;
            int framesLost = 0;
            for (int i = 0; i < Mathf.RoundToInt(3f * Fps) && _hero.CurrentTarget == null; i++)
            {
                maxLateral = Mathf.Max(maxLateral, Mathf.Abs(_hero.transform.position.x));
                framesLost++;
                yield return null;
            }
            Vector3 p = _hero.transform.position;
            Assert.LessOrEqual(maxLateral, LateralTolerance,
                "看不見期間英雄往木樁即時位置偏了（最大 |x|=" + maxLateral.ToString("F2") + "）：洩漏即時位置");
            Assert.GreaterOrEqual(p.z - HeroStart.z, 8.5f,
                "英雄沒有走到最後看見位置附近（z=" + p.z.ToString("F2") + "，失去目標 " + framesLost + " 幀）");
            Assert.AreSame(_dummy, _hero.CurrentTarget, "英雄踏進同一團霧後沒有接回木樁");
            Assert.Less(PlanarDistance(p, SteamCentre), steamRadius, "接回時英雄不在霧內");

            bool hit = false;
            for (int i = 0; i < Mathf.RoundToInt(1.5f * Fps) && !hit; i++)
            {
                yield return null;
                hit = _dummy.Health < health0 - 0.01f;
            }
            Assert.IsTrue(hit, "接回後 1.5 秒內沒有打到霧內木樁");
        }

        private float SteamRadius()
        {
            for (int slot = 0; slot < _field.ZoneCapacity; slot++)
                if (_field.TryGetZoneBySlot(slot, out ElementZone zone) && zone.Kind == ElementZoneKind.Steam) return zone.Radius;
            Assert.Fail("找不到蒸氣區");
            return 0f;
        }
    }
}
