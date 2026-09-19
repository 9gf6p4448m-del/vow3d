# 計畫：Phase 2 批 2 —— 0.5m 格點阻擋網格＋向量場繞牆（2026-09-19）

> 已知良好狀態：`1931b53`（v0.3.2，main＝origin/main，gh-pages＝`ef1db74`）。工作分支 `phase2-batch2`；任何一步做壞就退回這裡。
> 規格依據：`GDD.md:214-216`、`ARCHITECTURE.md:17`（紅線 5：NavMesh 100% 靜態、石牆只開 BoxCollider、禁 carving）。
> 本檔 §5 為凍結驗收條件；要動它只能走 `02 §2.1`（寫明原標準錯在哪＋使用者針對該條同意）。

## 0. 使用者裁定（2026-09-19，需求對齊一輪，四題全照建議）

1. v0.3.2 試玩沒問題，直接開批 2。
2. 目的地被牆圍死／走不到 → **走到最近可達點停下**（不發呆、不頂牆、不自動砍牆）。
3. 石牆生成壓到英雄 → **牆照立，把英雄推到最近的空格**（順便關掉「起點已重疊放行＝可穿牆」的洞）。
4. WebGL `Screen.dpi=192` 那把尺**先不動**，等 Android 原生建置再談。

自決並已告知：以目的地為源的整合場（Dijkstra）；追擊目標被牆隔開時自動繞牆；HUD 加 GRID 除錯開關。

## 1. 檔案清單與批次

### 步驟 A：純邏輯（不需 Unity，`verify.sh` 可驗）
- 新增 `Assets/Scripts/Core/Logic/NavGridTuning.cs` —— 批 2 全部數值的單一來源（格寬 0.5、原點 −20/−20、80×80、逃脫搜尋半徑、平滑前視格數）
- 新增 `Assets/Scripts/Core/Logic/BlockGrid.cs` —— 阻擋網格（逐格引用計數、版本號、視線、最近空格、圓對 OBB 重疊）
- 新增 `Assets/Scripts/Core/Logic/FlowField.cs` —— 整合場（8 鄰接 10/14、禁止切角、預配置陣列＋二元堆）
- 新增 `Assets/Scripts/Core/Logic/GridNavigator.cs` —— `HeroLocomotion` 唯一會碰的入口（目標解析＋轉向）
- 新增 `Assets/Tests/EditMode/BlockGridTests.cs`、`FlowFieldTests.cs`、`GridNavigatorTests.cs`
- 修改 `Tools/DotnetCheck/mutation_check.py` —— 追加格點突變（只增不改既有 35 筆）
- 修改 `Tools/DotnetCheck/PureLogic.Tests/PureLogic.Tests.csproj` —— 僅在需要納入新檔時加行
- **Checkpoint A**：`verify.sh` 全綠、突變全抓到；Unity 端尚未接線，遊戲行為與 v0.3.2 完全相同。

### 步驟 B：Unity 端接線
- 修改 `Assets/Scripts/Core/HeroLocomotion.cs` —— `SetNavigator`；`MoveTo`／`Chase` 先解析目標；`Step` 在視線被擋時改走向量場；新增推出重疊的入口。**`SetNavigator(null)` 或從未設定時，行為與 `1931b53` 逐行相同。**
- 修改 `Assets/Scripts/Combat/RuneWall.cs` —— 啟用時登記格點、每一條離場路徑都撤銷（見 §4 假設 8）
- 修改 `Assets/Scripts/Combat/TestWallTarget.cs`（或其基底）—— 靜態測試牆登記／死亡撤銷／重生再登記
- 新增 `Assets/Scripts/Bootstrap/NavGridDebugView.cs` —— GRID 除錯疊圖（單一預建 Mesh，預設關）
- 修改 `Assets/Scripts/UI/DebugHud.cs` —— 多一顆 `GRID: OFF／ON` 按鈕
- 修改 `Assets/Scripts/Bootstrap/Phase1Bootstrap.cs` —— 建立 `BlockGrid`／`GridNavigator`、接線、石牆啟用時觸發推出
- 修改 `Assets/Scripts/Editor/VOWPhase1SceneBuilder.cs` —— 預建除錯疊圖物件（執行期不得 `CreatePrimitive`）
- 新增 `Assets/Tests/PlayMode/WallDetourPlayTests.cs`；修改 `Assets/Tests/PlayMode/ZeroAllocationTests.cs`（只加行使，不動門檻）；`ScriptedInput.cs` 僅在需要時補輔助方法
- **Checkpoint B**：Unity batchmode EditMode＋PlayMode 全綠。

### 步驟 C：上線
- `VowVersion.cs` → `0.4.0`；`docs/PHASE1_ACCEPTANCE_GUIDE.md` 補 §12（批 2 玩法／解讀／已知限制／驗證紀錄）
- fresh-context opus 對抗審查 → 修 → 三態覆審（上限 3 輪）→ merge 回 main → push → `Tools/deploy-webgl.bat` → 真實瀏覽器驗線上版

