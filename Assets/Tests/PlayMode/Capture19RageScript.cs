using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // V9-C07（V090_ENCIRCLE_PLAN.md §3）：只用真實輸入的狂怒劇本。
    // 時間 0＝開局點擊那一幀（真實點 CAPTURE，再真實點對手）。之後每一步都是「開局後第幾秒、真實點一次地面的世界座標」，
    // 全部走 WorldTapInput.SendScreenTap；本檔整份（劇本＋測試）不得出現任何測試入口，條文 (e) 的 grep 對整檔執行。
    //
    // 劇本在做什麼（實測過程見 vow-toolchain/v090c07-explore.md）：
    //   ① 0.5～5.5s：英雄往北走到 (2.5, 1.2) 當誘餌；對手約 5.2s 翻下 1 號後轉頭追打。
    //   ② 7.0～11.5s：英雄分段往南，把對手一路帶到母板塊 12 的東南外側站定，被打到倒地（約 21s）。對手停在 12 號光圈內，
    //      先把 12 翻掉（紅方孤島 → 立刻中立，藍只剩 13、14），之後因為人在 12 光圈內、最近的非紅塊永遠是 12，就原地停車。
    //   ③ 27.0～28.3s：英雄復活（13 號復活點）後走到 5 號光圈，約 32.7s 翻藍（5 號只經母板塊 14 連回）。
    //   ④ 33.5～39.0s：繞到 12 號附近讓停車的對手轉頭，再往南、往西北繞到 14 號塔心站定；對手跟到 1.8m 外、也在 14 號
    //      光圈內 → 爭奪凍結，兩種幀率的對手都在這裡對齊。
    //   ⑤ 43.5s：英雄往西北跨出光圈；對手跟上仍在 14 號光圈內，從這一刻開始引導 → 約 47.5s 紅翻 14 號，
    //      藍 5 號連不回任何母板塊而中立化（斷能）；藍方落後 >15% → 藍方狂怒。
    //   設計原則：翻完 1 號之後「對手自己挑下一塊」幾乎都是等距平手，結果跟幀率與 0.5m 的點擊誤差有關，所以劇本只靠
    //   「停車」與「爭奪同步」決定時序。往西南的點擊會落進左側除錯面板，往東南要避開右下符印鈕，所以路線是分段繞的。
    public static class Capture19RageScript
    {
        public struct Step
        {
            public readonly float Seconds;
            public readonly Vector3 World;
            public readonly string Note;
            public Step(float seconds, float x, float z, string note) { Seconds = seconds; World = new Vector3(x, 0f, z); Note = note; }
        }

        public static readonly Step[] Steps =
        {
            new Step(0.5f, 2.0f, -9.0f, "walk north"),
            new Step(2.5f, 2.5f, -3.0f, "walk north"),
            new Step(5.5f, 2.5f, 1.2f, "bait"),
            new Step(7.0f, 4.0f, -3.5f, "lead south"),
            new Step(8.5f, 5.5f, -8.2f, "lead south"),
            new Step(10.0f, 6.5f, -11.0f, "lead south"),
            new Step(11.5f, 7.43f, -14.13f, "stand SE of circle 12 until knocked out"),
            new Step(27.0f, -3.5f, -10.0f, "after respawn: walk north-west"),
            new Step(28.3f, -7.2f, -3.2f, "stand in circle 5 until it flips blue"),
            new Step(33.5f, -5.7f, -8.0f, "walk south"),
            new Step(34.5f, -4.2f, -12.8f, "walk south"),
            new Step(35.5f, 2.5f, -13.0f, "bait again near 12"),
            new Step(36.9f, 2.5f, -17.0f, "walk south"),
            new Step(37.8f, -2.5f, -15.0f, "walk north-west"),
            new Step(39.0f, -6.5625f, -11.3671875f, "stand on tower 14 centre (contested)"),
            new Step(43.5f, -9.0625f, -8.8671875f, "step out north-west of circle 14"),
        };

        public static string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"clause\": \"V9-C07\",\n");
            sb.Append("  \"opening\": [\"tap CAPTURE button\", \"tap the opponent (position + 1m up)\"],\n");
            sb.Append("  \"time_zero\": \"the opening tap on the opponent\",\n");
            sb.Append("  \"steps\": [\n");
            for (int i = 0; i < Steps.Length; i++)
            {
                Step s = Steps[i];
                sb.Append("    {\"seconds\": ").Append(s.Seconds.ToString("R", CultureInfo.InvariantCulture))
                  .Append(", \"kind\": \"ground\", \"x\": ").Append(s.World.x.ToString("R", CultureInfo.InvariantCulture))
                  .Append(", \"y\": 0, \"z\": ").Append(s.World.z.ToString("R", CultureInfo.InvariantCulture))
                  .Append(", \"note\": \"").Append(s.Note).Append("\"}")
                  .Append(i + 1 < Steps.Length ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }
    }

    public sealed class Capture19RageScriptPlayTests
    {
        private const string SceneName = "VOW_Phase1_Greybox";
        private const float TapMarginPixels = 16f;   // §3 共同遵守：投影離四邊都 ≥ 16px
        private const int TileCount = 19;
        private const float WindowSeconds = 52f;     // 每次重播最多看開局後 52 秒（固定窗口）
        private const float Sixtieth = 1f / 60f;
        private const float Sixth = 1f / 6f;
        private const float ShiftMeters = 0.5f;

        private static string ToolchainDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "vow-toolchain"));

        private Phase1Bootstrap _bootstrap;
        private HeroController _hero;
        private TrainingOpponent _opponent;

        private struct RunResult
        {
            public int Side;            // -1 沒觸發；0 藍；1 紅
            public int Frame;           // f_r：開局點擊之後第幾幀觸發
            public float Seconds;       // f_r × dt
            public int NeutralizedBefore, NeutralizedAt;   // 觸發方中立化數：觸發前一幀／觸發那一幀
            public int BlueScore, RedScore;
            public float MinTapMargin;  // 本次所有點擊點離畫面四邊的最小距離（px）
            public string Trace;
        }

        [TearDown]
        public void TearDown() { Time.captureDeltaTime = 0f; }

        private IEnumerator Setup(float dt)
        {
            Time.captureDeltaTime = dt;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            _bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            _hero = Object.FindObjectOfType<HeroController>();
            _opponent = Object.FindObjectOfType<TrainingOpponent>();
            Assert.IsNotNull(_bootstrap);
            Assert.IsNotNull(_hero);
            Assert.IsNotNull(_opponent);
            Assert.AreEqual(CaptureMatchState.Off, _bootstrap.CaptureState);
        }

        private static float Margin(float x, float y)
        {
            return Mathf.Min(Mathf.Min(x, Screen.width - x), Mathf.Min(y, Screen.height - y));
        }

        // 真實點擊：先量投影，離四邊 <16px 就紅，不得換點（條文 (d)）。
        private float Tap(Vector3 world, string who)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(world);
            float margin = Margin(screen.x, screen.y);
            Debug.Log("[RAGE-C07] tap " + who + " " + world.ToString("F3") + " → (" + screen.x.ToString("F1") + ", "
                      + screen.y.ToString("F1") + ") margin " + margin.ToString("F1") + " of " + Screen.width + "x" + Screen.height);
            Assert.AreEqual(640, Screen.width, "條文 (d) 以 640×480 量測");
            Assert.AreEqual(480, Screen.height, "條文 (d) 以 640×480 量測");
            Assert.Greater(screen.z, 0f, who + "：點擊點在鏡頭後方");
            Assert.GreaterOrEqual(margin, TapMarginPixels, who + "：點擊點投影 (" + screen.x + ", " + screen.y + ") 離畫面邊緣不到 16px");
            _bootstrap.WorldTapInput.SendScreenTap(screen.x, screen.y);
            return margin;
        }

        private string Owners()
        {
            StringBuilder sb = new StringBuilder(TileCount);
            for (int i = 0; i < TileCount; i++)
            {
                Faction f = _bootstrap.CaptureView.OwnerOf(i);
                sb.Append(f == Faction.BlueTeam ? 'B' : f == Faction.RedTeam ? 'R' : '.');
            }
            return sb.ToString();
        }

        // 重播一次劇本。shiftStep＝−1：原劇本；否則只把第 shiftStep 個點地點平移 shift（其餘點不動）。
        // 第 f 幀開始前送出所有「秒數 ≤ (f−1)·dt」的點擊；觸發（任一方狂怒觸發次數增加）就停。
        private IEnumerator Replay(float dt, int shiftStep, Vector3 shift, RunResult[] outResult)
        {
            yield return Setup(dt);
            Capture19RageScript.Step[] steps = Capture19RageScript.Steps;
            RunResult r = new RunResult { Side = -1, Frame = -1, MinTapMargin = float.MaxValue };
            StringBuilder trace = new StringBuilder();

            Assert.IsTrue(_bootstrap.TryGetCaptureButtonScreenPoint(out float bx, out float by), "拿不到 CAPTURE 鈕的螢幕點");
            r.MinTapMargin = Mathf.Min(r.MinTapMargin, Margin(bx, by));
            Assert.GreaterOrEqual(Margin(bx, by), TapMarginPixels, "CAPTURE 鈕離畫面邊緣不到 16px");
            _bootstrap.WorldTapInput.SendScreenTap(bx, by);
            Assert.AreEqual(CaptureMatchState.Lobby, _bootstrap.CaptureState, "點 CAPTURE 後應進入 Lobby");
            r.MinTapMargin = Mathf.Min(r.MinTapMargin, Tap(_opponent.transform.position + Vector3.up, "opening opponent"));
            Assert.AreEqual(CaptureMatchState.Active, _bootstrap.CaptureState, "開局點擊後應進入 Active");

            int windowFrames = Mathf.RoundToInt(WindowSeconds / dt);
            int next = 0;
            int blueRage0 = _bootstrap.CaptureBlueRageTriggerCount, redRage0 = _bootstrap.CaptureRedRageTriggerCount;
            int prevBlueN = _bootstrap.CaptureBlueNeutralizedCount, prevRedN = _bootstrap.CaptureRedNeutralizedCount;
            string lastOwners = Owners();
            for (int f = 1; f <= windowFrames; f++)
            {
                int tappedStep = -1;
                while (next < steps.Length && Mathf.RoundToInt(steps[next].Seconds / dt) <= f - 1)
                {
                    Vector3 p = steps[next].World;
                    if (next == shiftStep) p += shift;
                    Assert.IsTrue(_hero.IsAlive, "第 " + next + " 步（" + steps[next].Seconds + "s）點擊時英雄倒地");
                    r.MinTapMargin = Mathf.Min(r.MinTapMargin, Tap(p, "step" + next));
                    trace.Append(" tap").Append(next).Append('@').Append(f - 1);
                    tappedStep = next;
                    next++;
                }
                yield return null;
                if (tappedStep >= 0)
                {
                    // 點擊必須真的落在世界區並下達移動（被 HUD 區吃掉就紅；v090c07-explore.md §8）。
                    bool moving = _hero.StateMachine.CurrentState == PlayerState.Moving;
                    Assert.IsTrue(moving, "第 " + tappedStep + " 步點地後英雄沒有進入 Moving（點擊被 UI 區吃掉？）狀態="
                                                         + _hero.StateMachine.CurrentState);
                }

                string owners = Owners();
                if (owners != lastOwners) trace.Append(' ').Append(owners).Append('@').Append(f);
                lastOwners = owners;

                int blueN = _bootstrap.CaptureBlueNeutralizedCount, redN = _bootstrap.CaptureRedNeutralizedCount;
                int side = _bootstrap.CaptureBlueRageTriggerCount != blueRage0 ? 0
                         : _bootstrap.CaptureRedRageTriggerCount != redRage0 ? 1 : -1;
                if (side >= 0)
                {
                    r.Side = side;
                    r.Frame = f;
                    r.Seconds = f * dt;
                    r.NeutralizedBefore = side == 0 ? prevBlueN : prevRedN;
                    r.NeutralizedAt = side == 0 ? blueN : redN;
                    r.BlueScore = _bootstrap.CaptureView.BlueScore;
                    r.RedScore = _bootstrap.CaptureView.RedScore;
                    break;
                }
                prevBlueN = blueN;
                prevRedN = redN;
            }
            r.Trace = trace.ToString();
            Debug.Log("[RAGE-C07] RESULT dt=" + dt.ToString("F4") + " shiftStep=" + shiftStep + " shift=" + shift.ToString("F1")
                      + " side=" + r.Side + " f_r=" + r.Frame + " t_r=" + r.Seconds.ToString("F3")
                      + " neut " + r.NeutralizedBefore + "→" + r.NeutralizedAt + " score " + r.BlueScore + "/" + r.RedScore
                      + " minMargin=" + r.MinTapMargin.ToString("F1") + " trace:" + r.Trace);
            outResult[0] = r;
        }

        private static void AssertTriggeredByACut(RunResult r, string label)
        {
            Assert.GreaterOrEqual(r.Side, 0, label + "：52 秒內沒有觸發狂怒；軌跡：" + r.Trace);
            Assert.GreaterOrEqual(r.NeutralizedAt - r.NeutralizedBefore, 1,
                label + "：觸發那一幀該方中立化數沒有增加（狂怒不是來自斷能）；軌跡：" + r.Trace);
        }

        // ═════════ V9-C07 劇本資料輸出（D10 讀同一份）═════════
        [Test]
        public void V9_C07_WritesTheScriptJson_WithTheSameStepsAsTheCsData()
        {
            string json = Capture19RageScript.ToJson();
            string path = Path.Combine(ToolchainDir, "v090-rage-script.json");
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Assert.AreEqual(json, File.ReadAllText(path, Encoding.UTF8));
            Assert.AreEqual(16, Capture19RageScript.Steps.Length, "劇本點地點數 N");
        }

        // ═════════ V9-C07 (a) 1/60 連跑 5 次＋(b) 1/6 跑 1 次 ═════════
        [UnityTest]
        public IEnumerator V9_C07_AB_FiveRunsAtSixtyFps_AndOneAtSixFps_TriggerTheSameSidesRageByACut()
        {
            RunResult[] one = new RunResult[1];
            RunResult[] a = new RunResult[5];
            for (int i = 0; i < 5; i++)
            {
                yield return Replay(Sixtieth, -1, Vector3.zero, one);
                a[i] = one[0];
                AssertTriggeredByACut(a[i], "(a) 第 " + (i + 1) + " 次");
                Assert.AreEqual(a[0].Side, a[i].Side, "(a) 第 " + (i + 1) + " 次觸發方與第 1 次不同");
            }
            int minF = a[0].Frame, maxF = a[0].Frame;
            float[] secs = new float[5];
            StringBuilder scores = new StringBuilder();
            for (int i = 0; i < 5; i++)
            {
                if (a[i].Frame < minF) minF = a[i].Frame;
                if (a[i].Frame > maxF) maxF = a[i].Frame;
                secs[i] = a[i].Seconds;
                scores.Append(" run").Append(i + 1).Append(" f_r=").Append(a[i].Frame).Append(' ')
                      .Append(a[i].BlueScore).Append('/').Append(a[i].RedScore);
            }
            System.Array.Sort(secs);
            float median = secs[2];
            Debug.Log("[RAGE-C07] (a) side=" + a[0].Side + " f_r range " + minF + "～" + maxF + " median t_r=" + median.ToString("F3") + scores);
            Assert.LessOrEqual(maxF - minF, 6, "(a) f_r 最大值減最小值");

            yield return Replay(Sixth, -1, Vector3.zero, one);
            RunResult b = one[0];
            AssertTriggeredByACut(b, "(b) 1/6");
            Assert.AreEqual(a[0].Side, b.Side, "(b) 1/6 的觸發方與 (a) 不同");
            Debug.Log("[RAGE-C07] (b) t_r=" + b.Seconds.ToString("F3") + " vs (a) median " + median.ToString("F3")
                      + " diff " + Mathf.Abs(b.Seconds - median).ToString("F3") + " score " + b.BlueScore + "/" + b.RedScore);
            Assert.LessOrEqual(Mathf.Abs(b.Seconds - median), 1.0f, "(b) 1/6 觸發時刻與 (a) 中位數相差");

            // D10 用的 t_r（牆鐘估算起點）；與劇本分開存，劇本檔只放劇本。
            string result = "{\n  \"clause\": \"V9-C07\",\n  \"side\": \"" + (a[0].Side == 0 ? "blue" : "red") + "\",\n"
                            + "  \"a_median_t_r_seconds\": " + median.ToString("R", CultureInfo.InvariantCulture) + ",\n"
                            + "  \"a_f_r_min\": " + minF + ",\n  \"a_f_r_max\": " + maxF + ",\n"
                            + "  \"a_score_at_trigger\": \"" + a[0].BlueScore + "/" + a[0].RedScore + "\",\n"
                            + "  \"b_t_r_seconds\": " + b.Seconds.ToString("R", CultureInfo.InvariantCulture) + "\n}\n";
            File.WriteAllText(Path.Combine(ToolchainDir, "v090-rage-result.json"), result, new UTF8Encoding(false));
        }

        // ═════════ V9-C07 (c) 每個點地點單獨往東西南北平移 0.5m（4N 次，1/60，全部觸發）═════════
        private IEnumerator ShiftOneStepFourWays(int step)
        {
            Vector3[] dirs =
            {
                new Vector3(ShiftMeters, 0f, 0f), new Vector3(-ShiftMeters, 0f, 0f),
                new Vector3(0f, 0f, -ShiftMeters), new Vector3(0f, 0f, ShiftMeters)
            };
            string[] names = { "東", "西", "南", "北" };
            RunResult[] one = new RunResult[1];
            for (int d = 0; d < 4; d++)
            {
                yield return Replay(Sixtieth, step, dirs[d], one);
                AssertTriggeredByACut(one[0], "(c) 第 " + step + " 步往" + names[d] + " 0.5m");
            }
        }

        [UnityTest] public IEnumerator V9_C07_C00_ShiftTap0() { yield return ShiftOneStepFourWays(0); }
        [UnityTest] public IEnumerator V9_C07_C01_ShiftTap1() { yield return ShiftOneStepFourWays(1); }
        [UnityTest] public IEnumerator V9_C07_C02_ShiftTap2() { yield return ShiftOneStepFourWays(2); }
        [UnityTest] public IEnumerator V9_C07_C03_ShiftTap3() { yield return ShiftOneStepFourWays(3); }
        [UnityTest] public IEnumerator V9_C07_C04_ShiftTap4() { yield return ShiftOneStepFourWays(4); }
        [UnityTest] public IEnumerator V9_C07_C05_ShiftTap5() { yield return ShiftOneStepFourWays(5); }
        [UnityTest] public IEnumerator V9_C07_C06_ShiftTap6() { yield return ShiftOneStepFourWays(6); }
        [UnityTest] public IEnumerator V9_C07_C07_ShiftTap7() { yield return ShiftOneStepFourWays(7); }
        [UnityTest] public IEnumerator V9_C07_C08_ShiftTap8() { yield return ShiftOneStepFourWays(8); }
        [UnityTest] public IEnumerator V9_C07_C09_ShiftTap9() { yield return ShiftOneStepFourWays(9); }
        [UnityTest] public IEnumerator V9_C07_C10_ShiftTap10() { yield return ShiftOneStepFourWays(10); }
        [UnityTest] public IEnumerator V9_C07_C11_ShiftTap11() { yield return ShiftOneStepFourWays(11); }
        [UnityTest] public IEnumerator V9_C07_C12_ShiftTap12() { yield return ShiftOneStepFourWays(12); }
        [UnityTest] public IEnumerator V9_C07_C13_ShiftTap13() { yield return ShiftOneStepFourWays(13); }
        [UnityTest] public IEnumerator V9_C07_C14_ShiftTap14() { yield return ShiftOneStepFourWays(14); }
        [UnityTest] public IEnumerator V9_C07_C15_ShiftTap15() { yield return ShiftOneStepFourWays(15); }
    }
}
