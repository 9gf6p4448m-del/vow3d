namespace Vow.Core.Logic
{
    // 創傷阻尼震屏：強度 = trauma²（小震幾乎無感、大震陡升），線性衰減至 0。
    // 優先級規則：新請求的 trauma 低於當前值時直接忽略——重震不會被緊接而來的輕震壓掉。
    public struct TraumaShake
    {
        public float Trauma;
        private float _decayPerSecond;

        public float Intensity => Trauma * Trauma;

        public void Request(float trauma, float duration)
        {
            if (trauma > 1f) trauma = 1f;
            if (trauma <= 0f || trauma < Trauma) return;

            Trauma = trauma;
            _decayPerSecond = duration > 0f ? trauma / duration : float.MaxValue;
        }

        public void Tick(float dt)
        {
            if (Trauma <= 0f) return;
            float next = Trauma - _decayPerSecond * dt;
            Trauma = next > 0f ? next : 0f;
        }
    }
}