## 2. 介面（先寫死再實作；全部零 UnityEngine、執行期零配置、不用 Linq）

```csharp
// Core/Logic/BlockGrid.cs
public sealed class BlockGrid
{
    public BlockGrid(float originX, float originZ, float cellSize, int columns, int rows);
    public int Version { get; }        // 只有「某格的 Blocked 狀態翻轉」才 +1（計數 1→2 不算）
    public int BlockedCount { get; }
    public bool IsBlocked(int cx, int cz);                       // 界外一律 true
    public bool TryWorldToCell(float x, float z, out int cx, out int cz);
    public void CellCenter(int cx, int cz, out float x, out float z);

    // 牆的 OBB（中心、牆面法線、半寬＝沿牆方向、半厚＝沿法線方向）各向外擴 inflate 後，
    // 「格子方塊與它相交」的每一格計數 += delta（+1 登記、−1 撤銷）。界外部分直接裁掉、不丟例外。
    public void StampBox(float centerX, float centerZ, float normalX, float normalZ,
                         float halfWidth, float halfThickness, float inflate, int delta);

    // 線段經過的每一格（supercover：擦過格角也算經過）都不是 Blocked
    public bool HasLineOfSight(float x0, float z0, float x1, float z1);

    // 以歐氏距離（到格心）找最近的非 Blocked 格；同距離取索引較小者（決定性）
    public bool TryFindNearestFree(float x, float z, int maxRadiusCells, out int cx, out int cz);

    public static bool CircleOverlapsBox(float px, float pz, float radius, float centerX, float centerZ,
                                         float normalX, float normalZ, float halfWidth, float halfThickness);
}

// Core/Logic/FlowField.cs
public sealed class FlowField
{
    public FlowField(BlockGrid grid);
    public void Build(int goalCx, int goalCz);   // Dijkstra；直走 10、斜走 14；斜走時兩個正交鄰格任一 Blocked 就禁止（不切角）
    public bool IsReached(int cx, int cz);
    public int CostAt(int cx, int cz);           // 未到達＝int.MaxValue
    public bool TryGetNext(int cx, int cz, out int nx, out int nz);   // 成本最低的合法鄰格；同成本取索引較小者
    public int LastBuildPopCount { get; }        // 每格至多出堆一次
    public int BuiltForVersion { get; }  public int GoalCx { get; }  public int GoalCz { get; }
}

// Core/Logic/GridNavigator.cs
public enum SteerMode { Direct, Follow, Stuck }
public sealed class GridNavigator
{
    public GridNavigator(BlockGrid grid, NavGridTuning tuning);
    public BlockGrid Grid { get; }

    // 目的地那格非 Blocked 且從 (fromX,fromZ) 走得到 → 原樣回傳、substituted=false。
    // 否則回傳「from 所在連通區內、格心離目的地歐氏距離最近」那一格的格心、substituted=true。
    // from 自己落在 Blocked 格（貼牆站在外擴區）時，以離它最近的空格當起點。
    public void ResolveGoal(float fromX, float fromZ, float destX, float destZ,
                            out float goalX, out float goalZ, out bool substituted);

    // Direct＝到 goal 有視線（呼叫端沿用 NavMesh 的 desiredVelocity，Phase 1 行為不變）
    // Follow＝給單位方向：沿整合場往前看最多 N 格，取「從實際位置仍有視線」的最遠格心（拉直路徑）
    //         所在格是 Blocked／未到達時，方向指向最近的已到達空格（先走出外擴區）
    // Stuck ＝連逃脫格都找不到
    public SteerMode Steer(float x, float z, float goalX, float goalZ, out float dirX, out float dirZ);
}
```

`HeroLocomotion` 新增：`void SetNavigator(GridNavigator nav, float inflateRadius)`、`bool EjectFromBox(centerX, centerZ, normalX, normalZ, halfWidth, halfThickness)`（圓與 OBB 有實體重疊才動；移到最近空格格心後 `SyncAgent`；回傳有沒有動）。`MoveTo` 記下原始目的地，`_agent.SetDestination(解析後的 goal)`——`HasArrived` 因此不必改。持有指令期間格點 `Version` 變了就用原始目的地重新解析一次。

## 3. 不做什麼

- 批 3（破敵牆護盾、友軍穿透、陣營圖層）、批 4（元素反應）；**不修**石牆兩本血量帳與 Ignore Raycast 做法（批 3 前必修，照舊記帳）
- 動態單位互相避障（GDD 的「RVO」那一半）：場上只有一個會動的英雄，Dummy 不進格點
- 微彈滑步不走格點：仍是 SphereCast 裁切＋貼牆滑一次（Phase 1 手感核心不動）
- 不動 `HeroCombatBrain`／`PlayerStateMachine`／`CadenceSim`／`RuneGestureTracker`／`RuneCastLogic`／`RuneWallLogic` 任何一行
- 不動 dpi 那把尺、不動既有測試的斷言與容差、既有 35 筆突變、`verify.sh` 的紅線 grep
- 不用 `NavMeshObstacle`、不重烘 NavMesh、不做多英雄共用場的快取策略

