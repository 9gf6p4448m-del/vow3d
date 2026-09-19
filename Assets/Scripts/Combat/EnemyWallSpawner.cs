using UnityEngine;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Combat
{
    // 除錯鈕用的紅隊石牆池（Phase 2 批 3 的驗收設施）。
    //
    // 為什麼一定要有它（§4-4①）：單機只有藍隊英雄，「擁有者 != 攻擊者」這條判斷式對己方符印牆**恆假**，
    // 沒有敵方牆的話陣營校驗與破牆護盾在遊戲內零鑑別力。
    //
    // 池與玩家的符印牆名冊**完全分開**（§4-6）：敵方牆不得佔用玩家的 2 面上限。
    public sealed class EnemyWallSpawner : MonoBehaviour
    {
        [SerializeField] private RuneWall[] _pool;

        private RuneTuning _runeTuning;
        private Transform _hero;
        private RuneWallRoster _roster;

        public int PoolSize => _pool != null ? _pool.Length : 0;

        // 實際的敵方牆池（與 RuneCaster.Pool 對稱）。驗收要能拿到「組裝端切出去的到底是哪幾面」，
        // 不能自己另外湊一份，也不能用 FindObjectsOfType 把兩個池又混回一起。
        public RuneWall[] Pool => _pool;

        public void Initialize(RuneTuning runeTuning, ProjectileTuning projectileTuning, Transform hero, RuneWall[] pool)
        {
            _runeTuning = runeTuning;
            _hero = hero;
            if (pool != null) _pool = pool;

            int cap = _pool != null && _pool.Length > 0 ? _pool.Length : projectileTuning.EnemyWallPoolSize;
            _roster = new RuneWallRoster(cap);

            if (_pool == null) return;
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null) _pool[i].Initialize(runeTuning);
        }

        public int AliveCount()
        {
            if (_pool == null) return 0;
            int count = 0;
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null && _pool[i].IsAlive) count++;
            return count;
        }

        // 英雄正前方 QuickCastDistance（4m）生一面紅隊牆，壽命與己方牆相同。池滿時擠掉最舊那面（FIFO）。
        public RuneWall Spawn()
        {
            if (_pool == null || _hero == null || _runeTuning == null || _roster == null) return null;

            // 已經死掉的牆先退出名冊，免得名額被屍體佔著
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null && !_pool[i].IsAlive) _roster.Remove(i);

            int index = FindFreeSlot();
            if (index < 0) return null;

            int evicted = _roster.Add(index);
            if (evicted >= 0 && evicted < _pool.Length && _pool[evicted] != null) _pool[evicted].CollapseWall(false);

            Vector3 heroPos = _hero.position;
            Vector3 forward = _hero.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 position = heroPos + forward * _runeTuning.QuickCastDistance;
            position.y = heroPos.y + _runeTuning.WallHeight * 0.5f;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

            // caster 給 null：敵方牆不屬於玩家名冊，死亡時不得去釋放玩家的名額。
            _pool[index].Activate(position, rotation, Faction.RedTeam, null, index);
            return _pool[index];
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null && !_pool[i].IsAlive) return i;
            return -1;
        }
    }
}
