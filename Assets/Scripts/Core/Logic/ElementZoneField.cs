namespace Vow.Core.Logic
{
    public enum ElementZoneKind { None = 0, Water = 1, Burning = 2, Quicksand = 3, Steam = 4 }

    public struct ElementZone
    {
        public int Id; public ElementZoneKind Kind; public float X, Z, Radius, RemainingSeconds;
        public int FactionId; public bool Active;
    }

    // 區域名冊與生命週期（PHASE2_BATCH4_PLAN.md §2）。固定容量陣列、零配置、零 UnityEngine。
    public sealed class ElementZoneField
    {
        private readonly ElementZone[] _zones;
        private int _nextId = 1;

        public ElementZoneField(ElementTuning tuning)
        {
            int capacity = tuning.MaxLiveZones;
            if (capacity < 1) capacity = 1;
            _zones = new ElementZone[capacity];
        }

        public int Capacity => _zones.Length;

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _zones.Length; i++) if (_zones[i].Active) n++;
                return n;
            }
        }

        public bool TryGetBySlot(int slot, out ElementZone zone)
        {
            if (slot < 0 || slot >= _zones.Length || !_zones[slot].Active)
            {
                zone = default;
                return false;
            }
            zone = _zones[slot];
            return true;
        }

        public bool TryGetById(int id, out ElementZone zone)
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                if (!_zones[i].Active || _zones[i].Id != id) continue;
                zone = _zones[i];
                return true;
            }
            zone = default;
            return false;
        }

        // 回傳新區域 id（單調遞增，不重用）。滿了 → 擠掉「RemainingSeconds 最小」那一格（同值取 slot 小者），再放。
        public int Spawn(ElementZoneKind kind, float x, float z, float radius, float durationSeconds, int factionId)
        {
            int slot = FindFreeSlot();
            if (slot < 0) slot = FindSlotWithLeastTimeLeft();

            int id = _nextId;
            _nextId++;

            _zones[slot].Id = id;
            _zones[slot].Kind = kind;
            _zones[slot].X = x;
            _zones[slot].Z = z;
            _zones[slot].Radius = radius;
            _zones[slot].RemainingSeconds = durationSeconds;
            _zones[slot].FactionId = factionId;
            _zones[slot].Active = true;
            return id;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                if (!_zones[i].Active) return i;
            }
            return -1;
        }

        private int FindSlotWithLeastTimeLeft()
        {
            int best = 0;
            for (int i = 1; i < _zones.Length; i++)
            {
                if (_zones[i].RemainingSeconds < _zones[best].RemainingSeconds) best = i;
            }
            return best;
        }

        // 手動終止（被反應消耗）；已不存在 → false
        public bool Terminate(int id)
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                if (!_zones[i].Active || _zones[i].Id != id) continue;
                _zones[i].Active = false;
                return true;
            }
            return false;
        }

        public void Tick(float deltaSeconds)
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                if (!_zones[i].Active) continue;
                _zones[i].RemainingSeconds -= deltaSeconds;
                if (_zones[i].RemainingSeconds <= 0f) _zones[i].Active = false;
            }
        }

        // 取「點在其內、指定種類」的區域中圓心最近者；平手取 id 小者。沒有 → -1。
        public int FindNearestContaining(float x, float z, ElementZoneKind kind)
        {
            int bestId = -1;
            float bestDistSq = 0f;
            for (int i = 0; i < _zones.Length; i++)
            {
                if (!_zones[i].Active || _zones[i].Kind != kind) continue;
                if (!ElementGeometry.IsInsideCircle(x, z, _zones[i].X, _zones[i].Z, _zones[i].Radius)) continue;

                float dx = x - _zones[i].X;
                float dz = z - _zones[i].Z;
                float distSq = dx * dx + dz * dz;

                if (bestId != -1 && distSq > bestDistSq) continue;
                if (bestId != -1 && distSq == bestDistSq && _zones[i].Id >= bestId) continue;

                bestId = _zones[i].Id;
                bestDistSq = distSq;
            }
            return bestId;
        }
    }
}