## 4. 假設與取捨（自行採用、全部可推翻，會同步寫進驗收指南 §12）

1. 格點涵蓋整個 40×40m 場地（80×80＝6400 格）；界外視為 Blocked。場地邊界內縮的那 0.5m 仍由既有邊界牆與 NavMesh 夾回負責。
2. 進格點的只有：符印石牆（存活期間）＋`TestWall_A/B`（存活期間）。外擴量＝英雄 `BodyRadius`（0.35m），寫死單一體型。
3. **到目標有視線時完全走舊路徑**（NavMesh `desiredVelocity`）：沒有牆擋路的情況下，批 2 對 Phase 1 手感零影響。
4. 整合場只在「視線被擋」且（目標格，格點版本）改變時重建；追擊每 0.1s 的 repath 對靜止目標不會重建。
5. 推出只看**實體重疊**（圓對 OBB），不看外擴區——貼牆站著是常態，不該被彈開。推出不看英雄當下狀態（出招中也推）；位置瞬移、不補動畫【試玩必調】。
6. 牆消失或新牆出現時，只有「還持有移動／追擊指令」的英雄會重新解析；已經到替代點停下的英雄不會自己再走（不搶控制權）。
7. 點在牆腳下（牆在 Ignore Raycast 層，射線打到牆下的地板）＝目的地落在 Blocked 格，走裁定 2 的同一條路。
8. 石牆離場路徑的分母由實作者先 `grep` 數出來（壽命到期、被近戰打爆、穿透耗盡、名冊擠掉、`Initialize` 重入、物件停用……），寫進 §12；**登記時把當下用的七個參數存起來，撤銷用同一組**（不從 transform 重算）。
9. GDD 的「0.05ms」當作**每幀轉向**的預算；`Build` 是事件驅動。時間量測只進驗證紀錄、不當及格線（計時閘門會隨機器負載忽紅忽綠，`02 §6.2`）；及格線改用決定性的「每格至多出堆一次」＋零配置。
10. GRID 疊圖只畫 Blocked 格（一張預配置 Mesh，版本變了才重填）；預設關；零配置量測在 GRID 關閉下進行，但疊圖本身重填也不得配置。

Simplicity 例外：

| 違反了什麼 | 為何必要 | 更簡單的方案為何被否決 |
|---|---|---|
| 做了整合場而非「貼牆切線滑動」 | U 形／L 形牆（兩面符印牆＋測試牆就排得出來）切線滑動會卡死 | 切線法在凹角無解，會回到「頂著牆走」 |
| 拉直路徑（視線前視） | 8 鄰接格點路徑是 45° 鋸齒，手機上看得出英雄在抖 | 純格點路徑 |
| GRID 除錯疊圖 | 使用者用手機試玩、回報「繞得很怪」時，截圖要看得到格點才能歸因 | 不做，靠猜 |

## 5. 端到端驗證步驟（凍結）

每條後面括號＝「什麼樣的實作會讓這條變紅」。時間上限一律用公式 `T = 1.5 × 幾何最短繞行長度 ÷ 英雄移動速度`（長度由場景座標算出並寫在測試註解裡，速度讀 tuning，不得寫死一個寬鬆常數）。位置容差：抵達 ≤0.3m、不穿牆 0.03m；實作中不得放寬。

**V1** `bash Tools/DotnetCheck/verify.sh` 全數通過：既有 102 個純邏輯測試一個不少且 0 失敗、compile-only 0 error、紅線 grep 全過。（弄壞既有手感／符印測試；新邏輯用了 UnityEngine 或 Linq；石牆用了 NavMeshObstacle）

