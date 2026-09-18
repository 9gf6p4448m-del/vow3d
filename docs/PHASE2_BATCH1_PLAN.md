# 計畫：Phase 2 批 1 —— 符印按鈕＋符印石牆（2026-09-19）

> 已知良好狀態：`31cddf6`（v0.2.1，線上版）。任何一步做壞就退回這裡。
> 本檔 §5 即驗收條件的凍結落點（commit 後不得為了過關而改；要改照 harness `02 §2.1`）。

## 0. 使用者裁定（2026-09-19，需求對齊一輪）

1. `ARCHITECTURE.md:41` 的「10 人實機盲測通過才解鎖 Phase 2」：**延後盲測、先開工**；盲測於 Phase 2 期間平行補做。不改 ARCHITECTURE 原文。
2. Phase 2 分四批、依賴順序：批 1＝輸入路由＋符印按鈕＋`IRuneWall`；批 2＝0.5m 格點＋向量場繞牆；批 3＝破敵牆得護盾＋友軍穿透（加友軍測試砲台）；批 4＝元素 Combo（開工前另做一輪對齊）。每批結尾推一版網頁試玩版。
3. 極速石牆＝**輕點一下就放**（雙擊的第二下落在冷卻中被忽略）。
4. 規格留白數值採暫定值、做成可調、試玩即改：符印冷卻 8s、石牆寬 4m×厚 0.6m×高 2m、血量 300、拖曳施法距離 2～8m。

## 1. 檔案清單與批次

### 步驟 A：純邏輯＋輸入路由（不需 Unity，`verify.sh` 可驗）
- 新增 `Assets/Scripts/Core/Logic/RuneTuning.cs` —— 批 1 全部數值的單一來源
- 新增 `Assets/Scripts/Core/Logic/RuneGestureTracker.cs` —— 符印單指手勢（輕點／拖曳／取消／鬆手），值型別零配置
- 新增 `Assets/Scripts/Core/Logic/RuneWallLogic.cs` —— 單面牆的壽命、血量、穿透計數（純數學）
- 新增 `Assets/Scripts/Core/Logic/RuneCastLogic.cs` —— 冷卻、落點換算、全隊上限 2 面的名冊
- 新增 `Assets/Scripts/Core/Contracts/IRuneCastInput.cs` —— 補「鬆手成牆」事件（見 §4 假設 1）
- 修改 `Assets/Scripts/Input/InputRoutingManager.cs` —— 新路由 `TouchRoute.Rune`＋符印區／取消區
- 修改 `Assets/Scripts/Input/TouchGestureRouter.cs` —— Rune 路由的相位處理；`ITouchGestureSink` 加四個符印回呼
- 新增 `Assets/Tests/EditMode/RuneGestureRoutingTests.cs`、`RuneWallLogicTests.cs`、`RuneCastLogicTests.cs`
- 修改 `Assets/Tests/EditMode/TouchGestureRouterTests.cs` —— 只補 `RecordingSink` 的新介面成員（既有案例一行不動）
- 修改 `Tools/DotnetCheck/mutation_check.py` —— 追加符印突變（只增不改既有 18 筆）
- 修改 `Tools/DotnetCheck/PureLogic.Tests/PureLogic.Tests.csproj` —— 僅在需要納入新 Contracts 檔時加一行
- **Checkpoint A**：`verify.sh` 全綠、突變全抓到；Unity 端尚未接線，遊戲行為與 v0.2.1 完全相同。

### 步驟 B：Unity 端接線
- 修改 `Assets/Scripts/Input/PlayerInputService.cs` —— 真的發出符印事件；登記符印區／取消區
- 修改 `Assets/Scripts/Input/NetworkLatencySimulator.cs` —— 「極速施放／鬆手成牆」走延遲佇列；拖曳更新與取消是本機 UI 回饋，不延遲
- 新增 `Assets/Scripts/Combat/RuneWall.cs` —— `IRuneWall` 實作（繼承 `CombatTargetBehaviour`；只開 `BoxCollider`，紅線 5）
- 新增 `Assets/Scripts/Combat/RuneCaster.cs` —— 訂閱符印事件 → `RuneCastLogic` → 從預建池取牆
- 新增 `Assets/Scripts/Bootstrap/RuneGhostPreview.cs` —— 拖曳中的半透明石牆虛影（先例：`CadenceAimPreview`）
- 新增 `Assets/Scripts/UI/RuneButtonView.cs` —— 右下角符印按鈕、冷卻遮罩、拖曳時的取消區（IMGUI，先例：`DebugHud`；全專案只有一條輸入路徑，不引入 EventSystem）
- 修改 `Assets/Scripts/Editor/VOWPhase1SceneBuilder.cs` —— 預建 3 面石牆池＋虛影＋按鈕＋接線（執行期不得 `CreatePrimitive`）
- 修改 `Assets/Scripts/Bootstrap/Phase1Bootstrap.cs` —— 接線
- 修改 `Assets/Tests/PlayMode/ScriptedInput.cs`；新增 `Assets/Tests/PlayMode/RuneWallPlayTests.cs`
- 修改 `Assets/Tests/PlayMode/ZeroAllocationTests.cs` —— 量測窗口內加入施放石牆（只加行使，不動門檻）
- **Checkpoint B**：Unity batchmode EditMode＋PlayMode 全綠；本機場景可放牆。

