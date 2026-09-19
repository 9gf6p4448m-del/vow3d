namespace Vow.Core.Logic
{
    // 掃掠命中的分類。數值刻意由小到大＝「越後面越該優先處理」，但排序只看距離（見 SortByDistance）。
    public enum SweepHitKind
    {
        Ignore = 0,        // 不是戰鬥目標（地板、英雄、邊界、疊圖）：續飛
        FriendlyWall = 1,  // 己方石牆：走穿透規則
        BlockingWall = 2,  // 敵方／中立石牆：擋下並對牆造成子彈傷害
        Target = 3         // 一般目標（木樁）：命中結算
    }

    public struct SweepHit
    {
        public float Distance;
        public int Id;
        public SweepHitKind Kind;
    }

    // 一發子彈在「一幀的位移線段」上的命中處理規則（PHASE2_BATCH3_PLAN.md §2）。
    // 不依賴 UnityEngine；固定容量陣列，執行期零配置。
    public sealed class ProjectileFlightLogic
    {
        private readonly int[] _penetratedWallIds;
        private int _penetratedCount;
        private float _damageMultiplier = 1f;

        public ProjectileFlightLogic(int maxPenetratedWalls)
        {
            _penetratedWallIds = new int[maxPenetratedWalls < 1 ? 1 : maxPenetratedWalls];
        }

        // Reset 後為 1；每次穿透乘上該牆回傳的倍率（§4-8：連乘，方向與 GDD §291 禁的「穿透疊加增傷」相反）。
        public float DamageMultiplier => _damageMultiplier;

        public void Reset()
        {
            _penetratedCount = 0;
            _damageMultiplier = 1f;
        }

        // 插入排序（n ≤ 8，零配置、決定性）：距離升冪；同距離取 Id 小者。
        // 順序在這裡決定結果（近的牆先擋、先穿），與點擊挑選不同。
        public static void SortByDistance(SweepHit[] hits, int count)
        {
            if (hits == null || count < 2) return;
            if (count > hits.Length) count = hits.Length;

            for (int i = 1; i < count; i++)
            {
                SweepHit key = hits[i];
                int j = i - 1;
                while (j >= 0 && IsAfter(hits[j], key))
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = key;
            }
        }

        private static bool IsAfter(SweepHit a, SweepHit b)
        {
            if (a.Distance > b.Distance) return true;
            if (a.Distance < b.Distance) return false;
            return a.Id > b.Id;
        }

        public bool HasPenetrated(int wallId)
        {
            for (int i = 0; i < _penetratedCount; i++)
                if (_penetratedWallIds[i] == wallId) return true;
            return false;
        }

        // 同一發子彈對同一面牆只算一次：重複呼叫不得再乘一次倍率（子彈在牆內跨多幀也只扣一次額度）。
        public void RecordPenetration(int wallId, float multiplier)
        {
            if (HasPenetrated(wallId)) return;
            if (_penetratedCount < _penetratedWallIds.Length) _penetratedWallIds[_penetratedCount++] = wallId;
            _damageMultiplier *= multiplier;
        }

        // 一幀要掃掠的線段長度。speed>0、dt>0 時恆為正，所以只當算式、不得拿來當判斷式（§4-4③）。
        public static float StepLength(float speed, float deltaSeconds)
        {
            return speed * deltaSeconds;
        }
    }
}