**V2** 新增的純邏輯測試至少涵蓋下列行為，且全綠：
- a. **登記錨點**：80×80、原點 −20、格寬 0.5；牆心 (0,0)、法線 (0,1)、半寬 2、半厚 0.3、外擴 0.35 → `BlockedCount == 40`（x 格 35～44、z 格 38～41），四個角格與緊鄰外圈各抽驗；法線改 (1,0) → 仍 40 且行列互換。（外擴沒算進去；用格心取樣而非方塊相交；寬厚軸搞反）
- b. 45° 牆：牆兩端點 (±1.414, ±1.414) 所在格 Blocked；垂直方向 2m 外的 (1.414, −1.414) 所在格不是。（旋轉沒處理、當成軸對齊包圍盒）
- c. 引用計數：兩面重疊的牆，撤一面→重疊格仍 Blocked；都撤→`BlockedCount == 0`；`Version` 只在格子翻轉時增加（同一組參數 +1 再 +1，第二次 Version 不變）。（用 bool 而非計數；撤銷不對稱）
- d. 界外：牆心在 (19.9, 0) 登記不丟例外、界內部分有登記；`IsBlocked(-1,0)`、`IsBlocked(80,0)` 為 true。
- e. 視線：無牆 true；線段穿過牆 false；線段只擦過 Blocked 格的格角 false；兩端點同格 true。（只查端點；Bresenham 漏掉擦角格）
- f. **整合場差分**：三種佈局（單牆、L 形、U 形）下，`CostAt` 全場逐格等於測試內自帶的暴力鬆弛法（反覆掃到不再變動）的結果；**活性**：至少一格的成本大於無牆時的成本。（切角、堆排序錯、提前終止）
- g. 沿 `TryGetNext` 從起點走到目標：成本嚴格遞減、不踏入 Blocked 格、每個斜步的兩個正交鄰格皆非 Blocked。
- h. `LastBuildPopCount ≤ 6400`，且無牆時 `== 6400`。（重複出堆的退化實作；沒有真的跑全場）
- i. **圍死**：目的地被一圈牆圍住 → `substituted == true`，回傳格等於暴力掃描「起點連通區內離目的地最近的格」；拿掉一面牆後 → `substituted == false`、回傳原目的地。（走不到還回原目的地→英雄頂牆；永遠回替代點）
- j. 目的地落在 Blocked 格（點牆腳）→ 同 i 前半。
- k. 起點在 Blocked 格（貼牆的外擴區）：`Steer` 不是 `Stuck`，方向與「指向最近空格」的內積 > 0；`ResolveGoal` 不因此誤判為走不到。
- l. 有視線 → `Direct`。
- m. **U 形陷阱模擬**：三面牆排成 U、英雄在 U 內、目標在 U 底外側；純邏輯每步 0.1m 沿 `Steer` 方向前進 → 在 `1.5 × 最短繞行長度 ÷ 0.1` 步內到達目標 0.3m 內，過程中從未踏入「起始時非 Blocked 後來也非 Blocked」以外的格（即從不進 Blocked 格）；**對照**：同場景改成「永遠朝目標直走」的樸素轉向，在同步數內到不了。（區域極小值卡死；拉直路徑時穿牆）
- n. `TryFindNearestFree` 與暴力掃描一致（含同距離的決定性）；`CircleOverlapsBox`：圓心在盒內 true、離盒邊 radius−ε true、radius+ε false、45° 盒的角落外 false。
- o. 零配置：暖機後 `Build`＋`Steer`＋`ResolveGoal`＋`StampBox` 各跑 100 次，`GC.GetAllocatedBytesForCurrentThread()` 差值 == 0；**正向對照**：同一段量測夾一個 `new int[16]` 會讓差值 > 0。

**V3** `python Tools/DotnetCheck/mutation_check.py`：既有 35 筆全部仍被抓到；新增 ≥8 筆全部被抓到，至少含——允許切角／引用計數改 bool／忽略外擴量／界外 `IsBlocked` 回 false／視線只查兩端點／走不到仍回原目的地／格子翻轉不加 `Version`／`delta=-1` 不生效。突變一律指向**非參數化**測試；每改一次判定式就重跑全部突變。（測試存在但沒有鑑別力）

**V4** Unity batchmode `-runTests`：EditMode 全綠；PlayMode 既有 18 個**原封不動**全綠，另加且全綠：
- a. **繞牆＋雙向對照**：英雄與目的地之間放一面符印牆（目的地在牆正後方）→ 在 T 內抵達目的地 0.3m 內；**同場景 `SetNavigator(null)`** → 同樣的 T 內到不了（這就是 v0.3.2 的頂牆行為）。（格點沒接上；或對照組顯示這條路本來就繞得過去）
- b. 不穿牆：a 的全程每一幀，英雄圓心到牆 OBB 的距離 ≥ `BodyRadius − 0.03`。
- c. **點牆腳**：目的地在牆的外擴區內 → T 內 `HasArrived == true`、英雄速度為零、狀態機回到 `Idle`，且停點離目的地 ≤ 牆半厚＋外擴＋一格對角線。（永遠 Moving；或停在很遠的地方）
- d. **角落圍死**：一面牆斜 45° 封住場地一角、目的地在三角形內、英雄在外 → 同 c 的三個斷言；且英雄全程沒有進入三角形。
- e. 牆消失後重新解析：英雄正走向替代點時 `CollapseWall` → 最終抵達**原始**目的地 0.3m 內。
- f. **推出**：在英雄腳下啟用一面牆 → 啟用後的下一幀，`CircleOverlapsBox(英雄, 牆) == false`、英雄仍在 NavMesh 上；隨後下令走到牆另一側 → T 內抵達且滿足 b。**對照**：不呼叫推出的話，同一幀重疊為 true。
- g. **追擊繞牆＋對照**：Dummy 與英雄之間隔一面牆，下攻擊指令 → T＋一次攻擊週期內 Dummy 血量下降；`SetNavigator(null)` → 同時間內血量不變。
- h. 無牆擋路時走直線：起點到目的地有視線的路線，全程離直線的最大側向偏移 < 0.05m。（把所有移動都改走格點→鋸齒）
- i. 零配置：量測窗口內包含「一面牆擋住當前路徑→觸發 `Build`＋`Follow` 轉向」與「該牆到期撤銷」，`GC Allocated In Frame` 仍為 0 bytes；既有正向對照仍會變紅。
- j. GRID 疊圖：預設 Renderer 關；切到 ON → 開，且 Mesh 的四邊形數 == `BlockedCount`；放一面牆後數字跟著變。

