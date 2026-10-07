#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 錘蓄力時按住 ATK 拖曳可偏轉出手方向（與弓同一套 aimYaw）。使用者 2026-10-07 手機回報「蓄力時不能轉方向」。
    // 全部走真路由：ATK 用模擬按住手指，水平位移用與 BowAimPlayTests 同一把尺（PixelsPerMillimeter）。
    // 鑑別設計：蓄力 0.6s 扇形全角約 115°（半角約 57°）、拖 15mm＝偏角 +40°。
    //   命中目標 A 在 +75°（偏轉後離軸 35° 內；未偏轉離軸 75° 外）；排除目標 B 在 −25°（偏轉後離軸 65° 外；未偏轉離軸 25° 內）。
    public sealed partial class CameraLabCombatPlayTests
    {
        private const float HmAimDragMillimetres = 15f;   // 拉滿 40°（BowAimLogic）
        private const double HmAimHold = 0.6;             // ≥0.5s

        [UnityTest]
        public IEnumerator HAIM1_Hammer_ChargeDragRight_SweepFollowsAimYaw_NotCrosshair()
        {
            yield return HmSetup();
            DummyTarget hit = SpawnDummyAt(_hmFront, 75f, 3f);
            DummyTarget miss = SpawnDummyAt(_hmFront, -25f, 3f);
            hit.Configure(1000f, _hmFront.TargetFaction);
            miss.Configure(1000f, _hmFront.TargetFaction);
            yield return null;
            float hit0 = hit.Health, miss0 = miss.Health;
            Assert.AreEqual(0f, _lab.YawDegrees, 1e-3f, "場景前提：鏡頭朝 +Z");

            double t0 = Time.unscaledTimeAsDouble;
            PressAttack();
            yield return null;
            DragAttackMillimetres(HmAimDragMillimetres);
            while (Time.unscaledTimeAsDouble - t0 < HmAimHold) yield return null;
            Assert.AreEqual(0, _lab.SweepStartCount, "蓄力中不出手");
            ReleaseAttack();
            Assert.AreEqual(1, _lab.SweepStartCount, "放開起手");
            yield return HmWaitResolve(1);
            yield return WaitSeconds(0.1f);

            float dx = (float)typeof(Vow.Bootstrap.CameraComparisonLab).GetField("_sweepDirX", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_lab);
            float dz = (float)typeof(Vow.Bootstrap.CameraComparisonLab).GetField("_sweepDirZ", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_lab);
            float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            Debug.Log("[HAMMER TEST] HAIM1 sweepYaw=" + yaw.ToString("F2") + " hitHP=" + hit.Health + " missHP=" + miss.Health);
            Assert.AreEqual(40f, yaw, 2f, "結算方向＝按下時準星(0°)＋拖曳偏角 40°");
            Assert.Less(hit.Health, hit0, "偏轉後方向(+75° 離軸 35°)的目標被命中");
            Assert.AreEqual(miss0, miss.Health, "原準星側(−25°，偏轉後離軸 65°)的目標不被命中");
            Assert.AreEqual(40f, _lab.YawDegrees, 2f, "鏡頭追蹤出手方向（與弓相同）");
        }
    }
}
#endif
