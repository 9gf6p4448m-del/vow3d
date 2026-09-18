namespace Vow.Core.Logic
{
    // 0 ~ 999 的字串預先建好：飄字與 FPS 顯示每幀要字串，但 int.ToString() 每次都會配置（紅線 4）。
    public static class IntStringCache
    {
        public const int MaxValue = 999;

        private static readonly string[] Cache = Build();

        public static string Get(int value)
        {
            if (value < 0) value = 0;
            if (value > MaxValue) value = MaxValue;
            return Cache[value];
        }

        private static string[] Build()
        {
            string[] cache = new string[MaxValue + 1];
            for (int i = 0; i <= MaxValue; i++) cache[i] = i.ToString();
            return cache;
        }
    }
}