**V5** 範圍：`git diff --stat <批 2 分支的起點>..` 逐檔對應 §1（2026-09-19 註：本檔寫完後、批 2 動工前，main 先插了兩個試玩回饋修正 v0.3.3／v0.3.4，其中 v0.3.4 依使用者裁定改了 `RuneCastLogic.cs`；那不是批 2 的改動，所以比對起點由 `1931b53` 改為 v0.3.4 的 commit——批 2 自己對 §3 六個檔仍須零改動，及格線未變）；§3 點名的六個檔零改動；既有測試檔除 §1 列明者外零改動，`ZeroAllocationTests.cs` 的門檻與正向對照零改動。

**V6** fresh-context opus 對抗審查（prompt 必含：逐一比對測試裡的數字常數與本檔是否一致、有沒有測試自己抹掉座標軸或放寬容差、`SetNavigator(null)` 是否真的逐行等同舊行為、石牆離場路徑的分母是否數全）：CRITICAL／HIGH 全修或經使用者簽准；修完三態覆審，上限 3 輪。

**V7** 送達與實機：`git log origin/gh-pages -1` 顯示 v0.4.0 部署；線上首頁版本列＝0.4.0；Playwright 開線上網址（含 `hasTouch` 手機模擬）——輕點符印鈕放牆、點牆正後方地板 → 連續截圖可見英雄繞過牆端、最後站在牆後；開 GRID 截圖可見 Blocked 格疊在牆上；console 無 error／exception。

**驗證紀錄另附（不當及格線）**：dotnet 環境下 `Build` 80×80 單牆 1000 次的中位數耗時、`Steer` 單次中位數耗時。

## 6. r1 對抗審查後的修訂（2026-09-19；報告 `vow-toolchain/REVIEW-p2b2-r1.md`：CRITICAL 1／HIGH 4／MEDIUM 5／LOW 5）

本節優先於 §4、§5 中與之衝突的文字。R1、R2 動到凍結判準，已依 `02 §2.1` 寫明原因並取得使用者逐條同意；R3 以後是加嚴或補洞。

### R1（H2）替代點優先停在英雄這一側 —— **使用者 2026-09-19 同意**
- 原標準錯在哪：V2-i／j 把「最近可達點」定成「格心離目的地歐氏距離最近」，完全不看英雄在哪；實測英雄在牆北 (0,8) 點牆腳 (0,4)，被送到牆南 (−0.25,2.75)。為什麼現在才知道：V4-c 的停點容差（1.357m）比兩側距離（1.27m）大，測試分不出哪一側，審查實跑才現形。
- 新定義：令 dMin＝起點連通區內「格心到目的地」的最小歐氏距離；候選＝距離 ≤ dMin＋一格對角線（格寬×√2）的格；候選中取**從起點出發的路徑成本最低**者；同成本取離目的地較近者；再同取索引較小者。
- V2-i／j 的暴力對照改用同一定義的獨立實作（成本用測試內自帶的暴力鬆弛法，不得呼叫受測物）。
- 新增 V2-p（牆心 (0,4)、法線 +Z、半寬 2、半厚 0.3、外擴 0.35）：①英雄 (0,8) 點 (0,4) → 替代點 z > 4.65 ②英雄 (0,0) 點 (0,4) → z < 3.35 ③英雄 (0,8) 點 (0,3.8) → z > 4.65（兩側差 0.2m，仍在一格對角線內）④英雄 (0,8) 點 (0,3.4) → z < 3.35（兩側差 1.2m：使用者明顯點在牆的另一面，就該繞過去）。
- V4-c 加一條斷言：停點與英雄起點在牆的同一側。

### R2（H3）拿掉「目的地在格點外就整趟退回 Phase 1」的特例，改寫兩條既有測試 —— **使用者 2026-09-19 同意**
- 原標準錯在哪：V4 寫「既有 18 個 PlayMode 測試原封不動全綠」，但其中 `RuneWallPlayTests.Wall_BlocksTheHero_AlongTheSamePath` 與 `Wall_IsInvisibleToWorldTapRaycast_ButStillBlocksTheHeroPhysically` 斷言的正是批 2 要消滅的行為（英雄頂著牆過不去）。為什麼現在才知道：寫計畫時沒逐條讀那 18 個測試；實作者撞到後加了一條只有 `(0,0,20)` 這種「恰在最外線」的目的地才走得到的分支讓它們維持綠，審查實跑（z=19 就繞過去、z=20 頂牆）才確認那是只為測試存在的死分支。
- 新標準：那條分支刪除；格點外的目的地先夾進格點再照常解析。那兩條測試**只多一行 `SetNavigator(null, 0f)`**（它們要守的本來就是「牆的碰撞體擋得住身體」），斷言與數值一字不動；V4 改為「既有 18 個全綠，其中 16 個原封不動、上述兩條只多這一行」。

