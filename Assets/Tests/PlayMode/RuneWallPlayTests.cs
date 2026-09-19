using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vow.Bootstrap;
using Vow.Combat;
using Vow.Combat.Feedback;
using Vow.Core;
using Vow.Core.Logic;

namespace Vow.Tests.PlayMode
{
    // Phase 2 批 1 步驟 B：符印石牆的 Unity 端行為，對應計畫書 §5 V4 a~f。
    // V4 g（零配置量測窗口內的施放與到期）在 ZeroAllocationTests.cs。
    //
    // 每個測試都用一顆全新的 ScriptedInput 重新 Initialize 場景既有的 RuneCaster／RuneGhostPreview，
    // 繞過真實觸控辨識（TouchGestureRouter 另有 EditMode 測試），只驗這一層「收到符印事件之後做了什麼」。
    public sealed class RuneWallPlayTests
    {
        // 英雄是被球體掃描擋下的：身體表面碰到牆面時，中心還在牆面前一個身體半徑。阻擋斷言比的是中心座標，
        // 所以門檻要扣掉身體半徑；只留 5cm 給 SkinWidth 與浮點誤差。半徑讀場景裡的實際值（建置器把
        // HeroTuningAsset.BodyRadius 同時餵給膠囊與 NavMeshAgent），寫死的話日後調大半徑會讓這條斷言無聲變鬆。
        private float HeroBodyRadius => _hero.GetComponent<NavMeshAgent>().radius;
        private const float BodySkinTolerance = 0.05f;

        private const string SceneName = "VOW_Phase1_Greybox";

        private HeroController _hero;
        private ScriptedInput _input;
        private RuneCaster _caster;
        private RuneGhostPreview _ghost;
        private RuneWall[] _pool;

        private IEnumerator Setup(RuneTuning tuning)
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null; // Awake／OnEnable／Start（含 Phase1Bootstrap 的接線）全部跑完

            _hero = Object.FindObjectOfType<HeroController>();
            Assert.IsNotNull(_hero, "場景裡找不到 HeroController");

            NavMeshAgent agent = _hero.GetComponent<NavMeshAgent>();
            Assert.IsTrue(agent.Warp(Vector3.zero), "無法把英雄放回原點");
            _hero.transform.rotation = Quaternion.identity; // forward = (0,0,1)，讓每個測試的方位可預期

            _input = new ScriptedInput();
            CombatFeedbackService feedback = Object.FindObjectOfType<CombatFeedbackService>();
            _hero.Initialize(_input, feedback, Camera.main);

            _caster = Object.FindObjectOfType<RuneCaster>();
            Assert.IsNotNull(_caster, "場景缺少 RuneCaster");
            _pool = Object.FindObjectsOfType<RuneWall>();
            Assert.GreaterOrEqual(_pool.Length, 3, "石牆池應預建 3 面（上限 2 面＋1 面坍塌緩衝）");
            _caster.Initialize(_input, _input, _hero.transform, Camera.main, tuning, _pool, _hero.HeroFaction);

            _ghost = Object.FindObjectOfType<RuneGhostPreview>();
            Assert.IsNotNull(_ghost, "場景缺少 RuneGhostPreview");
            _ghost.Initialize(_input, _input, _hero.transform, Camera.main, tuning);

            yield return null;
        }

