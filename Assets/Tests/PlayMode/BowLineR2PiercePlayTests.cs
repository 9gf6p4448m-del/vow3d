#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Vow.Core.Logic;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓「蓄滿一條線」修訂 R2「穿透同式」（幾何見 vow-toolchain/acceptance-bowline-r2-interp.md）。穿透碼未改（沿用實體射線，主對話已接受）。
    // 量測條件＝SetupBow 的英雄高度（與木樁 pivot 同 y）；英雄站地面時射線偏高的已知限制見 bowline-report-20261003.md 修訂 R2 段。
    public sealed partial class CameraLabCombatPlayTests
    {
        // ── 穿透同式：滿蓄穿透沿實體射線，線穿過第二木樁身體才中 ──
        private DummyTarget SpawnDummyOffset(DummyTarget template, float lateral, float forward)
        {
            Vector3 h = _hero.transform.position;
            Vector3 p = new Vector3(h.x + lateral, template.transform.position.y, h.z + forward);
            DummyTarget clone = Object.Instantiate(template, p, template.transform.rotation);
            _bootstrap.RegisterElementTarget(clone);
            _bootstrap.TargetRegistry.Register(clone);   // 比照場景木樁與 C7(b)：碰撞體登記進穿透射線的查表
            return clone;
        }

        [UnityTest] // R2-穿透：木樁 1 正前 6m；木樁 2 在 10m、離直線 0.30m（身體內）→ 兩者各 108；離直線 0.80m（身體外）→ 只中木樁 1
        public IEnumerator R2_Pierce_SecondBodyOnLineHit_OffLineMiss()
        {
            float[] laterals = { 0.30f, 0.80f };
            for (int i = 0; i < laterals.Length; i++)
            {
                yield return SetupBow(6f);
                DummyTarget first = _bowDummy;
                DummyTarget second = SpawnDummyOffset(first, laterals[i], 10f);
                second.Configure(1000f, second.TargetFaction);
                yield return null;
                float h1 = first.Health, h2 = second.Health;
                yield return HoldAttack(1.0);
                Assert.AreSame(first, _lab.LastAimTarget, "測試前提：準星正對木樁 1＝直接目標");
                yield return WaitForDrop(first, h1, 2f);
                float d1 = h1 - first.Health, d2 = h2 - second.Health;
                _hero.ClearCombatTargetInPlace();
                yield return WaitSeconds(1.0f);
                float d2Late = h2 - second.Health;
                Debug.Log("[BOWLINE R2] Pierce lateral=" + laterals[i] + " first=" + d1.ToString("F4") + " second=" + d2.ToString("F4") + " secondAfter1s=" + d2Late.ToString("F4"));
                float full = _hero.AttackDamage * BowChargedDamageMultiplier;
                Assert.AreEqual(full, d1, 1e-3f, "木樁 1 受蓄滿 108");
                if (i == 0) Assert.AreEqual(full, d2, 1e-3f, "離直線 0.30m（身體內）：穿透同一發也中 108");
                else Assert.AreEqual(0f, d2Late, "離直線 0.80m（身體外）：不中");
                Object.Destroy(second.gameObject);
            }
        }
    }
}
#endif