### R3（H1）格點最外圈＝英雄到不了的地方，必須是 Blocked
- §4 假設 1 作廢。新不變量（PlayMode 實量）：**每一個非 Blocked 格的格心，都落在「邊界牆內面 − BodyRadius」以內**（以場景裡實際的邊界 BoxCollider 為準，不寫死 19.45）。數值單一來源放 `NavGridTuning`；純邏輯測試 `V2-h`（裸 `BlockGrid` 無牆時 popCount==6400）不受影響——外圈由導航器／接線層登記，不改 `BlockGrid` 建構子的語意。
- 新增 V4-k（審查 Probe2 的情境）：英雄 (17.15,0,−4) 朝 +Z 極速施放、點 (17.15,0,4) → 在 T 內抵達 0.3m 內（T 用繞西端外擴牆角的真正最短折線）；對照：外圈不登記 → 同 T 內到不了。

### R4（C1）推出按「效果」寫，不按「入口」寫
- 任何阻擋物登記進格點（`RegisterNavBlocker` 是唯一入口；先 grep 數出呼叫點與所有子類）都要觸發推出，不再只掛 `RuneWall.OnActivated`。
- 新增 V4-l（審查 Probe3 的情境）：打爆 `TestWall_A` → 英雄站到牆心 → 等重生 → 重生後下一幀 `CircleOverlapsBox == false` 且英雄在 NavMesh 上；隨後下令走到牆另一側，全程滿足 V4-b 的不穿牆不變量。對照：不推出 → 同一幀重疊為 true。

### R5（H4）有視線就不碰整合場
- `ResolveGoal`：目的地格非 Blocked 且 `HasLineOfSight(from, dest)` → 原樣回傳、**不 Build**。導航器公開累計 Build 次數（唯讀）。
- 新增 V2-q：空格點連下 100 次不同目的地 → Build 次數 0；有牆但不擋視線 → 0；牆擋視線 → 第一次 1、同目的地同版本重複呼叫仍 1。新增對應突變（拿掉視線快路）。

### R6（MEDIUM）
- M1 `StampBox` 計數不得為負：夾在 0 並回報錯誤（純邏輯層用可測的方式，例如回傳值或計數器）；新增測試＋突變。
- M2 GRID 疊圖重填的零配置要**實量**：Profiler 探針窗口內 GRID 為 ON 且發生至少一次重填，仍 0 bytes；正向對照仍會紅。
- M3 「目的地場」與「替代點場」分成兩個 `FlowField`。新增 V2-r：目的地走不到、格點版本不變時，重複 `ResolveGoal`＋`Steer` 50 輪，Build 次數在第一輪之後不再增加。
- M4 `SteerMode` 拆開「goal 格被蓋住」與「找不到逃脫格」；前者 `HeroLocomotion` 當幀立刻用原始目的地重新解析，不得退回頂牆。新增純邏輯測試覆蓋 goal 格 Blocked 的 `Steer`。
- M5 `GridNavigator` 公開 tuning（唯讀），`HeroLocomotion` 的 `EjectSearchRadiusCells` 複本刪除。

### R7（LOW 與鑑別力缺口）
- L1 U 形測試的最短繞行長度更正為 **16.12m**＝3.6118＋1.30＋6.70＋4.5106（(0,−3)→右壁內上角 (1.35,0.35)→外上角 (2.65,0.35)→外下角 (2.65,−6.35)→(0,−10)）。主對話先前手算的 15.48m 漏了內角、那條線會穿過右壁；方向是過嚴不是放水，但 §5 要求的是真值。
- L2 更正 V4-h 註解的距離數字。L3 新增一個**只在 Unity 下執行**的 EditMode 測試，實證「`GC.GetAllocatedBytesForCurrentThread()` 對一次真實配置回報 0」——三個 `[Ignore]` 的前提從此有證據；哪天 Unity 修了，這條會紅、提醒把它們放回來。L4 `SetNavigator(null)` 在持有指令期間呼叫時，把 agent 的目的地改回原始目的地。L5 V4-c／d 的等待迴圈先讓出兩幀，並斷言中途曾進入 `Moving`。
- 拉直路徑的鑑別力：新增 V2-s——單面牆情境（V4-a 的幾何）純邏輯每步 0.1m 模擬，實走長度 ≤ 幾何最短 9.48414m × 1.06；對照：`FollowLookaheadCells=1` 的實走長度 > 該上限。
- 突變清單補上 `FlowField.TryGetNext` 的禁止切角判斷。

### R8 第一輪修復（`28d5191`）後主對話覆核的更正（2026-09-19）

