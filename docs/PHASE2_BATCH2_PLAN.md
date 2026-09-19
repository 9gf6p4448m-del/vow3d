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

**V5** 範圍：`git diff --stat 1931b53..` 逐檔對應 §1；§3 點名的六個檔零改動；既有測試檔除 §1 列明者外零改動，`ZeroAllocationTests.cs` 的門檻與正向對照零改動。

**V6** fresh-context opus 對抗審查（prompt 必含：逐一比對測試裡的數字常數與本檔是否一致、有沒有測試自己抹掉座標軸或放寬容差、`SetNavigator(null)` 是否真的逐行等同舊行為、石牆離場路徑的分母是否數全）：CRITICAL／HIGH 全修或經使用者簽准；修完三態覆審，上限 3 輪。

**V7** 送達與實機：`git log origin/gh-pages -1` 顯示 v0.4.0 部署；線上首頁版本列＝0.4.0；Playwright 開線上網址（含 `hasTouch` 手機模擬）——輕點符印鈕放牆、點牆正後方地板 → 連續截圖可見英雄繞過牆端、最後站在牆後；開 GRID 截圖可見 Blocked 格疊在牆上；console 無 error／exception。

**驗證紀錄另附（不當及格線）**：dotnet 環境下 `Build` 80×80 單牆 1000 次的中位數耗時、`Steer` 單次中位數耗時。
