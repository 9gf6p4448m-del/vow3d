namespace Vow.Core.Logic
{
    // 固定容量的延遲佇列（環形緩衝、零配置），用來模擬網路往返延遲。
    //
    // 兩條鐵律：
    //   1. 保序——先進先出：只看最前面那一筆到期了沒，後面的就算抽到較短的延遲也排在它後面，抖動再大都不會讓後發的指令先到。
    //   2. 不丟——佇列滿時把「最舊的一筆」交還給呼叫端立即送出，絕不默默丟棄。吃指令是這個專案最忌諱的事。
    public sealed class DelayedEventQueue<T> where T : struct
    {
        private readonly T[] _items;
        private readonly double[] _dueTimes;
        private int _head;
        private int _count;

        public DelayedEventQueue(int capacity)
        {
            if (capacity < 1) capacity = 1;
            _items = new T[capacity];
            _dueTimes = new double[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;

        // 回傳 true 表示佇列已滿、evicted 是被擠出來的最舊事件，呼叫端必須立刻送出它。
        public bool Enqueue(T item, double now, double delaySeconds, out T evicted)
        {
            bool overflowed = false;
            evicted = default;

            if (_count == _items.Length)
            {
                evicted = _items[_head];
                _items[_head] = default;
                _head = (_head + 1) % _items.Length;
                _count--;
                overflowed = true;
            }

            double due = now + (delaySeconds > 0.0 ? delaySeconds : 0.0);

            int tail = (_head + _count) % _items.Length;
            _items[tail] = item;
            _dueTimes[tail] = due;
            _count++;
            return overflowed;
        }

        public bool TryDequeueDue(double now, out T item)
        {
            if (_count == 0 || _dueTimes[_head] > now)
            {
                item = default;
                return false;
            }
            return TryDequeueAny(out item);
        }

        // 不看到期時間，取出最舊的一筆（關閉延遲模擬時用來依序清空）。
        public bool TryDequeueAny(out T item)
        {
            if (_count == 0)
            {
                item = default;
                return false;
            }

            item = _items[_head];
            _items[_head] = default; // 釋放可能持有的物件引用
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }
    }

    // 決定性的延遲抖動（xorshift32）：同一顆種子產生同一串延遲，測試可重現。
    public struct LatencyJitter
    {
        private uint _state;

        public LatencyJitter(uint seed)
        {
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        // 回傳 [minSeconds, maxSeconds] 之間的延遲。
        public double Next(double minSeconds, double maxSeconds)
        {
            if (maxSeconds <= minSeconds) return minSeconds;

            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            double unit = (_state & 0xFFFFFF) / (double)0xFFFFFF;
            return minSeconds + (maxSeconds - minSeconds) * unit;
        }
    }
}
