using NUnit.Framework;
using Vow.Core.Logic;

namespace Vow.Tests
{
    // PHASE2_BATCH4_PLAN.md §5 V2-p～V2-s：區域名冊的池滿擠出、Tick 到期、燃燒區 DoT 總量。
    public sealed class ElementZoneFieldTests
    {
        private const float Epsilon = 1e-4f;

        // V2-p Spawn_WhenTheFieldIsFull_EvictsTheZoneWithTheLeastTimeLeft
        [Test]
        public void Spawn_WhenTheFieldIsFull_EvictsTheZoneWithTheLeastTimeLeft()
        {
            ElementTuning tuning = new ElementTuning(); // MaxLiveZones == 6
            ElementZoneField field = new ElementZoneField(tuning);

            float[] remaining = { 5.0f, 1.0f, 4.0f, 3.0f, 2.0f, 6.0f };
            int[] ids = new int[remaining.Length];
            for (int i = 0; i < remaining.Length; i++)
            {
                ids[i] = field.Spawn(ElementZoneKind.Water, i, 0f, 1f, remaining[i], 0);
            }
            Assert.AreEqual(6, field.ActiveCount, "六格全滿");

            int newId = field.Spawn(ElementZoneKind.Burning, 99f, 99f, 2f, 10f, 5);

            Assert.GreaterOrEqual(newId, 0, "池滿時第 7 次 Spawn 必須真的生得出新 id，不得回 -1");
            Assert.AreEqual(6, field.ActiveCount, "擠掉一個、放進一個，總數仍是 6");

            Assert.IsTrue(field.TryGetBySlot(1, out ElementZone evictedSlot));
            Assert.AreEqual(newId, evictedSlot.Id, "剩餘時間最短（1.0s）的那一格必須被擠掉、放新區域");
            Assert.AreEqual(ElementZoneKind.Burning, evictedSlot.Kind);

            for (int i = 0; i < remaining.Length; i++)
            {
                if (i == 1) continue;
                Assert.IsTrue(field.TryGetBySlot(i, out ElementZone zone));
                Assert.AreEqual(ids[i], zone.Id, $"slot {i} 不該被動到");
                Assert.AreEqual(remaining[i], zone.RemainingSeconds, Epsilon, $"slot {i} 的剩餘時間不該被動到");
            }
        }

        // V2-q Tick_ExpiresAWaterZoneExactlyAtSixSeconds
        [Test]
        public void Tick_ExpiresAWaterZoneExactlyAtSixSeconds()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int id = field.Spawn(ElementZoneKind.Water, 0f, 0f, tuning.WaterRadius, tuning.WaterDurationSeconds, 0);

            field.Tick(5.9f);
            Assert.IsTrue(field.TryGetById(id, out _), "t=5.9s 水域必須還在");

            field.Tick(0.1f); // 累計 6.0s
            Assert.IsFalse(field.TryGetById(id, out _), "t=6.0s 水域必須到期消失");
        }

        // V2-r Tick_ExpiresAQuicksandAtThreePointFive_AndSteamAtThree
        [Test]
        public void Tick_ExpiresAQuicksandAtThreePointFive_AndSteamAtThree()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int quicksandId = field.Spawn(ElementZoneKind.Quicksand, 0f, 0f, tuning.ReactionRadius, tuning.QuicksandDurationSeconds, 0);
            int steamId = field.Spawn(ElementZoneKind.Steam, 10f, 10f, tuning.ReactionRadius, tuning.SteamDurationSeconds, 0);

            field.Tick(3.0f);
            Assert.IsFalse(field.TryGetById(steamId, out _), "t=3.0s 蒸氣必須消失");
            Assert.IsTrue(field.TryGetById(quicksandId, out _), "t=3.0s 流沙仍在");

            field.Tick(0.5f); // 累計 3.5s
            Assert.IsFalse(field.TryGetById(quicksandId, out _), "t=3.5s 流沙必須也消失");
        }

        // V2-s BurningZone_OverFourSeconds_DealsEightyDamageInTotal
        [Test]
        public void BurningZone_OverFourSeconds_DealsEightyDamageInTotal()
        {
            ElementTuning tuning = new ElementTuning();
            ElementZoneField field = new ElementZoneField(tuning);
            int id = field.Spawn(ElementZoneKind.Burning, 0f, 0f, tuning.BurnRadius, tuning.BurnDurationSeconds, 0);

            const float dt = 1f / 60f;
            const int totalTicks = 240; // 240 * (1/60) == 4.0s
            float totalDamage = 0f;

            for (int i = 0; i < totalTicks; i++)
            {
                if (field.TryGetById(id, out ElementZone zone) && zone.Active)
                {
                    totalDamage += tuning.BurnDamagePerSecond * dt;
                }
                field.Tick(dt);
            }

            Assert.AreEqual(80f, totalDamage, 0.5f, "4.0s 內的總傷害必須是 80（容差 0.5）");

            // 240 次 dt=1/60 的浮點累減不保證恰好落在 <=0（可能還留幾微秒的殘差），
            // 所以再多 Tick 一次確保跨過殘差、真的到期，而不是卡在邊界上斷言。
            field.Tick(dt);
            Assert.IsFalse(field.TryGetById(id, out _), "4.0s 之後燃燒區必須到期，不再累加");

            field.Tick(dt);
            Assert.IsFalse(field.TryGetById(id, out _), "到期後繼續 Tick 不得復活");
        }
    }
}
