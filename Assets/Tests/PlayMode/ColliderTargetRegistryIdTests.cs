#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // Editor-only Unity tests: PlayMode assembly already references Combat/Core.
    // PureLogic.Tests includes EditMode/**/*.cs only and must not acquire Unity references.
    public sealed class ColliderTargetRegistryIdTests
    {
        [Test]
        public void IdAndColliderLookups_ReturnTheSameLivePooledTarget_AsOwnerAndHealthChange()
        {
            var root=new GameObject("RegistryIdPoolLifecycle");
            var ground=new GameObject("RegistryIdUnregisteredGround");
            try
            {
                BoxCollider collider=root.AddComponent<BoxCollider>();
                RuneWall wall=root.AddComponent<RuneWall>();
                wall.Initialize(new RuneTuning());
                var registry=new ColliderTargetRegistry();
                registry.Register(wall);
                int id=collider.GetInstanceID();
                Assert.AreNotEqual(0,id);
                AssertLookupPair(registry,collider,id,wall);
                Assert.IsFalse(registry.TryResolve(0,out ICombatTarget zero)); Assert.IsNull(zero);
                Assert.IsFalse(registry.TryResolve((Collider)null,out ICombatTarget absent)); Assert.IsNull(absent);
                Collider unregistered=ground.AddComponent<BoxCollider>();
                Assert.IsFalse(registry.TryResolve(unregistered.GetInstanceID(),out ICombatTarget unknown)); Assert.IsNull(unknown);

                wall.Activate(Vector3.zero,Quaternion.identity,Faction.BlueTeam,null,0);
                AssertLookupPair(registry,collider,id,wall);
                Assert.IsTrue(wall.IsAlive); Assert.AreEqual(Faction.BlueTeam,wall.OwnerFaction);
                wall.CollapseWall(false);
                AssertLookupPair(registry,collider,id,wall);
                Assert.IsFalse(wall.IsAlive); Assert.IsFalse(collider.enabled);
                wall.Activate(Vector3.right,Quaternion.identity,Faction.RedTeam,null,0);
                AssertLookupPair(registry,collider,id,wall);
                Assert.IsTrue(wall.IsAlive); Assert.AreEqual(Faction.RedTeam,wall.OwnerFaction);
                Assert.AreEqual(id,collider.GetInstanceID(),"池重用仍查同一實體，不快取owner/health");

                registry.Unregister(wall);
                AssertMissingPair(registry,collider,id);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(ground); }
        }

        [Test]
        public void UnregisterThenRefreshThenRegister_RemovesOldChildIds_AndTracksTheReplacement()
        {
            var root=new GameObject("RegistryIdColliderReplacement");
            try
            {
                BoxCollider original=root.AddComponent<BoxCollider>();
                var child=new GameObject("OriginalRegistryChild"); child.transform.SetParent(root.transform,false);
                SphereCollider oldChild=child.AddComponent<SphereCollider>();
                RuneWall wall=root.AddComponent<RuneWall>(); wall.Initialize(new RuneTuning());
                var registry=new ColliderTargetRegistry(); registry.Register(wall);
                int originalId=original.GetInstanceID(),oldId=oldChild.GetInstanceID();
                AssertLookupPair(registry,original,originalId,wall); AssertLookupPair(registry,oldChild,oldId,wall);

                // Existing contract order is significant: unregister the old cached set first.
                registry.Unregister(wall);
                AssertMissingPair(registry,original,originalId); AssertMissingPair(registry,oldChild,oldId);
                Object.DestroyImmediate(child);
                Assert.IsTrue(oldChild==null,"Unity destroyed Collider retains original null semantics");
                Assert.IsFalse(registry.TryResolve(oldChild,out ICombatTarget destroyed)); Assert.IsNull(destroyed);
                var replacement=new GameObject("ReplacementRegistryChild"); replacement.transform.SetParent(root.transform,false);
                CapsuleCollider next=replacement.AddComponent<CapsuleCollider>();
                wall.RefreshColliderCache(); registry.Register(wall);
                AssertLookupPair(registry,original,originalId,wall);
                AssertLookupPair(registry,next,next.GetInstanceID(),wall);
                if(next.GetInstanceID()!=oldId)
                { Assert.IsFalse(registry.TryResolve(oldId,out ICombatTarget stale)); Assert.IsNull(stale); }
                registry.Unregister(wall);
                AssertMissingPair(registry,original,originalId); AssertMissingPair(registry,next,next.GetInstanceID());
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AssertLookupPair(ColliderTargetRegistry registry,Collider collider,int id,ICombatTarget expected)
        {
            Assert.IsTrue(registry.TryResolve(collider,out ICombatTarget byCollider));
            Assert.IsTrue(registry.TryResolve(id,out ICombatTarget byId));
            Assert.AreSame(expected,byCollider); Assert.AreSame(byCollider,byId);
        }
        private static void AssertMissingPair(ColliderTargetRegistry registry,Collider collider,int id)
        {
            Assert.IsFalse(registry.TryResolve(collider,out ICombatTarget byCollider)); Assert.IsNull(byCollider);
            Assert.IsFalse(registry.TryResolve(id,out ICombatTarget byId)); Assert.IsNull(byId);
        }
    }
}
#endif
