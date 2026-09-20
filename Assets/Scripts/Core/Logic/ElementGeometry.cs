using System;

namespace Vow.Core.Logic
{
    // 圓形／扇形的點內判定與名冊過濾（PHASE2_BATCH4_PLAN.md §2）。零 UnityEngine、不用 Linq、零配置。
    public static class ElementGeometry
    {
        // 邊界語意定死：含邊界（<=）。一律平方比較，不開根號。
        public static bool IsInsideCircle(float px, float pz, float cx, float cz, float radius)
        {
            float dx = px - cx;
            float dz = pz - cz;
            float radiusSq = radius * radius;
            return (dx * dx + dz * dz) <= radiusSq;
        }

        // dir 內部正規化；dir 長度 < 1e-6 → 一律 false。頂點本身（dist == 0）→ true。
        // 邊界語意與圓形一致：含邊界（距離 == range 或角度 == 半角都算內）。
        public static bool IsInsideSector(float px, float pz, float apexX, float apexZ,
                                          float dirX, float dirZ, float range, float totalAngleDegrees)
        {
            double dirLenSq = (double)dirX * dirX + (double)dirZ * dirZ;
            if (dirLenSq < 1e-12) return false;

            double vx = (double)px - apexX;
            double vz = (double)pz - apexZ;
            double distSq = vx * vx + vz * vz;
            if (distSq < 1e-12) return true;

            double dist = Math.Sqrt(distSq);
            if (dist > range) return false;

            double dirLen = Math.Sqrt(dirLenSq);
            double dot = (vx * dirX + vz * dirZ) / (dist * dirLen);
            if (dot > 1.0) dot = 1.0;
            else if (dot < -1.0) dot = -1.0;

            double halfAngleRad = totalAngleDegrees * 0.5 * (Math.PI / 180.0);
            double cosHalfAngle = Math.Cos(halfAngleRad);
            return dot >= cosHalfAngle;
        }

        // 平行陣列輸入；回傳寫進 outIndices 的筆數，索引為升冪；outIndices 滿了就停（回傳＝容量）。
        public static int CollectInsideCircle(float[] xs, float[] zs, int count,
                                              float cx, float cz, float radius, int[] outIndices)
        {
            int written = 0;
            for (int i = 0; i < count && written < outIndices.Length; i++)
            {
                if (!IsInsideCircle(xs[i], zs[i], cx, cz, radius)) continue;
                outIndices[written] = i;
                written++;
            }
            return written;
        }

        public static int CollectInsideSector(float[] xs, float[] zs, int count, float apexX, float apexZ,
                                              float dirX, float dirZ, float range, float totalAngleDegrees,
                                              int[] outIndices)
        {
            int written = 0;
            int capacity = outIndices.Length;
            for (int i = 0; i < count && written < capacity; i++)
            {
                if (!IsInsideSector(xs[i], zs[i], apexX, apexZ, dirX, dirZ, range, totalAngleDegrees)) continue;
                outIndices[written] = i;
                written++;
            }
            return written;
        }
    }
}
