#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Object = UnityEngine.Object;

namespace Vow.Tests.PlayMode
{
    // 弓蓄力覆審 r1（vow-toolchain/review-bowcharge-r1-20261003.md）回歸：
    //   H1 蓄力箭放開後、命中前被打斷（搖桿／換武器／CancelCombatForDuel），之後再點同一目標＝一般普攻（60、不穿透、先走進 12m）。
    //   M2 蓄力中的鎖定標記（PreviewTarget）用蓄力後的錐／射程挑，與放開時實際打的一致。
    // 只直接用基底也有的公開成員（傷害、位置、PreviewTarget），量測落在行為上。
    public sealed partial class CameraLabCombatPlayTests
    {
        private const float BowNormalRange = 12f;

        // 準星左轉 5°：木樁落在螢幕中線右側（右半屏＝鏡頭區，短點＝世界點擊），再用世界點擊點它。
        private IEnumerator WorldTapDummy(DummyTarget dummy)
        {
            _lab.RotateThirdPerson(-5f);
            yield return null; yield return null;
            Vector3 sp = Camera.main.WorldToScreenPoint(dummy.transform.position + Vector3.up * 1f);
            Assert.Greater(sp.x, Screen.width * 0.5f, "前提：木樁在右半屏");
            _input.SendScreenTap(sp.x, sp.y);
        }

        // 點完後等第一下命中：回傳 (第一下傷害, 命中當下英雄與木樁水平距離, 後方木樁傷害)。
        private float _firstHit, _firstHitDistance, _behindDamage;
        private IEnumerator MeasureFirstHit(DummyTarget dummy, DummyTarget behind, float seconds)
        {
            float h0 = dummy.Health, b0 = behind.Health;
            _firstHit = -1f; _firstHitDistance = -1f;
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return null;
                if (dummy.Health < h0)
                {
                    _firstHit = h0 - dummy.Health;
                    _firstHitDistance = FlatDistance(_hero.transform.position, dummy.transform.position);
                    break;
                }
            }
            yield return null; yield return null;
            _behindDamage = b0 - behind.Health;
        }

        private DummyTarget SpawnBehind(DummyTarget front, float distanceFromHero)
        {
            DummyTarget behind = SpawnDummyAt(front, 0f, distanceFromHero);
            behind.Configure(1000f, behind.TargetFaction);
            _bootstrap.TargetRegistry.Register(behind);
            return behind;
        }

        private void AssertNormalFirstHit(string label)
        {
            Debug.Log("[BOWCHARGE TEST] " + label + " hit=" + _firstHit.ToString("F3") + " dist=" + _firstHitDistance.ToString("F3")
                + " behind=" + _behindDamage.ToString("F3"));
            Assert.AreEqual(_hero.AttackDamage, _firstHit, 1e-3f, label + "：再點同一目標的第一下＝一般普攻 60（蓄力箭不得殘留）");
            Assert.LessOrEqual(_firstHitDistance, BowNormalRange + 0.05f, label + "：16m 外不開打，先走進 12m");
            Assert.AreEqual(0f, _behindDamage, 1e-3f, label + "：一般普攻不穿透");
        }

        // 共用前段：14m 木樁＋後方 15.5m 木樁，弓滿蓄放開→英雄在 16m 射程內原地起前搖（還沒命中）。
        private DummyTarget _h1Behind;
        private Vector3 _h1Stand;
        private IEnumerator ReleaseFullChargeAtFourteen()
        {
            yield return SetupBow(14f);
            _h1Behind = SpawnBehind(_bowDummy, 15.5f);
            yield return null; yield return null;
            _h1Stand = _hero.transform.position;
            yield return HoldAttack(1.0);
            Assert.AreSame(_bowDummy, _hero.CurrentTarget, "前提：滿蓄放開已鎖定 14m 木樁");
            Assert.AreEqual(1000f, _bowDummy.Health, "前提：蓄力箭還沒命中");
        }