### 步驟 C：上線
- `Assets/Scripts/Core/VowVersion.cs` → `0.3.0`；`docs/PHASE1_ACCEPTANCE_GUIDE.md` 補 Phase 2 批 1 段落與 §8 新解讀
- fresh-context 對抗審查（opus）→ 修 → commit → push main → `Tools/deploy-webgl.bat` → 真實瀏覽器驗線上版

## 2. 介面（先寫死再實作）

```csharp
// Core/Contracts/IRuneCastInput.cs —— ARCHITECTURE 的 IPlayerInputService 逐字保留、不加成員
public interface IRuneCastInput
{
    // 拖曳後在取消區之外鬆手：螢幕方向（單位向量）、拉伸量 0~1
    event Action<Vector2, float> OnRuneCastReleased;
}
// IPlayerInputService.OnRuneVectorDragUpdated(Vector2 screenDir, float distance01)：拖曳中每次取樣發一次

// Input/TouchGestureRouter.cs
public interface ITouchGestureSink
{
    // …既有三個不變…
    void OnRuneDragUpdated(float screenDirX, float screenDirY, float distance01);
    void OnRuneQuickCast();
    void OnRuneReleased(float screenDirX, float screenDirY, float distance01);
    void OnRuneCancelled();
}

// Input/InputRoutingManager.cs
public enum TouchRoute { Rejected, UiRegion, Rune, Pip, World }   // 判定順序：邊緣死區 → UI → Rune → Pip → World
void SetRuneZone(ScreenRegion zone);        // 符印按鈕
void SetRuneCancelZone(ScreenRegion zone);  // 按鈕上方的取消區（只在拖曳中有意義，不參與 Route()）
bool IsInRuneCancelZone(float x, float y);

// Core/Logic/RuneGestureTracker.cs（struct）
public enum RuneGestureOutcome { None, DragUpdated, QuickCast, Released, Cancelled }
void Begin(int touchId, float x, float y);
RuneGestureOutcome Move(float x, float y, float dragThresholdPx, float saturationPx);
RuneGestureOutcome End(float x, float y, float dragThresholdPx, float saturationPx, bool inCancelZone);
RuneGestureOutcome Cancel();   // 曾經開始拖曳 → Cancelled；否則 None
// 讀值：Held, Dragging, DirX, DirY（連續角度、不做八向吸附）, Distance01

// Core/Logic/RuneWallLogic.cs（class，零 UnityEngine）
void Activate(float maxHealth, float lifespan);   bool IsAlive;   float Health, RemainingLifespan;
void Tick(float dt);                  // 壽命歸零 → 死亡
void ApplyDamage(float amount);
bool TryPenetrate(out float damageMultiplier);   // 1~5 發 ×1.0、6~10 發 ×0.85；每發 −10% 最大生命、−0.5s；第 11 發拒絕
int PenetrationCount;   const MaxPenetrations = 10;

// Core/Logic/RuneCastLogic.cs
bool IsReady(double now);  double CooldownRemaining(double now);
bool TryBeginCast(double now);                      // 成功才進冷卻；取消不呼叫它
static void QuickCastPlacement(px, pz, fwdX, fwdZ, out cx, out cz, out normalX, out normalZ);   // 前方 4m，牆面法線＝朝向
static void DragPlacement(px, pz, dirX, dirZ, distance01, out cx, out cz, out normalX, out normalZ); // 2m + 6m×distance01
// 名冊：RuneWallRoster(capacity 2).Add(slot) → 回傳要坍塌的最舊 slot 或 -1；Remove(slot)
```

## 3. 不做什麼