        private static int AliveCount(RuneWall[] pool)
        {
            int count = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) count++;
            return count;
        }

        private static RuneWall FirstAlive(RuneWall[] pool)
        {
            for (int i = 0; i < pool.Length; i++) if (pool[i].IsAlive) return pool[i];
            return null;
        }

        // V4 a：極速施放後 0.2s 內，場上恰有 1 面存活石牆，中心在施放當下位置前方 4m（±0.1m）、
        // 牆面法線與英雄朝向夾角 <1°、BoxCollider.enabled。
        [UnityTest]
        public IEnumerator QuickCast_SpawnsOneWall_FourMetresAhead_FacingTheHero()
        {
            yield return Setup(new RuneTuning());

            Vector3 heroPos = _hero.transform.position;
            Vector3 heroForward = _hero.transform.forward;

            _input.RuneQuickCast();

            float deadline = Time.time + 0.2f;
            RuneWall wall = null;
            while (Time.time <= deadline)
            {
                wall = FirstAlive(_pool);
                if (wall != null) break;
                yield return null;
            }

            Assert.IsNotNull(wall, "0.2s 內沒有任何存活石牆");
            Assert.AreEqual(1, AliveCount(_pool), "應恰好 1 面存活");

            Vector3 expectedCenter = heroPos + heroForward * 4f;
            Vector3 offset = wall.transform.position - expectedCenter;
            offset.y = 0f;
            Assert.LessOrEqual(offset.magnitude, 0.1f, "石牆中心偏離預期落點：" + wall.transform.position);

            float angle = Vector3.Angle(wall.transform.forward, heroForward);
            Assert.Less(angle, 1f, "牆面法線與英雄朝向夾角過大：" + angle);

            BoxCollider collider = wall.GetComponent<BoxCollider>();
            Assert.IsTrue(collider.enabled, "石牆的 Collider 應已開啟");
        }

        // V4 b 正向對照：沒有石牆時，沿同一條路線 3 秒內應越過 z=4 平面。
        [UnityTest]
        public IEnumerator NoWall_HeroCrossesThePlane_WithinThreeSeconds()
        {
            yield return Setup(new RuneTuning());

            _input.TapGround(new Vector3(0f, 0f, 20f));

            float deadline = Time.time + 3f;
            bool crossed = false;
            while (Time.time <= deadline)
            {
                if (_hero.transform.position.z > 4f) { crossed = true; break; }
                yield return null;
            }

            Assert.IsTrue(crossed, "正向對照失敗：沒有石牆時 3 秒內應越過 z=4 平面，實際位置 " + _hero.transform.position);
        }

        // V4 b：同一條路線，極速施放的石牆立在 z=4（法線朝 +Z）之後，3 秒內不得越過牆面。
        // r1 對抗審查 M2：門檻改成「牆中心 z − 厚度/2」（牆面，嚴格小於）而非寫死 <=4（那樣即使牆做得更薄／位置算錯也測不出來）；
        // 另外要求英雄確實起步，不然「一直沒動」也會通過阻擋斷言，零鑑別力。
        [UnityTest]
        public IEnumerator Wall_BlocksTheHero_AlongTheSamePath()
        {
            RuneTuning tuning = new RuneTuning();
            yield return Setup(tuning);

            _input.RuneQuickCast();
            yield return null;
            RuneWall wall = FirstAlive(_pool);
            Assert.IsNotNull(wall, "石牆未成形，阻擋測試沒有意義");
            // §6 R2：這條守的是「牆的碰撞體擋得住身體」，不是「英雄不會繞路」——批 2 之後英雄本來就該繞過去。
            _hero.GetComponent<HeroLocomotion>().SetNavigator(null, 0f);
            float wallFaceZ = wall.transform.position.z - tuning.WallThickness * 0.5f - HeroBodyRadius + BodySkinTolerance;

            _input.TapGround(new Vector3(0f, 0f, 20f));

            float deadline = Time.time + 3f;
            while (Time.time <= deadline)
            {
                Assert.Less(_hero.transform.position.z, wallFaceZ, "英雄越過了石牆牆面（z=" + wallFaceZ + "）：" + _hero.transform.position);
                yield return null;
            }

            Assert.Greater(_hero.transform.position.z, 1f,
                "英雄應該確實起步、被牆擋在半路，而不是原地不動也算通過：" + _hero.transform.position);
        }

        // V4 c：施放後 5.0s（+0.3s 容差）石牆不再存活、Collider 已關。
        [UnityTest]
        public IEnumerator Wall_ExpiresAfterItsLifespan()
        {
            yield return Setup(new RuneTuning());

            _input.RuneQuickCast();
            yield return null;

            RuneWall wall = FirstAlive(_pool);
            Assert.IsNotNull(wall, "石牆未成形");
            BoxCollider collider = wall.GetComponent<BoxCollider>();

            yield return new WaitForSeconds(5.3f);

            Assert.IsFalse(wall.IsAlive, "5.3s 後石牆應已消失");
            Assert.IsFalse(collider.enabled, "石牆的 Collider 應已關閉");
        }

        // V4 d：連放三面，第三面成形後第一面不再存活，存活數＝2。
        // 冷卻歸零只作用在這個測試自己的 RuneTuning 實例上，不動 RuneTuning.cs 的正式預設值（8s）。
        [UnityTest]
        public IEnumerator ThirdWall_EvictsTheOldestWhenTeamCapIsExceeded()
        {
            RuneTuning tuning = new RuneTuning { CooldownSeconds = 0f };
            yield return Setup(tuning);

            _input.RuneQuickCast();
            yield return null;
            RuneWall first = FirstAlive(_pool);
            Assert.IsNotNull(first, "第一面石牆未成形");

            _input.RuneQuickCast();
            yield return null;
            _input.RuneQuickCast();
            yield return null;

            Assert.IsFalse(first.IsAlive, "第三面成形後，最早的那面應已坍塌");
            Assert.AreEqual(2, AliveCount(_pool), "存活數應為 2");
        }

        // V4 f：冷卻中再次極速施放，存活石牆數不變。
        [UnityTest]
        public IEnumerator QuickCastDuringCooldown_DoesNotSpawnAnotherWall()
        {
            yield return Setup(new RuneTuning()); // 預設冷卻 8s

            _input.RuneQuickCast();
            yield return null;
            Assert.AreEqual(1, AliveCount(_pool), "第一次施放應成牆");

            _input.RuneQuickCast(); // 冷卻中：應被忽略
            yield return null;

            Assert.AreEqual(1, AliveCount(_pool), "冷卻中再次施放不應增加存活石牆數");
        }

        // V4 e：拖曳更新→虛影可見且位置＝預期落點；取消→虛影隱藏、無石牆、冷卻未開始；
        // 鬆手→石牆成形於該落點、虛影隱藏。
        // r1 對抗審查 H1：期望落點的 y 由測試自己算（地面 y ＋ tuning.WallHeight/2，與 RuneCaster.SpawnWall／
        // RuneGhostPreview 同一個公式），斷言時不再把 y 軸抹掉——虛影或實牆的 y 錯了（例如半截埋進地板）現在會被抓到。
        [UnityTest]
        public IEnumerator Drag_ShowsGhost_CancelHidesWithoutWall_ReleaseSpawnsWallAndHidesGhost()
        {
            RuneTuning tuning = new RuneTuning();
            yield return Setup(tuning);

            Renderer ghostRenderer = _ghost.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(ghostRenderer, "虛影缺少 Renderer");
            Assert.IsFalse(ghostRenderer.enabled, "初始狀態虛影應隱藏");

            Vector2 screenDir = new Vector2(0f, 1f); // 螢幕正上方 → 攝影機水平前方（見 RuneCaster.ScreenToWorldGroundDirection）
            const float distance01 = 0.5f;

            _input.RuneDrag(screenDir, distance01);
            yield return null;

            Assert.IsTrue(ghostRenderer.enabled, "拖曳中虛影應可見");

            Vector3 heroPos = _hero.transform.position;
            Vector3 worldDir = RuneCaster.ScreenToWorldGroundDirection(screenDir, Camera.main.transform);
            RuneCastLogic placementCalc = new RuneCastLogic(tuning);
            Assert.IsTrue(placementCalc.TryDragPlacement(heroPos.x, heroPos.z, worldDir.x, worldDir.z, distance01, out RuneWallPlacement expected));
            Vector3 expectedPos = new Vector3(expected.CenterX, heroPos.y + tuning.WallHeight * 0.5f, expected.CenterZ);
            Vector3 ghostOffset = _ghost.transform.position - expectedPos;
            Assert.LessOrEqual(ghostOffset.magnitude, 0.05f,
                "虛影位置與預期落點不符（含 y 軸）：實際 " + _ghost.transform.position + " 預期 " + expectedPos);

            // 取消：不必真的滑回原點（那是 RuneGestureTracker 的職責，EditMode 已測），這裡只驗 RuneCaster／
            // RuneGhostPreview 對 Cancelled 事件的反應——無石牆、虛影隱藏、冷卻未開始。
            _input.RuneCancel();
            yield return null;
            Assert.IsFalse(ghostRenderer.enabled, "取消後虛影應隱藏");
            Assert.AreEqual(0, AliveCount(_pool), "取消不應產生石牆");
            Assert.AreEqual(0f, _caster.CooldownRemaining, 0.001f, "取消不應進入冷卻");

            // 鬆手成牆
            _input.RuneDrag(screenDir, distance01);
            yield return null;
            _input.RuneRelease(screenDir, distance01);
            yield return null;

            Assert.IsFalse(ghostRenderer.enabled, "鬆手後虛影應隱藏");
            RuneWall spawned = FirstAlive(_pool);
            Assert.IsNotNull(spawned, "鬆手應該成牆");
            Vector3 spawnedOffset = spawned.transform.position - expectedPos;
            Assert.LessOrEqual(spawnedOffset.magnitude, 0.1f,
                "成牆位置與拖曳預期落點不符（含 y 軸）：實際 " + spawned.transform.position + " 預期 " + expectedPos);
        }

        // 批 3 V4-e／V6：量測位置從「牆在不在 Ignore Raycast 層」這個實作手段，搬到使用者真的會經歷的路徑。
        // 要守的行為逐字不變（點自家牆等於點到牆後的地板、牆照樣擋身體），但斷言**更嚴**：
        // 舊版只要牆在層 2 就過，新版要求陣營判斷正確（牆已經回到 Default 層），
        // 反向由 ShieldAndProjectilePlayTests.V4f（敵方／中立牆必須點得到）補上。
        [UnityTest]
        public IEnumerator Wall_IsInvisibleToWorldTapRaycast_ButStillBlocksTheHeroPhysically()
        {
            RuneTuning tuning = new RuneTuning();
            yield return Setup(tuning);

            _input.RuneQuickCast();
            yield return null;
            RuneWall wall = FirstAlive(_pool);
            Assert.IsNotNull(wall, "石牆未成形");

            Phase1Bootstrap bootstrap = Object.FindObjectOfType<Phase1Bootstrap>();
            Assert.IsNotNull(bootstrap, "場景缺少 Phase1Bootstrap");

            ICombatTarget selectedTarget = null;
            bool moveSelected = false;
            Vector3 moveDestination = Vector3.zero;
            bootstrap.InputService.OnCombatTargetSelected += target => selectedTarget = target;
            bootstrap.InputService.OnMoveDestinationSelected += point => { moveSelected = true; moveDestination = point; };

            // 對「自家石牆本體」的螢幕座標下點擊：射線一定先打到牆，陣營校驗要讓它穿過去打到牆後的地板。
            Vector3 screenPoint = Camera.main.WorldToScreenPoint(wall.transform.position);
            bootstrap.WorldTapInput.OnWorldTap(screenPoint.x, screenPoint.y);

            Assert.IsNull(selectedTarget, "點自家石牆不得鎖定它（實際鎖到 " + selectedTarget + "）");
            Assert.IsTrue(moveSelected, "點自家石牆應該變成「走到牆後地板」的移動指令");
            Assert.Greater(moveDestination.z, wall.transform.position.z + tuning.WallThickness * 0.5f,
                "落點必須落在牆的後方地板上：" + moveDestination);

            float wallFaceZ = wall.transform.position.z - tuning.WallThickness * 0.5f - HeroBodyRadius + BodySkinTolerance;
            // §6 R2：同上——這條守的是射線穿得過去＋碰撞體擋得住身體，繞牆與否不在它的射程內。
            _hero.GetComponent<HeroLocomotion>().SetNavigator(null, 0f);
            _input.TapGround(new Vector3(0f, 0f, 20f));

            float deadline = Time.time + 3f;
            while (Time.time <= deadline)
            {
                Assert.Less(_hero.transform.position.z, wallFaceZ, "英雄越過了石牆牆面：" + _hero.transform.position);
                yield return null;
            }
        }

        // r1 對抗審查 H5：RuneCaster 重新 Initialize（例如場景重載、測試換場）時，池裡若有還活著的牆，
        // 換 _logic 之前要先把它收掉；否則舊的存活狀態失去追蹤，變成一面 Collider 開著、永遠不會到期的牆。
        [UnityTest]
        public IEnumerator ReInitializingTheCaster_CollapsesAnyAliveWall_InsteadOfLeakingIt()
        {
            yield return Setup(new RuneTuning());

            _input.RuneQuickCast();
            yield return null;
            RuneWall wall = FirstAlive(_pool);
            Assert.IsNotNull(wall, "石牆未成形");
            Assert.IsTrue(wall.IsAlive);
            BoxCollider collider = wall.GetComponent<BoxCollider>();
            Assert.IsTrue(collider.enabled);

            _caster.Initialize(_input, _input, _hero.transform, Camera.main, new RuneTuning(), _pool, _hero.HeroFaction);

            Assert.IsFalse(wall.IsAlive, "重新 Initialize 後，原本活著的牆應該被收掉，不得留下永久牆");
            Assert.IsFalse(collider.enabled, "石牆的 Collider 應已關閉");
        }

        // 第三輪：使用者試玩後裁定移除取消區，只留「滑回按下的位置放手＝取消」，畫面上不再有紅色取消柱，
        // 改用虛影變色提示。這裡不依賴真實的 RuneGestureTracker 滑回原點判定（那是 EditMode 的職責），
        // 改用測試自己的 armed 旗標直接驗證 RuneGhostPreview 對 isCancelArmed 查詢來源的反應。
        [UnityTest]
        public IEnumerator Ghost_TurnsCancelColour_WhileReleaseWouldCancel()
        {
            RuneTuning tuning = new RuneTuning();
            yield return Setup(tuning);

            bool armed = false;
            _ghost.Initialize(_input, _input, _hero.transform, Camera.main, tuning, () => armed);

            Renderer ghostRenderer = _ghost.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(ghostRenderer, "虛影缺少 Renderer");

            Vector2 screenDir = new Vector2(0f, 1f);
            const float distance01 = 0.5f;

            _input.RuneDrag(screenDir, distance01);
            yield return null;
            yield return null; // LateUpdate 跑過一輪，顏色狀態才會真的落地

            Assert.IsTrue(ghostRenderer.enabled, "拖曳中虛影應可見");
            Assert.IsFalse(_ghost.ShowingCancelColor, "旗標為假時不應顯示取消色");
            Color normalColor = ReadAppliedBaseColor(ghostRenderer);
            Assert.AreEqual(normalColor, _ghost.CurrentColor, "元件回報的 CurrentColor 應與 Renderer 實際套用的顏色一致");

            armed = true;
            yield return null;
            yield return null;

            Assert.IsTrue(_ghost.ShowingCancelColor, "旗標為真時應顯示取消色");
            Color cancelColor = ReadAppliedBaseColor(ghostRenderer);
            Assert.AreEqual(cancelColor, _ghost.CurrentColor, "元件回報的 CurrentColor 應與 Renderer 實際套用的顏色一致");
            Assert.Greater(cancelColor.r - normalColor.r, 0.2f,
                "取消色的紅色分量應明顯高於正常色：" + cancelColor + " vs " + normalColor);
            Assert.Less(cancelColor.b, normalColor.b, "取消色不應比正常色更藍：" + cancelColor + " vs " + normalColor);

            armed = false;
            yield return null;
            yield return null;

            Assert.IsFalse(_ghost.ShowingCancelColor, "旗標翻回假後應回到正常色");
            Color revertedColor = ReadAppliedBaseColor(ghostRenderer);
            Assert.AreEqual(normalColor, revertedColor, "旗標翻回假後 Renderer 實際顏色應回到最初的正常色");

            _input.RuneCancel();
            yield return null;

            Assert.IsFalse(ghostRenderer.enabled, "取消後虛影應隱藏");
            Assert.IsFalse(_ghost.ShowingCancelColor, "隱藏時狀態應重設回正常色");
            Assert.AreEqual(0, AliveCount(_pool), "取消不應產生石牆");
            Assert.AreEqual(0f, _caster.CooldownRemaining, 0.001f, "取消不應進入冷卻");
        }

        // 不信元件自己回報的 CurrentColor／ShowingCancelColor，直接讀 Renderer 實際套用的 MaterialPropertyBlock。
        private static Color ReadAppliedBaseColor(Renderer renderer)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor(Shader.PropertyToID("_BaseColor"));
        }
    }
}