**R1a 取代 R1 的選點公式（行為意圖不變：優先停在英雄這一側）。** R1 的「候選帶內取路徑成本最低者」有副作用：停點會被往英雄方向多拉一格（V4-c 由 (−0.25,2.75) 變成 (0.25,2.25)，離使用者點的牆腳多 0.5m），實作者因此把 §5 V4-c 的凍結停點容差由「一格對角線」放成「兩格對角線」。依 `02 §2.1`，過不去要改的是實作（這裡是主對話自己寫壞的公式），不是容差：
1. 候選帶 B＝起點連通區內、格心到目的地距離 ≤ dMin＋一格對角線（含 0.1mm 比較容差）的格。
2. W＝B 內從起點出發路徑成本最低者（決定「哪一側」）。
3. 取 B 內路徑成本 ≤ cost(W)＋28（兩個斜步；語意＝「為了更靠近你點的位置，最多多走約 1.4m」）的格之中，**格心離目的地最近**者；同距離取路徑成本低者；再同取索引小者。28 放進 `NavGridTuning`。
- V2-p 四個案例的期望不變。**V4-c 的停點容差改回 §5 原文（牆半厚＋外擴＋一格對角線）**，第一輪加上的「精確格心座標」與「與起點同側」兩條斷言保留（期望格心依 R1a 重算，預期回到 (−0.25,2.75)）。V4-d／V4-e 的期望值與 T 依 R1a＋R3a 重算並寫出推導。V2-i／j 的獨立暴力對照同步改成 R1a。

**R3a 邊界只擋「格心到不了」的那一圈。** 第一輪用方塊相交登記邊界，擋掉兩圈（624 格），使「沒有牆擋路時」點場地邊緣 19.0～19.45m 會被替代到 18.75——違反最高優先序（沒有牆擋路時 Phase 1 行為不變）。改為：
- 可達範圍＝場景實際邊界 BoxCollider 內面 − BodyRadius（現值 19.45）。**格心落在可達範圍外的格**才登記為 Blocked；現行幾何下預期恰為最外一圈 316 格（4×80−4），測試要斷言這個數字。R3 的不變量（每個非 Blocked 格心都在可達範圍內）照舊。
- 目的地先夾進可達範圍（不只是夾進格點），再照常解析。
- 新增 V4-m（無牆、Phase 1 行為回歸）：英雄 (10,0,0) 點 (19.3,0,6) → T 內抵達 0.3m 內、全程側向偏移 < 0.05m、整趟 `BuildCount` 增量為 0。V4-k 照舊要綠（牆外擴端 19.5 蓋住第 78 圈、第 79 圈是邊界 → 東側封死、繞西端）。

**R9 `HasLineOfSight` 的 DDA 缺陷（實作者在第一輪發現並修掉，不在 r1 findings 內）要有自己的回歸測試與突變**：空格點上，兩端點都恰在格角的線段（至少含 (0,0)→(10,10)、(−5,3)→(7,3)、(2,−8)→(2,9) 與其反向）一律有視線；新增突變（拿掉那兩行修正）必須被**這個**測試抓到，不是只靠 V2-q 間接抓。

### R10 r2 覆審後的文字更正（2026-09-19；報告 `vow-toolchain/REVIEW-p2b2-r2.md`：三態 17／1／0，新 finding MEDIUM 3／LOW 6，無 CRITICAL／HIGH）
- R8 括號裡預告 V4-c「預期回到 (−0.25,2.75)」是主對話忘了套用自己寫的平手規則：(39,45) 與 (40,45) 到目的地等距，階段 3 先比路徑成本（50 < 54）→ 正確答案是 **(0.25,2.75)**，實作與測試是對的（r2 N6）。
- R9 點名的三條線段（45°／水平／垂直）對 DDA 缺陷**沒有鑑別力**（修復前就回 true）；有鑑別力的是實作者加的非 45° 斜線 `(0,0)→(−10,9)`、`(−6,−4)→(9,8)`（r2 N5）。
- r2 的 M3（表面修好）、N1～N3、N7～N10 排入 v0.4.1；N4（視線在格界上的語意）記錄不修。

### R11 v0.4.1：r2 遺留項的凍結驗收條件（2026-09-19 動手前訂定；已知良好狀態＝main `0dd3eb5`）

範圍只有 r2 的 M3／N1／N2／N3／N7／N8／N9。**行為意圖不變**：替代點選點照 R1a、沒有牆擋路時 Phase 1 行為不變、§3 六個檔與全部 asmdef 零改動。時間量測照 §4-9 不當及格線，所以下面全部用「次數」當代理指標。

**V11-a（N1 掃描合併，純邏輯）** `GridNavigator` 公開唯讀累計計數 `ScannedCellCount`（替代點解析時每檢查一格加 1，仿 `BuildCount`）。80×80 格點、單面牆、點牆腳（目的地格 Blocked）解析一次：`ScannedCellCount` 增量 ≤ 8000（現行三趟＝19200，會紅）。不得為了過這條而每次呼叫配置陣列（見 V11-f）。

