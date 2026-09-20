using Vow.Core;

namespace Vow.Combat
{
    // 可列舉的戰鬥目標名冊（PHASE2_BATCH4_PLAN.md §4-5）。固定容量陣列、去重、零配置。
    //
    // 為什麼不重用 `ColliderTargetRegistry`：它是 `Dictionary<int, ICombatTarget>`，鍵是 collider
    // 的 instance id——不可列舉，而且一個目標有多個 Collider 時會出現在字典裡多次，元素 AOE 會對它
    // 重複結算。兩者在 `Phase1Bootstrap` **成對**註冊／註銷。
    //
    // 為什麼存 `CombatTargetBehaviour` 而不是 `ICombatTarget`：AOE 要讀 `IFactionOwned.OwnerFaction`
    // （自家石牆不吃自家元素傷害，§4-7）、要推進每個目標的受擊顯影倒數，而全專案的 `ICombatTarget`
    // 實作只有這一個底座。它本身就是 `ICombatTarget`，`Get(i)` 仍以介面型別交出去。
    public sealed class CombatTargetRoster
    {
        private readonly CombatTargetBehaviour[] _entries;
        private int _count;

        public CombatTargetRoster(int capacity)
        {
            if (capacity < 1) capacity = 1;
            _entries = new CombatTargetBehaviour[capacity];
        }

        public int Capacity => _entries.Length;
        public int Count => _count;

        public ICombatTarget Get(int index)
        {
            return index >= 0 && index < _count ? _entries[index] : null;
        }

        public CombatTargetBehaviour GetBehaviour(int index)
        {
            return index >= 0 && index < _count ? _entries[index] : null;
        }

        // 已在名冊裡就不再加（去重）。滿了回 false——靜默丟掉一個目標會讓 AOE 少打一個東西，
        // 呼叫端要看得到。
        public bool Add(CombatTargetBehaviour target)
        {
            if (target == null) return false;
            for (int i = 0; i < _count; i++) if (ReferenceEquals(_entries[i], target)) return false;
            if (_count >= _entries.Length) return false;

            _entries[_count] = target;
            _count++;
            return true;
        }

        public bool Remove(CombatTargetBehaviour target)
        {
            if (target == null) return false;
            for (int i = 0; i < _count; i++)
            {
                if (!ReferenceEquals(_entries[i], target)) continue;
                _count--;
                _entries[i] = _entries[_count];
                _entries[_count] = null;
                return true;
            }
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++) _entries[i] = null;
            _count = 0;
        }
    }
}
