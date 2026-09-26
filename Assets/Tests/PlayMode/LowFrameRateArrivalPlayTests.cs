using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Combat.Feedback;
using Vow.Core;

namespace Vow.Tests.PlayMode
{
    // v0.9.1（計畫修-10）：低幀率下點地移動不得衝過目的地。
    // 瀏覽器／手機掉幀時每幀 0.2～0.33s，舊版 HeroLocomotion.Step 每幀直接走「速度×dt」（5.5m/s → 1.4～1.8m），
    // 不夾在剩餘距離內，英雄會在目的地前後來回過衝約 1m（線上 V9-D05 因此衝出 4 號光圈、佔領歸零）。
    // 這裡用大 dt 走真實點地鏈路（ScriptedInput.TapGround → HeroController → HeroLocomotion），
    // 逐幀記錄沿行進方向的投影：全程不得越過目的地，最後停點離目的地 ≤0.05m。
    public sealed class LowFrameRateArrivalPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const int FrameBudget = 40;       // 1/3 s × 40 ＝ 13.3 s 遊戲時間，遠大於 7.8m ÷ 5.5m/s
        private const float Tolerance = 0.05f;

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; }

        [UnityTest]
        public IEnumerator TapToMove_AtThreeFps_StopsOnTheDestination_WithoutOvershooting()
        {
            yield return WalkAndCheck(1f / 3f);
        }

        [UnityTest]
        public IEnumerator TapToMove_AtFourFps_StopsOnTheDestination_WithoutOvershooting()
        {
            yield return WalkAndCheck(1f / 4f);
        }

        private static IEnumerator WalkAndCheck(float dt)
        {
            Time.captureDeltaTime = dt;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            HeroController hero = Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(hero, "場景裡找不到 HeroController");
            ScriptedInput input = new ScriptedInput();
            hero.Initialize(input, Object.FindObjectOfType<CombatFeedbackService>(), Camera.main, new RecordingHaptics());

            Vector3 start = hero.transform.position;
            Vector3 destination = new Vector3(-6f, 0f, -5f);
            Vector3 travel = destination - start;
            travel.y = 0f;
            float total = travel.magnitude;
            Vector3 direction = travel / total;

            input.TapGround(destination);

            float maxProjection = float.MinValue;
            int maxProjectionFrame = -1;
            for (int frame = 0; frame < FrameBudget; frame++)
            {
                yield return null;
                Vector3 offset = hero.transform.position - start;
                offset.y = 0f;
                float projection = Vector3.Dot(offset, direction);
                if (projection > maxProjection)
                {
                    maxProjection = projection;
                    maxProjectionFrame = frame;
                }
            }

            Vector3 rest = hero.transform.position - destination;
            rest.y = 0f;

            // 行為斷言在前：越過目的地／停點偏離，才是這條要守的東西。
            Assert.LessOrEqual(maxProjection, total + Tolerance,
                "dt=" + dt + "：英雄沿行進方向越過目的地 " + (maxProjection - total) + "m（第 " + maxProjectionFrame +
                " 幀，投影 " + maxProjection + " ／ 目的地 " + total + "）");
            Assert.LessOrEqual(rest.magnitude, Tolerance,
                "dt=" + dt + "：停點離目的地 " + rest.magnitude + "m，位置 " + hero.transform.position);
        }
    }
}