- 批 2 的格點阻擋網格／向量場繞牆（英雄點牆後地板仍會頂著牆走，已知限制照舊）
- 批 3 的破牆護盾、友軍彈道、敵方／中立石牆的生成；`TryPenetrateBullet` 本批只有純邏輯與測試，場上沒有子彈會呼叫它
- 批 4 的元素反應
- 不動 `HeroCombatBrain`／`PlayerStateMachine`／`CadenceSim` 任何一行（Phase 1 手感核心）
- 不引入 uGUI Canvas／EventSystem；不做石牆正式美術、粒子、著色器特效（冷庫協議）
- 不動既有測試的斷言、既有 18 筆突變、`verify.sh` 的 8 條紅線 grep

## 4. 假設與取捨（R7 沒問到、自行採用；全部可推翻，會同步寫進驗收指南 §8）

1. **規格缺「鬆手成牆」事件**：`IPlayerInputService` 只有拖曳更新／極速施放／取消三個符印事件。另立 `IRuneCastInput` 補上，不改 ARCHITECTURE 逐字抄錄的契約。
2. **瞄準不進狀態機**：`PlayerState.CastingRune` 本批不使用。理由：現行轉移表只允許從 Idle／Moving 進入且該狀態下移動／攻擊指令會被吃掉，Tick 也沒有離開機制；極速石牆是「被突進時的緊急防禦」，必須在前搖、收招、滑步中都放得出來，否則就是 GDD 要消滅的「吃指令」。石牆於事件到達當下立即成形，英雄照常走 A。
3. **拖曳原點＝手指按下的位置**（浮動原點，與微輪盤一致，利於盲操）；「滑回按鈕中心」＝手指回到距原點 3.5mm 內；另有按鈕上方的矩形取消區。在兩者任一之內鬆手＝取消。
4. **沒拖出門檻就鬆手＝極速石牆，不論按了多久**（長按不動再放開也會放牆）。
5. **拇指向量的兩個自由度**：角度＝石牆相對英雄的方位（經相機轉成世界方向），拉伸量＝距離（3.5mm→2m，線性到 14mm→8m）；石牆牆面永遠垂直於「英雄→落點」連線。14mm 為暫定飽和行程【試玩必調】。
6. **極速石牆的「正前方」＝英雄當下 `transform.forward`**，牆面垂直於朝向。
7. **取消不消耗冷卻**；冷卻中輕點或鬆手一律無效（按鈕顯示冷卻遮罩）。
8. **石牆 `TargetFaction = DestructibleWall`、任何陣營可打**（與測試石牆一致）；擁有者陣營另存於 `RuneWall.OwnerFaction`，批 3 陣營校驗時再決定要不要進契約。
9. **石牆池＝場景預建 3 面**（上限 2 面＋1 面給坍塌中的舊牆），平時保持 GameObject 啟用、只關 Collider／Renderer——`Phase1Bootstrap` 的 `FindObjectsOfType` 才掃得到並註冊。
10. **落點不做場地邊界裁切**，除非實作時發現既有的邊界機制可直接重用。
11. **延遲注入**：極速施放與鬆手成牆走延遲佇列（忠於「沒有客戶端預測的最壞情況」）；虛影與取消是本機回饋，不延遲。
12. 友軍穿透的數值解讀（三處規格文字合併）：第 1～5 發 ×1.0、第 6～10 發固定 ×0.85（不累乘），每發 −10% 最大生命與 −0.5s；`GDD.md:243` 的「0.05ms 格點」視為「0.5m」筆誤（批 2 才用到）。

Simplicity 例外：

| 違反了什麼 | 為何必要 | 更簡單的方案為何被否決 |
|---|---|---|
| 本批就寫穿透數學（批 3 才有子彈） | `IRuneWall` 是契約、`TryPenetrateBullet` 必須實作才能編譯 | 回 `false` 的空殼會留下一個「看起來實作了」的介面，批 3 容易漏掉；純數學 15 行＋測試，成本低於風險 |
| 預建石牆池而非 `Instantiate` | 紅線 4（戰鬥中每幀零配置）＋WebGL 執行期不得 `CreatePrimitive` | 執行期生成會配置記憶體且 IL2CPP 剔除後 primitive 建不出來（v0.1.2 踩過） |

## 5. 端到端驗證步驟（凍結）

每條後面括號＝「什麼樣的實作會讓這條變紅」。

**V1** `bash Tools/DotnetCheck/verify.sh` 結尾為全數通過：純邏輯測試 0 失敗（既有 70 個一個不少）、compile-only 0 error、8 條紅線 grep 全過。（任何既有手感測試被弄壞、執行期用了 Linq、石牆用了 NavMeshObstacle／carving）

