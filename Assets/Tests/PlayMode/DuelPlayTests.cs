using System.Collections;
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
    public sealed class DuelPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; }

        private IEnumerator Setup(bool nearOpponent = false)
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
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            if (nearOpponent)
            {
                _hero.GetComponent<HeroLocomotion>().WarpTo(_opponent.transform.position + Vector3.back * 1.6f);
                yield return null; // 鏡頭追上英雄，WorldToScreenPoint 才是當幀的點擊位置
            }
        }

        private void TapOpponent()
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(_opponent.transform.position + Vector3.up);
            Assert.Greater(screen.z, 0f);
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
        }

        [UnityTest]
        public IEnumerator FirstTap_StartsTheRoundWithoutAttacking_SecondTapCanHit()
        {
            yield return Setup(true);
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.AreEqual(1, _bootstrap.DuelStartCount);
            Assert.IsNull(_hero.CurrentTarget, "開局點選不應同時送進英雄普攻");
            Assert.AreEqual(300f, _opponent.Health, 0.01f);

            TapOpponent();
            float deadline = Time.time + 0.8f;
            while (_opponent.Health >= 300f && Time.time < deadline) yield return null;
            Assert.Less(_opponent.Health, 300f, "第二下點選沒有經動畫事件造成普攻傷害");
        }

        [UnityTest]
        public IEnumerator Warning_LocksTheCircle_ThenHitsOnce()
        {
            yield return Setup(true);
            TapOpponent();
            float deadline = Time.time + 0.4f;
            while (!_opponent.IsWarning && Time.time < deadline) yield return null;
            Assert.IsTrue(_opponent.IsWarning, "對手沒有進入 0.7 秒預警");
            Vector3 locked = _opponent.AttackCenter;
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            for (int i = 0; i < 35; i++) yield return null;
            Assert.AreEqual(100f, _hero.Health, 0.01f, "預警結束前不得扣血");
            Assert.AreEqual(locked.x, _opponent.AttackCenter.x, 0.001f);
            Assert.AreEqual(locked.z, _opponent.AttackCenter.z, 0.001f);
            for (int i = 0; i < 15; i++) yield return null;
            Assert.AreEqual(80f, _hero.Health, 0.01f);
            Assert.AreEqual(1, _opponent.AttacksResolved);
        }

        [UnityTest]
        public IEnumerator DodgeOutsideTheWarningCircle_TakesNoDamage()
        {
            yield return Setup(true);
            TapOpponent();
            float deadline = Time.time + 0.4f;
            while (!_opponent.IsWarning && Time.time < deadline) yield return null;
            Assert.IsTrue(_opponent.IsWarning);
            _hero.GetComponent<HeroLocomotion>().WarpTo(_opponent.AttackCenter + Vector3.back * 3f);
            for (int i = 0; i < 50; i++) yield return null;
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            Assert.AreEqual(1, _opponent.AttacksResolved);
        }

        [UnityTest]
        public IEnumerator EitherKnockout_ResetsBoth_AndRequiresAnotherTap()
        {
            yield return Setup();
            TapOpponent();
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            Assert.IsFalse(_opponent.IsEngaged);
            for (int i = 0; i < 145; i++) yield return null;
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            Assert.AreEqual(1, _bootstrap.DuelStartCount);
            TapOpponent();
            Assert.AreEqual(2, _bootstrap.DuelStartCount);
            _hero.TakeDuelDamage(100f);
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            for (int i = 0; i < 155; i++) yield return null;
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
        }

        [UnityTest]
        public IEnumerator DormantOpponent_IsSelectableButCannotTakeDamage()
        {
            yield return Setup();
            int damagedEvents = 0;
            _opponent.OnDamaged += _ => damagedEvents++;
            _opponent.ReceiveDamage(40f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            Assert.AreEqual(0, damagedEvents);
            Assert.AreEqual(0, _opponent.RevealRefreshCount);
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState,
                "待機無敵不能把 IsAlive 關掉，否則點選入口消失");
        }

        [UnityTest]
        public IEnumerator RuneWall_BlocksTheStrike_ThenRemovingItAllowsDamage()
        {
            yield return Setup(true);
            TapOpponent();
            float deadline = Time.time + 0.4f;
            while (!_opponent.IsWarning && Time.time < deadline) yield return null;
            Assert.IsTrue(_opponent.IsWarning);

            RuneCaster caster = Object.FindObjectOfType<RuneCaster>();
            RuneWall wall = caster.Pool[0];
            Vector3 midpoint = (_hero.transform.position + _opponent.transform.position) * 0.5f;
            midpoint.y = new RuneTuning().WallHeight * 0.5f;
            wall.Activate(midpoint, Quaternion.LookRotation(_opponent.transform.position - _hero.transform.position),
                          Faction.BlueTeam, null, 0);
            for (int i = 0; i < 50; i++) yield return null;
            Assert.AreEqual(100f, _hero.Health, 0.01f, "牆在中間仍被打中");
            Assert.AreEqual(1, _opponent.AttacksResolved);

            wall.CollapseWall(false);
            deadline = Time.time + 3f;
            while (_hero.Health >= 100f && Time.time < deadline) yield return null;
            Assert.AreEqual(80f, _hero.Health, 0.01f, "撤牆後下一招應能命中");
        }

        [UnityTest]
        public IEnumerator OpponentCloseToHero_StillWalksAroundABlockingRuneWall()
        {
            yield return Setup(true);
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            RuneWall wall = Object.FindObjectOfType<RuneCaster>().Pool[0];
            Vector3 midpoint = (_hero.transform.position + _opponent.transform.position) * 0.5f;
            midpoint.y = new RuneTuning().WallHeight * 0.5f;
            wall.Activate(midpoint, Quaternion.LookRotation(_opponent.transform.position - _hero.transform.position),
                          Faction.BlueTeam, null, 0);
            float startingZ = _opponent.transform.position.z;
            float deadline = Time.time + 3f;
            while (_opponent.transform.position.z >= midpoint.z - 0.3f && Time.time < deadline)
                yield return null;
            Assert.Less(_opponent.transform.position.z, midpoint.z - 0.3f,
                "距離很近但隔牆時，對手仍應繞到牆另一側，而不是在牆前反覆空揮。位置="
                + _opponent.transform.position + " 轉向=" + _opponent.GetComponent<HeroLocomotion>().LastSteerMode
                + " 出手=" + _opponent.AttacksResolved + " 英雄HP=" + _hero.Health
                + " 牆存活=" + wall.IsAlive);
            Assert.Less(_opponent.transform.position.z, startingZ - 0.5f);
        }

        [UnityTest]
        public IEnumerator ARealTapAndAttackAnimation_CanFinishARound_ThenAnotherTapStartsTheNext()
        {
            yield return Setup(true);
            TapOpponent();
            _opponent.ReceiveDamage(240f, DamageType.Physical, _hero.gameObject);
            Assert.AreEqual(60f, _opponent.Health, 0.01f);
            TapOpponent();
            float deadline = Time.time + 0.8f;
            while (_bootstrap.DuelState == DuelRoundState.Active && Time.time < deadline) yield return null;
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState,
                "最後一擊應由真實點擊與動畫事件完成");
            for (int i = 0; i < 155; i++) yield return null;
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.AreEqual(2, _bootstrap.DuelStartCount);
        }

        [UnityTest]
        public IEnumerator Knockout_KeepsTheWallDuringPause_ThenClearsGridAndReturnsThePoolSlot()
        {
            yield return Setup();
            TapOpponent();
            int blockedBefore = _bootstrap.NavGrid.BlockedCount;
            RuneWall wall = Object.FindObjectOfType<RuneCaster>().Pool[0];
            wall.Activate(new Vector3(2f, 1f, 2f), Quaternion.identity, Faction.BlueTeam, null, 0);
            Assert.Greater(_bootstrap.NavGrid.BlockedCount, blockedBefore);
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            for (int i = 0; i < 60; i++) yield return null;
            Assert.IsTrue(wall.IsAlive, "2.5 秒停頓內場景尚不應清掉已立的牆");
            for (int i = 0; i < 95; i++) yield return null;
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.IsFalse(wall.IsAlive);
            Assert.AreEqual(blockedBefore, _bootstrap.NavGrid.BlockedCount, "舊牆不可留下鬼格");
            wall.Activate(new Vector3(2f, 1f, 2f), Quaternion.identity, Faction.BlueTeam, null, 0);
            Assert.IsTrue(wall.IsAlive, "池槽位應可再次借用");
            Assert.Greater(_bootstrap.NavGrid.BlockedCount, blockedBefore);
        }

        [UnityTest]
        public IEnumerator StartingDuel_CancelsAlreadyFlyingTurretBullets()
        {
            yield return Setup();
            TestTurret turret = Object.FindObjectOfType<TestTurret>();
            Projectile[] bullets = Object.FindObjectsOfType<Projectile>();
            turret.SetFiring(true);
            yield return null;
            bool hadFlyingBullet = false;
            for (int i = 0; i < bullets.Length; i++) hadFlyingBullet |= bullets[i].IsActive;
            Assert.IsTrue(hadFlyingBullet, "必須先有一發在路上，取消測試才有鑑別力");
            TapOpponent();
            Assert.IsFalse(turret.IsFiring);
            for (int i = 0; i < bullets.Length; i++) Assert.IsFalse(bullets[i].IsActive);
            RuneWall wall = Object.FindObjectOfType<RuneCaster>().Pool[0];
            wall.Activate(new Vector3(-5f, 1f, 6f), Quaternion.identity, Faction.BlueTeam, null, 0);
            float health = wall.Health;
            for (int i = 0; i < 45; i++) yield return null;
            Assert.AreEqual(health, wall.Health, 0.01f, "舊子彈不能在新局打到新牆");
            Assert.AreEqual(300f, _opponent.Health, 0.01f);
        }

        [UnityTest]
        public IEnumerator RockShield_AbsorbsTheFirstOpponentStrikeBeforeHeroHealth()
        {
            yield return Setup(true);
            TapOpponent();
            _bootstrap.Shield.Grant();
            float shieldBefore = _bootstrap.Shield.Amount;
            Assert.GreaterOrEqual(shieldBefore, 20f);
            float deadline = Time.time + 1.5f;
            while (_opponent.AttacksResolved == 0 && Time.time < deadline) yield return null;
            Assert.AreEqual(1, _opponent.AttacksResolved);
            Assert.AreEqual(100f, _hero.Health, 0.01f);
            Assert.AreEqual(shieldBefore - 20f, _bootstrap.Shield.Amount, 0.01f);
        }

        [UnityTest]
        public IEnumerator WarningCircle_UsesTheApprovedOnePointFiveMeterBoundary()
        {
            yield return Setup(true);
            TapOpponent();
            float deadline = Time.time + 0.4f;
            while (!_opponent.IsWarning && Time.time < deadline) yield return null;
            Assert.IsTrue(_opponent.IsWarning);
            _hero.GetComponent<HeroLocomotion>().WarpTo(_opponent.AttackCenter + Vector3.right * 1.49f);
            deadline = Time.time + 1f;
            while (_opponent.AttacksResolved == 0 && Time.time < deadline) yield return null;
            Assert.AreEqual(1, _opponent.AttacksResolved);
            Assert.AreEqual(80f, _hero.Health, 0.01f, "半徑內 1.49m 應命中");

            yield return Setup(true);
            TapOpponent();
            deadline = Time.time + 0.4f;
            while (!_opponent.IsWarning && Time.time < deadline) yield return null;
            Assert.IsTrue(_opponent.IsWarning);
            _hero.GetComponent<HeroLocomotion>().WarpTo(_opponent.AttackCenter + Vector3.right * 1.51f);
            deadline = Time.time + 1f;
            while (_opponent.AttacksResolved == 0 && Time.time < deadline) yield return null;
            Assert.AreEqual(1, _opponent.AttacksResolved);
            Assert.AreEqual(100f, _hero.Health, 0.01f, "半徑外 1.51m 應閃過");
        }

        [UnityTest]
        public IEnumerator StartingDuel_ClearsOldElementZones_AndButtonsStayBlockedUntilReset()
        {
            yield return Setup();
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(1, _bootstrap.ElementField.ActiveZoneCount);
            TapOpponent();
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount);
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(0, _bootstrap.ElementField.ActiveZoneCount);
            _opponent.ReceiveDamage(300f, DamageType.Physical, _hero.gameObject);
            for (int i = 0; i < 155; i++) yield return null;
            _bootstrap.PressElementWaterButton();
            Assert.AreEqual(1, _bootstrap.ElementField.ActiveZoneCount,
                "回待機後元素鈕應恢復可用，冷卻也應重置");
        }

        [UnityTest]
        public IEnumerator DelayedStart_AndTapAtTheEndOfKnockout_DoNotAutoRestart()
        {
            yield return Setup();
            _bootstrap.SetDuelLatencyPreset(80);
            TapOpponent();
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState,
                "80 ms 模擬下開局指令不該立即生效");
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.AreEqual(DuelRoundState.Active, _bootstrap.DuelState);
            Assert.AreEqual(1, _bootstrap.DuelStartCount);

            _hero.TakeDuelDamage(100f);
            Assert.AreEqual(DuelRoundState.KnockoutPause, _bootstrap.DuelState);
            for (int i = 0; i < 148; i++) yield return null;
            TapOpponent();
            for (int i = 0; i < 12; i++) yield return null;
            Assert.AreEqual(DuelRoundState.Dormant, _bootstrap.DuelState);
            Assert.AreEqual(1, _bootstrap.DuelStartCount,
                "KO 停頓末端排入的點擊不能在重置後自動開下一局");
        }
    }
}