        [UnityTest] // H1(a)：滿蓄放開→前搖內推搖桿打斷→1s 後右半屏點同一目標→一般普攻
        public IEnumerator H1a_Bow_ChargedShotInterruptedByJoystick_LaterWorldTapIsNormalAttack()
        {
            yield return ReleaseFullChargeAtFourteen();
            Vector2 stick = new Vector2(Screen.width * .15f, Screen.height * .2f);
            _input.BeginSimulatedHold(1, stick.x, stick.y);
            _input.MoveSimulatedHold(1, stick.x + _input.ContinuousRouter.JoystickRadiusPixels, stick.y);
            for (int i = 0; i < 3; i++) yield return null;
            _input.EndSimulatedHold(1);
            Assert.IsNull(_hero.CurrentTarget, "前提：搖桿打斷前搖、清掉目標");
            Assert.AreEqual(1000f, _bowDummy.Health, "前提：被打斷的蓄力箭沒命中");
            yield return WaitSeconds(1f);
            yield return WarpAndSettle(_h1Stand);
            yield return WorldTapDummy(_bowDummy);
            Assert.AreSame(_bowDummy, _hero.CurrentTarget, "前提：世界點擊點到木樁");
            yield return MeasureFirstHit(_bowDummy, _h1Behind, 4f);
            AssertNormalFirstHit("搖桿打斷");
        }

        [UnityTest] // H1(b)：滿蓄放開→前搖內切 WPN（弓→錘）→切回弓→點同一目標→一般普攻
        public IEnumerator H1b_Bow_ChargedShotInterruptedByWeaponSwitch_LaterWorldTapIsNormalAttack()
        {
            yield return ReleaseFullChargeAtFourteen();
            TapWeapon();   // 弓→錘
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(1000f, _bowDummy.Health, "前提：被打斷的蓄力箭沒命中");
            SelectWeaponByTaps(4);   // 錘→鉤鎖→Standard→劍→弓
            AssertBowSelected();
            yield return WaitSeconds(1f);
            yield return WarpAndSettle(_h1Stand);
            yield return WorldTapDummy(_bowDummy);
            Assert.AreSame(_bowDummy, _hero.CurrentTarget, "前提：世界點擊點到木樁");
            yield return MeasureFirstHit(_bowDummy, _h1Behind, 4f);
            AssertNormalFirstHit("切武器");
        }

        [UnityTest] // H1(c)：滿蓄放開→前搖內 CancelCombatForDuel（倒地／換回合同一入口）→點同一目標→一般普攻
        public IEnumerator H1c_Bow_ChargedShotInterruptedByCancelCombatForDuel_LaterWorldTapIsNormalAttack()
        {
            yield return ReleaseFullChargeAtFourteen();
            _hero.CancelCombatForDuel();
            Assert.IsNull(_hero.CurrentTarget, "前提：CancelCombatForDuel 清掉目標");
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(1000f, _bowDummy.Health, "前提：被打斷的蓄力箭沒命中");
            yield return WaitSeconds(0.3f);   // 短於任何逾時：只靠「打斷即解除」才會綠
            yield return WarpAndSettle(_h1Stand);
            yield return WorldTapDummy(_bowDummy);
            Assert.AreSame(_bowDummy, _hero.CurrentTarget, "前提：世界點擊點到木樁");
            yield return MeasureFirstHit(_bowDummy, _h1Behind, 4f);
            AssertNormalFirstHit("CancelCombatForDuel");
        }

        [UnityTest] // M2：蓄力中鎖定標記＝放開會打的目標——14m 木樁滿蓄時有標記；6m 偏 8° 滿蓄時沒有標記
        public IEnumerator M2_Bow_ChargingMarker_UsesChargedConeAndRange()
        {
            yield return SetupBow(14f);
            Assert.IsNull(_lab.PreviewTarget, "對照：沒按住時 14m 超出 12m，沒有標記");
            PressAttack();
            yield return WaitUnscaled(1.05);
            yield return null;
            Assert.AreSame(_bowDummy, _lab.PreviewTarget, "滿蓄中：射程 16m，14m 木樁要有標記");
            ReleaseAttack();

            yield return SetupBow(6f);
            _lab.RotateThirdPerson(8f);
            yield return null; yield return null;
            Assert.AreSame(_bowDummy, _lab.PreviewTarget, "對照：沒按住時 8° 在 12° 錐內，有標記");
            PressAttack();
            yield return WaitUnscaled(1.05);
            yield return null;
            Assert.IsNull(_lab.PreviewTarget, "滿蓄中：錐收到 4°，8° 的木樁不得有標記");
            ReleaseAttack();
        }
    }
}
#endif
