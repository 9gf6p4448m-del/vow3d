using System;
using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // v0.17.0 鉤鎖（拉自己到目標）凍結驗收 G1／G2／G4／G7 的純邏輯部分。
    // 凍結檔：vow-toolchain/acceptance-grapple-20261002.md；裁定：decision-record-grapple-20261002.md。dotnet 與 Unity EditMode 共用。
    public sealed class GrappleLabTests
    {
        [Test] // G1：數值與循環
        public void G1_GrappleSpec_ValuesAndCycleOfFive()
        {
            WeaponSpec g = WeaponSpec.Grapple;
            Assert.AreEqual(WeaponId.Grapple, g.Id);
            Assert.AreEqual(4, (int)WeaponId.Grapple);
            Assert.AreEqual(20f, g.ConeHalfAngleDegrees, "準星錐 ±20°");
            Assert.AreEqual(10f, g.AimRangeMeters, "鉤距 10m");
            Assert.IsFalse(g.FallbackToNearest, "錐外不鉤");
            Assert.IsFalse(g.OverridesAttackRange, "抵達後普攻沿用預設射程");
            Assert.IsTrue(g.IsGrapple);
            Assert.AreEqual(2f, g.GrappleStopMeters, "停在目標 2m 處");
            Assert.AreEqual(0.2f, g.GrapplePullSeconds, "位移約 0.2s");
            Assert.AreEqual(4f, g.GrappleCooldownSeconds, "冷卻 4s");
            Assert.AreEqual(WeaponId.Grapple, WeaponSpec.Get(WeaponId.Grapple).Id);

            Assert.AreEqual(5, WeaponSelection.Count);
            WeaponSelection sel = default;
            WeaponId[] expected = { WeaponId.Sword, WeaponId.Bow, WeaponId.Hammer, WeaponId.Grapple, WeaponId.Standard };
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], sel.Next(), "第 " + (i + 1) + " 下");
        }

        [Test] // G7：IsSweep 只有錘為真；IsGrapple 只有鉤鎖為真
        public void G7_OnlyHammerIsSweep_OnlyGrappleIsGrapple()
        {
            for (int i = 0; i < WeaponSelection.Count; i++)
            {
                WeaponSpec w = WeaponSpec.Get((WeaponId)i);
                Assert.AreEqual((WeaponId)i, w.Id);
                Assert.AreEqual(w.Id == WeaponId.Hammer, w.IsSweep, w.Id + " IsSweep");
                Assert.AreEqual(w.Id == WeaponId.Grapple, w.IsGrapple, w.Id + " IsGrapple");
            }
        }

        private static int Pick(float degreesFromZ, float distance)
        {
            double a = degreesFromZ * Math.PI / 180.0;
            AimTargetPicker picker = default;
            picker.Begin(0f, 0f, 0f, 1f, WeaponSpec.Grapple);
            picker.Consider(0, (float)(Math.Sin(a) * distance), (float)(Math.Cos(a) * distance));
            return picker.ResolvedIndex;
        }

        [Test] // G2／G3（挑選）：錐 ±20°、10m 內才挑；錐外與超距不退回
        public void G2_Picker_ConeTwentyDegrees_TenMetres_NoFallback()
        {
            Assert.AreEqual(0, Pick(0f, 9.5f));
            Assert.AreEqual(0, Pick(19f, 9.9f));
            Assert.AreEqual(0, Pick(-19f, 3f));
            Assert.AreEqual(-1, Pick(21f, 5f), "錐外不挑");
            Assert.AreEqual(-1, Pick(180f, 3f), "背後不挑（不退回最近者）");
            Assert.AreEqual(-1, Pick(0f, 10.1f), "超過 10m 不挑");
        }

        [Test] // G2：停點＝目標前 2m，0.2s 內等速走完；已在 2m 內不拉
        public void G2_Hook_PullsToTwoMetresBeforeTarget_WithinPullSeconds()
        {
            GrappleHook hook = default;
            Assert.IsTrue(hook.TryStart(0f, WeaponSpec.Grapple, 1f, 1f, 1f, 10f));
            Assert.IsTrue(hook.Pulling);
            float x = 1f, z = 1f, t = 0f;
            bool done = false;
            for (int i = 0; i < 100 && !done; i++)
            {
                done = hook.Step(1f / 60f, out float dx, out float dz);
                x += dx; z += dz; t += 1f / 60f;
            }
            Assert.IsTrue(done);
            Assert.IsFalse(hook.Pulling);
            Assert.AreEqual(1f, x, 1e-4f);
            Assert.AreEqual(8f, z, 1e-4f, "停在目標（z=10）前 2m");
            Assert.LessOrEqual(t, 0.2f + 1f / 60f + 1e-4f, "約 0.2s 走完");

            Assert.IsFalse(GrappleHook.ComputePull(0f, 0f, 0f, 1.9f, 2f, out _, out _), "已在 2m 內不需要拉");
            GrappleHook near = default;
            Assert.IsFalse(near.TryStart(0f, WeaponSpec.Grapple, 0f, 0f, 0f, 1.5f), "已在 2m 內不起鉤、不吃冷卻");
            Assert.IsTrue(near.TryStart(0f, WeaponSpec.Grapple, 0f, 0f, 0f, 5f));
        }

        [Test] // G4：冷卻 4s；中途取消冷卻照算
        public void G4_Hook_CooldownFourSeconds_CancelKeepsCooldown()
        {
            GrappleHook hook = default;
            Assert.IsTrue(hook.TryStart(10f, WeaponSpec.Grapple, 0f, 0f, 0f, 9f));
            hook.Cancel();
            Assert.IsFalse(hook.Pulling);
            Assert.IsFalse(hook.Step(1f / 60f, out float dx, out float dz));
            Assert.AreEqual(0f, dx);
            Assert.AreEqual(0f, dz);
            Assert.IsFalse(hook.TryStart(10.5f, WeaponSpec.Grapple, 0f, 0f, 0f, 9f), "冷卻內不鉤");
            Assert.IsFalse(hook.TryStart(13.99f, WeaponSpec.Grapple, 0f, 0f, 0f, 9f), "3.99s 仍在冷卻");
            Assert.IsTrue(hook.TryStart(14f, WeaponSpec.Grapple, 0f, 0f, 0f, 9f), "4s 後可再鉤");
        }
    }
}