**V2** 新增的 EditMode 測試至少涵蓋下列行為，且全綠：
- a. 符印區內按下→放開（位移 < 門檻）：恰好 1 次 QuickCast；WorldTap、Flick、Released、Cancelled 皆 0。（輕點漏發；或事件滲透給世界層）
- b. 符印區按下→拖出門檻→繼續拖到世界區域上空→鬆手：DragUpdated ≥1、Released 恰好 1 次且方向與最後位移一致；**WorldTap＝0、Flick＝0**（GDD「阻斷符印事件向下滲透」）。（拖出按鈕後被當成點地或微彈）
- c. 拖出後滑回原點 3.5mm 內鬆手：Cancelled 1、Released 0、QuickCast 0。（取消區失效→誤放牆）
- d. 拖出後在上方取消區內鬆手：同 c。
- e. 拖曳中收到 `Canceled` 相位、或手指無 Ended 就消失（EndFrame 回收）：Cancelled 1、Released 0。
- f. 符印被按住時，第二根手指按符印區：不產生任何符印事件；同時另一根手指點世界：WorldTap 照常 1 次。（符印鎖死其他輸入＝「死鎖」）
- g. 拖曳中切換操作模式：Cancelled 1，之後該手指鬆手不再產生事件。
- h. distance01：位移＝門檻時 0、＝飽和行程時 1、超過仍為 1、其間單調遞增。
- i. 石牆壽命：Activate(300, 5.0) 後 Tick 累計 4.99s 仍存活、5.0s 死亡。
- j. 穿透：第 1～5 發倍率 1.0、第 6～10 發 0.85、每發生命 −30 與壽命 −0.5s、第 10 發後牆死亡、第 11 發回 false。
- k. 名冊上限：放第 3 面時回傳最早那面的 slot；已自然消失的牆不佔名額。
- l. 冷卻：施放後 8s 內 `TryBeginCast` 回 false、滿 8s 回 true；未呼叫 `TryBeginCast`（取消）不進冷卻。
- m. 落點：極速＝位置＋朝向×4m；拖曳 distance01＝0→2m、1→8m；法線＝方向單位向量。

**V3** `python Tools/DotnetCheck/mutation_check.py`：既有 18 筆全部仍被抓到，新增 ≥6 筆符印突變全部被抓到，至少含——輕點改成也送 WorldTap／取消區內鬆手仍送 Released／名冊上限改 3／壽命不倒數／取消也進冷卻／第 6 發以後倍率仍 1.0。（測試存在但沒有鑑別力）

**V4** Unity batchmode `-runTests`：EditMode 全綠；PlayMode 既有 8 個全綠，另加且全綠：
- a. 極速施放後 0.2s 內：場上恰有 1 面存活石牆，中心在英雄施放當下位置前方 4m（±0.1m）、牆面法線與英雄朝向夾角 <1°、`BoxCollider.enabled`。
- b. **阻擋＋正向對照**：同一條穿越路線，沒牆時英雄 3s 內越過牆平面；有牆時 3s 內未越過。（牆只有外觀沒有碰撞；或對照組顯示這條路本來就走不過去）
- c. 施放後 5.0s（+0.3s 容差）石牆不再存活、Collider 已關。
- d. 連放三面（測試中跳過冷卻的方式不得改到正式冷卻值）：第三面成形後第一面不再存活，存活數＝2。
- e. 拖曳更新→虛影 Renderer 可見且位置＝預期落點；鬆手→石牆成形於該落點、虛影隱藏；取消→無石牆、虛影隱藏、冷卻未開始。
- f. 冷卻中再次極速施放：存活石牆數不變。
- g. 零配置測試的量測窗口內包含一次石牆施放與一次石牆到期，GC Allocated In Frame 仍為 0 bytes，正向對照仍會變紅。

**V5** 範圍：`git diff --stat 31cddf6..` 逐檔對應 §1；`HeroCombatBrain.cs`、`PlayerStateMachine.cs`、`CadenceSim.cs` 零改動；既有測試檔除 §1 列明者外零改動。

**V6** fresh-context opus 對抗審查：CRITICAL／HIGH 全修或經使用者簽准；修完再送一次三態覆審（上限 3 輪）。

**V7** 送達與實機：`git log origin/gh-pages -1` 顯示 v0.3.0 部署；線上首頁版本列＝0.3.0；Playwright 開線上網址（含 `hasTouch` 手機模擬）——輕點符印鈕後截圖可見石牆且 console 無 error／exception；拖曳時截圖可見虛影；點牆後地板，英雄被牆擋住（位置未越過牆平面）。