**V11-b（N1 不改行為，純邏輯）** 差分測試：固定種子、≥300 個隨機盤面（1～3 面隨機角度的牆、隨機 from／dest），`ResolveGoal` 的 (goalX, goalZ, substituted) 與測試內**獨立的** R1a 暴力參照（沿用 V2-i／j 那份，不得改成呼叫受測物）逐值相同；活性：其中 `substituted==true` ≥ 60 個。**這條要先在未改動的 `0dd3eb5` 程式碼上跑綠**（它是特徵化測試），改完仍綠。新增突變：把合併後的掃描範圍／候選條件改壞一處，必須被這條抓到。

**V11-c（M3 追擊不重算，PlayMode）** r2 的 P10 情境：追一個站在 `TestWall_A` 外擴區內、靜止的木樁。從下追擊指令起 3 秒：`BuildCount` 總增量 ≤ 4，且最後 1 秒增量＝0（現行 2 秒 14 次，會紅）。英雄最後停在與起點同側的替代點。

**V11-d（M3 不得過度快取，PlayMode；三個子情境各自斷言）** ①目標換格：追擊中把木樁瞬移到另一個走不到的位置（另一面牆的外擴區）→ 0.3 秒內 agent 目的地換成新的替代點（與新位置的距離 ≤ 牆半厚＋外擴＋一格對角線）。②格點版本變了：追擊走不到的目標途中那面牆消失 → 英雄改走向目標本身並進入攻擊距離。③目標在可達處移動（r2 P9）：追會動的目標 2 秒，`BuildCount` 增量仍為 0 且英雄與目標距離最後 ≤ 攻擊距離＋0.5m。紅燈條件：快取只看「有沒有解析過」而不看目標格／版本。

**V11-e（N2）** 邊界圈建不出來（`_arenaBoundary` 為 null、或底下 0 個 `BoxCollider`）時必須 `Debug.LogError`，訊息含 `ArenaBoundary`。測試用 `LogAssert.Expect(LogType.Error, …)` 兩個情境各一條；現行程式碼上兩條都紅（沒有任何 log）。正常場景開場不得出現這條 error（既有測試的 LogAssert 無未預期 error 即為證）。

**V11-f（N3 零配置窗口要行使到新路徑）** `ZeroAllocationTests` 的量測窗口內另外發生：至少一次 `substituted==true` 的解析、至少一次 `SteerMode.GoalBlocked` 的當幀重解析；兩者都要在測試裡**斷言發生次數 ≥1**（活性），`UpdateBytes==0` 門檻、`Frames>=240`、正向對照一字不動。另加 dotnet 專用零配置測試：替代點解析 100 次、`StampCell` 200 次皆 0 bytes（Unity 下 `[Ignore]`，理由同 L3）。

**V11-g（N7）** PlayMode 兩條：持有「已被替代」的移動指令時 `SetNavigator(null, 0f)` → agent 目的地＝使用者原始目的地；追擊中 `SetNavigator(null, 0f)` → agent 目的地＝目標當下位置（容差 0.05m）。實作者要貼出「拿掉還原那幾行時這兩條變紅」的輸出。

**V11-h（N8／N9）** V4-d 起點改用精確值 `18 − 4/√2`、T 註解改 2.93930（方向＝加嚴）；`_navStamped1Handler` 改名 `_navStampedHandler`，`grep -rn _navStamped1Handler Assets/` 零命中。

**V11-i（回歸）** `verify.sh` ALL PASS；`mutation_check.py` 全抓到（55＋新增）；Unity EditMode、PlayMode 全綠；`git diff 0dd3eb5.. -- Assets/Tests` 的刪除行逐行列出並說明為什麼不提高通過機率；既有測試的門檻、期望值、容差不得改動（V11-h 點名的兩處除外）。

**N10 記錄不修**：連通區完全空（英雄格 Blocked 且 10m 內無空格）時 `ResolveGoal` 回傳 Blocked 格心——本作牆寬 4m、場地 40m 排不出這個盤面；只在 `GridNavigator.cs` 該處加一行註解說明。N4 照 R10 記錄不修。

**V11-j（2026-09-19 主對話覆核 `4d9bf5f` 後追加；加嚴，不動 V11-a～i）** M3 的快取不得改變「走得到」的追擊行為：現行實作連 `substituted==false` 的結果也快取，空地上追一個只在**同一格內**移動的目標時，agent 目的地會停在舊位置（最多差一格對角線）——違反最高優先序「沒有牆擋路時 Phase 1 行為不變」。條件：無牆空地、追擊靜止木樁 0.3 秒後，把木樁在**同一格內**平移 ≥0.3m（測試要斷言平移前後 `TryWorldToCell` 同格），再過 0.25 秒：`NavMeshAgent.destination` 與木樁當下位置的平面距離 ≤ 0.05m，且整趟 `BuildCount` 增量＝0。紅燈條件：`4d9bf5f` 的碼（目的地停在平移前的位置，差 ≥0.3m）。V11-c／d 照舊要綠。
