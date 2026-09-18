using System.Collections.Generic;
using UnityEngine;
using Vow.Core;

namespace Vow.Combat
{
    // Collider → 戰鬥目標的查表。取代每次點擊都 GetComponentInParent 的做法（後者在 Editor 下找不到時會產生 GC）。
    public sealed class ColliderTargetRegistry : ICombatTargetResolver
    {
        private readonly Dictionary<int, ICombatTarget> _byColliderId = new Dictionary<int, ICombatTarget>(64);

        public void Register(CombatTargetBehaviour target)
        {
            if (target == null) return;
            Collider[] colliders = target.TargetColliders;
            for (int i = 0; i < colliders.Length; i++)
                _byColliderId[colliders[i].GetInstanceID()] = target;
        }

        public void Unregister(CombatTargetBehaviour target)
        {
            if (target == null) return;
            Collider[] colliders = target.TargetColliders;
            for (int i = 0; i < colliders.Length; i++)
                _byColliderId.Remove(colliders[i].GetInstanceID());
        }

        public bool TryResolve(Collider collider, out ICombatTarget target)
        {
            if (collider == null)
            {
                target = null;
                return false;
            }
            return _byColliderId.TryGetValue(collider.GetInstanceID(), out target);
        }
    }
}
