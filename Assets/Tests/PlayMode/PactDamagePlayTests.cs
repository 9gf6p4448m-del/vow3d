using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // 受擊探針：真傷若誤經普通聖所鉤子，300 HP 的 8% 會由 24 誤變成 20.4。
    public sealed class PactDamageProbeTarget : CombatTargetBehaviour
    {
        public void ConfigureWall(Faction owner)
        {
            Configure(300f, Faction.DestructibleWall);
            SetOwnerFaction(owner);
        }

        protected override float ScaleIncomingDamage(float amount) { return amount * 0.85f; }
        protected override void HandleDeath() { }
    }

    public sealed class PactDamagePlayTests
    {
        private GameObject _fieldObject;
        private GameObject _attackerObject;
        private GameObject _targetObject;
        private GameObject _outsideObject;
        private GameObject _heroObject;
        private ElementField _field;
        private PactDamageProbeTarget _target;
        private CombatTargetRoster _roster;
        private GameObject _enemyWallObject;
        private GameObject _alliedWallObject;
        private GameObject _neutralTargetObject;
        private GameObject _neutralWallObject;
        private PactDamageProbeTarget _outsideTarget;
        private CaptureMatchLogic _match;
        private Vector3 _center;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_fieldObject != null) Object.DestroyImmediate(_fieldObject);
            if (_attackerObject != null) Object.DestroyImmediate(_attackerObject);
            if (_targetObject != null) Object.DestroyImmediate(_targetObject);
            if (_enemyWallObject != null) Object.DestroyImmediate(_enemyWallObject);
            if (_alliedWallObject != null) Object.DestroyImmediate(_alliedWallObject);
            if (_neutralTargetObject != null) Object.DestroyImmediate(_neutralTargetObject);
            if (_neutralWallObject != null) Object.DestroyImmediate(_neutralWallObject);
            if (_outsideObject != null) Object.DestroyImmediate(_outsideObject);
            if (_heroObject != null) Object.DestroyImmediate(_heroObject);
        }

        private sealed class FivePointShield : IRockShield
        {
            public float Amount { get; private set; } = 5f;
            public float GrantedAmount => 5f;
            public float RemainingSeconds => 1f;
            public void Grant() { Amount = 5f; }
            public float Absorb(float incomingDamage)
            {
                float absorbed = Mathf.Min(Amount, incomingDamage);
                Amount -= absorbed;
                return incomingDamage - absorbed;
            }
            public void Clear() { Amount = 0f; }
        }

        private void Setup(PactTalent thirdTier)
        {
            Time.captureDeltaTime = 1f / 60f;
            _match = new CaptureMatchLogic(new CaptureTuning(), CaptureBoardSpec.V0100Sanctuary);
            Assert.IsTrue(_match.TryEnterCaptureMode());
            Assert.IsTrue(_match.TryStart());
            int tile = _match.Spec.MotherTile(0, 0);
            _center = new Vector3(_match.Spec.CenterX(tile), 0f, _match.Spec.CenterZ(tile));
            _match.SeedScoresForTest(750, 0);
            _match.Tick(0.01f, 1000f, 1000f, -1000f, -1000f);
            Assert.IsTrue(_match.TryChooseTalent(0, PactTalent.SwiftStep));
            Assert.IsTrue(_match.TryChooseTalent(0, PactTalent.TidalPull));
            Assert.IsTrue(_match.TryChooseTalent(0, thirdTier));

            _attackerObject = new GameObject("Pact blue attacker");
            _attackerObject.transform.position = _center;
            _targetObject = new GameObject("Pact red target");
            _targetObject.transform.position = _center;
            _target = _targetObject.AddComponent<PactDamageProbeTarget>();
            _target.Configure(300f, Faction.RedTeam);
            _outsideObject = new GameObject("Pact outside red target");
            _outsideObject.transform.position = _center + Vector3.right * 8f;
            _outsideTarget = _outsideObject.AddComponent<PactDamageProbeTarget>();
            _outsideTarget.Configure(300f, Faction.RedTeam);
            _roster = new CombatTargetRoster(6);
            Assert.IsTrue(_roster.Add(_target));
            Assert.IsTrue(_roster.Add(_outsideTarget));
            _fieldObject = new GameObject("Pact element field");
            _field = _fieldObject.AddComponent<ElementField>();
            var tuning = new ElementTuning();
            _field.Initialize(tuning, _roster, null, null, new ElementZoneView[tuning.MaxLiveZones + 1]);
            _field.ConfigurePactDamage(_match, null, _attackerObject.transform, null);
        }

        [UnityTest]
        public IEnumerator B302_SteamAndQuicksandWithoutBaseAoe_EachDealOneTrueHit()
        {
            Setup(PactTalent.ElementalAnnihilation);
            _field.CastWater(_center, 0);
            Assert.AreEqual(ElementReaction.Steam, _field.CastFire(_center, 0));
            Assert.AreEqual(276f, _target.Health, 0.001f, "Steam 的 0 AoE 仍須追加 24 真傷");
            Assert.AreEqual(300f, _outsideTarget.Health, 0.001f, "蒸氣圈外不可受傷");

            _field.ClearZonesForDuel();
            _field.CastWater(_center, 0);
            Assert.AreEqual(ElementReaction.Quicksand, _field.NotifyWallActivated(_center, Faction.BlueTeam));
            Assert.AreEqual(252f, _target.Health, 0.001f, "Quicksand 必須用新區域藍方陣營追加一次 24 真傷");
            Assert.AreEqual(300f, _outsideTarget.Health, 0.001f, "流沙圈外不可受傷");
            yield return null;
        }

        [Test]
        public void B302_ComboDamagesEnemyWallButNotAlliedWall()
        {
            Setup(PactTalent.ElementalAnnihilation);
            _enemyWallObject = new GameObject("Pact enemy wall");
            _enemyWallObject.transform.position = _center;
            var enemyWall = _enemyWallObject.AddComponent<PactDamageProbeTarget>();
            enemyWall.ConfigureWall(Faction.RedTeam);
            _alliedWallObject = new GameObject("Pact allied wall");
            _alliedWallObject.transform.position = _center;
            var alliedWall = _alliedWallObject.AddComponent<PactDamageProbeTarget>();
            alliedWall.ConfigureWall(Faction.BlueTeam);
            _neutralTargetObject = new GameObject("Pact neutral dummy");
            _neutralTargetObject.transform.position = _center;
            var neutralTarget = _neutralTargetObject.AddComponent<PactDamageProbeTarget>();
            neutralTarget.Configure(300f, Faction.Neutral);
            _neutralWallObject = new GameObject("Pact neutral wall");
            _neutralWallObject.transform.position = _center;
            var neutralWall = _neutralWallObject.AddComponent<PactDamageProbeTarget>();
            neutralWall.ConfigureWall(Faction.Neutral);
            Assert.IsTrue(_roster.Add(enemyWall));
            Assert.IsTrue(_roster.Add(alliedWall));
            Assert.IsTrue(_roster.Add(neutralTarget));
            Assert.IsTrue(_roster.Add(neutralWall));

            _field.CastWater(_center, 0);
            Assert.AreEqual(ElementReaction.Steam, _field.CastFire(_center, 0));
            Assert.AreEqual(276f, enemyWall.Health, 0.001f, "敵方牆在 Combo 範圍內應追加 8% 真傷");
            Assert.AreEqual(300f, alliedWall.Health, 0.001f, "己方牆不受 Combo 傷害");
            Assert.AreEqual(300f, neutralTarget.Health, 0.001f, "中立木樁不受 Combo 傷害");
            Assert.AreEqual(300f, neutralWall.Health, 0.001f, "中立牆不受 Combo 傷害");
        }

        [UnityTest]
        public IEnumerator B301_GeoFrenzyDirectAndBurn_RechecksAttackerPosition()
        {
            Setup(PactTalent.GeothermalFrenzy);
            Assert.AreEqual(ElementReaction.PlainFire, _field.CastFire(_center, 0));
            Assert.AreEqual(300f - 40f * 1.15f * 0.85f, _target.Health, 0.001f);

            // 火區留在母板塊，攻擊者移出後 DoT 不得保留施法當刻的 1.15 倍。
            float before = _target.Health;
            _attackerObject.transform.position = new Vector3(1000f, 0f, 1000f);
            for (int i = 0; i < 60; i++) yield return null;
            Assert.AreEqual(20f * 0.85f, before - _target.Health, 0.5f);
        }

        [UnityTest]
        public IEnumerator B302_FirestormUsesSector_AndTrueDamageBypassesSanctuary()
        {
            Setup(PactTalent.ElementalAnnihilation);
            Vector3 apex = _center - Vector3.forward * 2f;
            _field.CastFire(_center, 0);
            float before = _target.Health;
            Assert.AreEqual(ElementReaction.Firestorm, _field.CastWind(apex, Vector3.forward, 0));
            Assert.AreEqual(60f * 0.85f + 24f, before - _target.Health, 0.001f);
            Assert.AreEqual(300f, _outsideTarget.Health, 0.001f, "火浪扇形外不可受傷");
            yield return null;
        }

        [Test]
        public void B303_RedQuicksandCombo_HitsHeroThroughShieldWithoutSanctuaryReduction()
        {
            Setup(PactTalent.GeothermalFrenzy);
            Assert.IsTrue(_match.TryChooseTalent(1, PactTalent.SwiftStep));
            Assert.IsTrue(_match.TryChooseTalent(1, PactTalent.TidalPull));
            Assert.IsTrue(_match.TryChooseTalent(1, PactTalent.ElementalAnnihilation));

            _heroObject = new GameObject("Pact blue hero");
            _heroObject.transform.position = _center;
            HeroController hero = _heroObject.AddComponent<HeroController>();
            var shield = new FivePointShield();
            hero.ConfigureDuel(new DuelTuning(), shield);
            hero.SetDamageTakenPercent(85);
            _field.ConfigurePactDamage(_match, hero, _attackerObject.transform, _attackerObject.transform);

            _field.CastWater(_center, 1);
            Assert.AreEqual(ElementReaction.Quicksand, _field.NotifyWallActivated(_center, Faction.RedTeam));
            Assert.AreEqual(0f, shield.Amount, 0.001f, "紅方 Combo 真傷應先消耗 5 點護盾");
            Assert.AreEqual(97f, hero.Health, 0.001f, "100 HP 的 8% 真傷減 5 盾後扣 3 HP，不可套聖所 15% 減傷");
            Assert.AreEqual(300f, _target.Health, 0.001f, "紅方 Combo 不可打同隊對手");
        }
    }
}
