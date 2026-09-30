# 計畫：v0.14.0 深淵峽谷——立體地貌 C 案第 1 批（三層高度＋6 處斜坡＋崖台規則＋2 個地熱點）

> 狀態：**已凍結（2026-09-30）**。凍結依據：r1〔`vow-toolchain/v0140-plan-review.md`〕→ r2〔`-r2.md`〕→ r3〔`-r3.md`〕→ r3 定點覆審〔`-r3fix.md`：可凍結，BLOCKER／HIGH 0〕；使用者裁定見 §14。凍結後 §2～§6 的規則與數值、§9 全部 V14 條文、§10 突變定義只能走 §12 修改。
> 已知良好狀態：`main 96730fd`（v0.13.1，`96730fdf34c1fbbb35982bc758a1ac0771f26aa3`），線上 `origin/gh-pages e82603c`（來源 `eb3c164`）。工作分支 `v0140-abyssal-canyon`（從 `96730fd` 開，起草時工作區乾淨）。
> 裁定紀錄（權威、唯一需求來源）：`vow-toolchain/v0140-alignment.md`（2 輪、7 題全部照建議）；示意圖 `vow-toolchain/v0140-layout-C.png`。範本：`docs/V0100_SANCTUARY_PLAN.md`（章節結構、共同遵守、指令、變更規則沿用）。
> 推導腳本：`vow-toolchain/v0140-reachability.py`（輸出 `v0140-reachability.out.txt`）。它是**重建模型**（從 `CaptureBoardSpec.cs` 解析 19 塊中心與 42 條邊，以 float64 逐行重建 `BlockGrid.StampBox`／`FlowField` 規則，以高度場近似鏡頭 Linecast），**不是被測物**，實作不得 import 或移植它的值當期望值；它的格點數字在 float32 邊界上可能差一格，凍結前由 §12.1-1 用 C# 實跑複核。
> 標記：【解讀】＝對齊紀錄沒講清楚、由起草者給出的解讀，全部同時列在 §13；【凍結前補】＝字面值須在 §12.1 前置作業實跑後寫入，未補不得凍結（r1：全數已補，計畫內已無待補欄位）。r1 新增字面值的 C# 重算：`vow-toolchain/v0140-refcs/Extra.cs`（`dotnet run -c Release -- r1`，輸出 `refcs-r1-output.txt`；C# 重建模型，不是 Unity 實跑）。
>
> 目的：把目前「只有 x/z 的平面格子導航」升級為 2.5D——每個位置有高度、層與層之間只能經斜坡（或地熱點單向上崖）移動；在正式 19 塊佔領局加上深淵峽谷版面、谷心核心、崖台射程／視野、谷底淺水與地熱點。WebGL 可以玩，不代表原生手機驗收已通過。

---

## 1. 範圍

### 1.1 本批做（對齊紀錄 Q1～Q6）

| 項目 | 來源 |
|---|---|
| 版面 C：低層峽谷 1-0-4、高層崖台 2／3／5／6、其餘中層；南北鏡像 | 裁定「版面 C」 |
| 高度 −1.0／0／+1.0 | Q4 |
| 6 處斜坡：9→2、11→3、15→5、17→6（崖台）；7→1、13→4（峽谷） | Q2 |
| 崖台規則：站崖台射程 +10%（雙方）、局部視野 6→8m；谷底看不到崖台上敵人（除非該塊己方真視野） | Q5 |
| 谷底淺水：水元素半徑 +1m | Q5 |
| 2 個地熱點：站 0.6s 彈上相鄰崖台、受傷中斷、每點冷卻 8s；東北彈上 2 號、西南彈上 5 號（180° 旋轉對稱） | Q5 |
| 核心移到谷心 (0,0)、半徑 2.5m，與 0 號佔塔圈重合（推翻 v0.13.1 修-1） | Q1 甲 |
| 紅方 AI 只走斜坡、不用地熱點 | Q6 |
| 驗收：英雄在谷底貼崖壁不被完全遮住；不過則網點透視提前到本批 | Q4 |
| 導航資料結構預留雙層接口（第 2 批岩橋用） | Q3 |

### 1.2 本批不做

- **岩橋**（雙層、4 次重擊斷裂）：第 2 批。**網點透視＋低窪外框**（GDD §伍-2）：第 3 批（除非 V14-B13 閘門判紅、依 Q4 提前）。
- 不改 CaptureMatchLogic 的**歸屬連通圖**（BFS 斷能、包夾、圍城仍用原 42 條邊，見 §4.6【解讀】）、不改計分／聖所／倒數／天賦數值、不改先鋒與巨獸數值（只改核心位置與半徑）。
- 不重烘 NavMesh、不做執行期烘焙、不加 `NavMeshObstacle`（紅線）；Off／單挑模式場地維持平地（§13 Q2）。
- 不做 Phase 4 網路同步、不做原生手機手感驗收。
- GDD §伍-1 寫「4 處固有斜坡」，對齊紀錄裁定 6 處；以裁定為準，GDD 待本批結束後由使用者決定是否同步（不在本批改 GDD）。

### 1.3 雙層接口（本批只預留、恆為單層）

岩橋會讓同一個 (x,z) 有兩個可站高度（橋面 +1.0、橋下谷底 −1.0）。本批的所有高度查詢都經過下列介面、帶層號，第 2 批只需要新增實作，不改呼叫端：

```csharp
namespace Vow.Core.Logic
{
    public enum TerrainClass { Plain = 0, Canyon = 1, Cliff = 2, Ramp = 3 }   // Plain＝中層與棋盤外

    public interface ITerrainQuery
    {
        int LayerCountAt(float x, float z);                    // 本批恆為 1
        float HeightAt(float x, float z, int layer);           // layer 0＝地面；第 2 批 layer 1＝橋面
        TerrainClass ClassAt(float x, float z, int layer);
        int ResolveLayer(float x, float z, float currentY);    // 由目前高度決定站哪一層；本批恆回 0
        bool IsSameFloor(float x0, float z0, int layer0, float x1, float z1, int layer1); // §5「同一樓地板」判定
    }
}
```

- 每個會移動的實體（英雄、對手、巨獸）持有 `int TerrainLayer`（本批恆為 0），高度一律 `HeightAt(x, z, TerrainLayer)`。
- 格點：本批仍是單張 `BlockGrid`（崖壁以引用計數蓋格，§7.2）。第 2 批的形狀先寫死在這裡以免接口漂移：`LayeredNavGraph` 持有「地面 `BlockGrid`＋橋面 `BlockGrid`」兩張格點與「橋端 portal」清單，節點＝(cell, layer)；`GridNavigator` 的公開簽章（`ResolveGoal`／`Steer`）屆時各加一個預設為 0 的 `layer` 參數。本批**不改** `GridNavigator` 簽章。
- V14-A01 斷言 `LayerCountAt` 全盤恆為 1、`ResolveLayer` 恆回 0：第 2 批改動時這條會紅，逼實作者回來改計畫而不是默默擴充。

---

## 2. 版面

座標系：沿用 `CaptureBoardSpec.V0100Sanctuary`（平頂六角、R＝4.375、h＝3.7890625；0 中央，1～6 中圈北起順時針，7～18 外圈北起順時針；+z＝北＝紅方）。

### 2.1 19 塊逐塊層別

| 塊 | 中心 (x, z) | 層 | 高度 | 備註 |
|---|---|---|---:|---|
| 0 | (0, 0) | 低（谷底） | −1.0 | 谷心；先鋒、核心、0 號佔塔圈；2 個地熱點踏點 |
| 1 | (0, 7.578125) | 低 | −1.0 | 峽谷北段 |
| 2 | (6.5625, 3.7890625) | 高（崖台） | +1.0 | 東北崖台；東北地熱點落點 |
| 3 | (6.5625, −3.7890625) | 高 | +1.0 | 東南崖台 |
| 4 | (0, −7.578125) | 低 | −1.0 | 峽谷南段 |
| 5 | (−6.5625, −3.7890625) | 高 | +1.0 | 西南崖台；西南地熱點落點 |
| 6 | (−6.5625, 3.7890625) | 高 | +1.0 | 西北崖台 |
| 7 | (0, 15.15625) | 中（平原） | 0 | 紅母（復活優先 1） |
| 8 | (6.5625, 11.3671875) | 中 | 0 | 紅母（優先 3） |
| 9 | (13.125, 7.578125) | 中 | 0 | |
| 10 | (13.125, 0) | 中 | 0 | |
| 11 | (13.125, −7.578125) | 中 | 0 | |
| 12 | (6.5625, −11.3671875) | 中 | 0 | 藍母（優先 2） |
| 13 | (0, −15.15625) | 中 | 0 | 藍母（優先 1） |
| 14 | (−6.5625, −11.3671875) | 中 | 0 | 藍母（優先 3） |
| 15 | (−13.125, −7.578125) | 中 | 0 | |
| 16 | (−13.125, 0) | 中 | 0 | |
| 17 | (−13.125, 7.578125) | 中 | 0 | |
| 18 | (−6.5625, 11.3671875) | 中 | 0 | 紅母（優先 2） |
| 棋盤外 | 40×40 場地內其餘區域 | 中 | 0 | 內圈 0～6 被外圈完全包住，棋盤外只與中層相接，不需要崖壁 |

腳本複核：南北鏡像（z→−z，1↔4、2↔3、5↔6、7↔13、8↔12、9↔11、14↔18、15↔17）層別與斜坡一致；180° 旋轉下斜坡與地熱點一致。

### 2.2 6 處斜坡（【解讀】寬 3.5m、長 4m）

**使用者 2026-09-30 裁定（工程）**：斜坡資料結構支援**任意落差**（每條斜坡的高度由兩端塊的層高決定，坡度＝落差／長度，不寫死 1m），讓日後改丁案（崖台接谷底、落差 2m）只需改資料。本批 6 條都是落差 1m、坡度 0.25。**r1（審稿 M-3）**：V14-A09 的門檻維持字面 0.25×格心距＋1e-5（本批 6 條坡度都是 0.25；期望值不得讀規格欄位，「該處坡度」在跨斜坡端的格子對上也無定義）；任意落差改由 **V14-A22**（落差 2m 的測試斜坡）驗收。`CanyonTerrainSpec` 須有公開建構子 `CanyonTerrainSpec(CaptureBoardSpec board, float[] tileHeights, int[] rampLowTiles, int[] rampHighTiles)`（斜坡寬長讀 `CanyonTuning`），`CanyonTerrainSpec.V0140` 以同一建構子建出。

斜坡＝一個矩形坡道：中心在兩塊共用邊的中點，長軸沿兩塊塔心連線（因此「塔心到塔心」的直線正好走在坡道中軸上），跨邊各 2m；寬 3.5m（共用邊長 4.375m，兩端各留 0.4375m 崖壁）。高度沿長軸線性內插；坡度 1m／4m＝0.25（14.04°），遠小於 `HeroLocomotion.ApplyDisplacement` 的「地面」判準 `normal.y > 0.7`（45.6°）。

| 斜坡 | 低端塊 → 高端塊 | 中心 (x, z) | 長軸單位向量（低→高，建構時由兩塔心算） | 高度（低端→高端） |
|---|---|---|---|---|
| R0 9→2 | 9 → 2 | (9.84375, 5.68359375) | ≈(−0.86601, −0.50002) | 0 → +1.0 |
| R1 11→3 | 11 → 3 | (9.84375, −5.68359375) | ≈(−0.86601, +0.50002) | 0 → +1.0 |
| R2 15→5 | 15 → 5 | (−9.84375, −5.68359375) | ≈(+0.86601, +0.50002) | 0 → +1.0 |
| R3 17→6 | 17 → 6 | (−9.84375, 5.68359375) | ≈(+0.86601, −0.50002) | 0 → +1.0 |
| R4 7→1 | 1 → 7 | (0, 11.3671875) | (0, +1) | −1.0 → 0 |
| R5 13→4 | 4 → 13 | (0, −11.3671875) | (0, −1) | −1.0 → 0 |

寬度取捨（腳本實測，0.35m 外擴後的格點淨寬）：寬 3.0m 時 4 條崖台斜坡最窄只剩 **1.15m**（斜向 30° 的量化損失），3.5m 時 **1.72m**；峽谷斜坡（軸向對齊格線）兩者都是 1.99m。取 3.5m。

### 2.3 崖壁

**規則（唯一定義）**：高度場不連續的地方就是崖壁；連續的地方就可走。具體為 44 段線段（腳本逐段列出）：

- **20 段完整崖壁**：相鄰兩塊層別不同、且不是斜坡的共用邊——0|2、0|3、0|5、0|6、1|2、1|6、1|8、1|18、4|3、4|5、4|12、4|14、2|8、2|10、3|10、3|12、5|14、5|16、6|16、6|18。其中谷底對崖台（0|2、0|3、0|5、0|6、1|2、1|6、4|3、4|5）落差 2.0m，其餘 1.0m。
- **12 段斜坡邊端**：6 條斜坡邊各剩兩端 0.4375m。
- **12 段斜坡側牆**：每條坡道兩條長邊（坡面與旁邊地面高度不同，只有兩個短邊的端點處連續）。

同層相鄰的邊（例如 0|1、0|4、2|3、5|6、外圈互鄰）都沒有崖壁。腳本的高度連續不變量（相鄰可走格的高度差 ≤ 0.25×格距）在上述 44 段蓋格後違反 **0 組**。

### 2.4 地熱點（【解讀】座標由腳本搜尋、格子不 Blocked、離光圈有餘裕）

| 點 | 踏點圓心 (x, z) | 踏點半徑 | 所在塊 | 落點 (x, z) | 落點塊／高度 | 備註 |
|---|---|---:|---|---|---|---|
| 東北 G0 | (2.09375, 2.0625) | 0.375 | 0（谷底） | (4.59375, 2.0703125) | 2／+1.0 | 距 0 號塔心 2.9390，踏點圓與核心／0 號佔塔圈相隔 0.0640m；落點在 2 號光圈外 0.1134m、距最近崖壁線段 1.2246m（r1 C# 重算，`refcs-r1-output.txt` M-10 節；草案原寫 0.8457，已更正） |
| 西南 G1 | (−2.09375, −2.0625) | 0.375 | 0 | (−4.59375, −2.0703125) | 5／+1.0 | G0 的 180° 旋轉 |

兩個踏點格、落點格在重建格點上都不是 Blocked，藍出生點與紅出生點都走得到踏點。示意圖把星號畫在 0 號塊靠 2／5 號那側，此處照辦。

### 2.5 全圖可達性（腳本實算，`v0140-reachability.out.txt`）

可走邊＝同層相鄰＋6 條斜坡＝**22 條**（原 42 條，20 條變崖壁）。

**從藍母 13 出發**（BFS；同步數取索引小者為父；「平面」＝原 42 條邊的最短中心連線長）：

| 塊 | 層 | 步數 | 路徑 | 經過的斜坡 | 地形後(m) | 平面(m) | 倍率 |
|---|---|---:|---|---|---:|---:|---:|
| 0 | 低 | 2 | 13-4-0 | 13→4 | 15.16 | 15.16 | 1.00 |
| 1 | 低 | 3 | 13-4-0-1 | 13→4 | 22.73 | 22.73 | 1.00 |
| 2 | 高 | 4 | 13-12-11-3-2 | 11→3 | 30.31 | 22.73 | 1.33 |
| 3 | 高 | 3 | 13-12-11-3 | 11→3 | 22.73 | 15.16 | 1.50 |
| 4 | 低 | 1 | 13-4 | 13→4 | 7.58 | 7.58 | 1.00 |
| 5 | 高 | 3 | 13-14-15-5 | 15→5 | 22.73 | 15.16 | 1.50 |
| 6 | 高 | 4 | 13-14-15-5-6 | 15→5 | 30.31 | 22.73 | 1.33 |
| 7 | 中 | 4 | 13-4-0-1-7 | 13→4, 1→7 | 30.31 | 30.31 | 1.00 |
| 8 | 中 | 5 | 13-4-0-1-7-8 | 13→4, 1→7 | 37.89 | 30.31 | 1.25 |
| 9 | 中 | 4 | 13-12-11-10-9 | — | 30.31 | 30.31 | 1.00 |
| 10 | 中 | 3 | 13-12-11-10 | — | 22.73 | 22.73 | 1.00 |
| 11 | 中 | 2 | 13-12-11 | — | 15.16 | 15.16 | 1.00 |
| 12 | 中 | 1 | 13-12 | — | 7.58 | 7.58 | 1.00 |
| 13 | 中 | 0 | 13 | — | 0 | 0 | 1.00 |
| 14 | 中 | 1 | 13-14 | — | 7.58 | 7.58 | 1.00 |
| 15 | 中 | 2 | 13-14-15 | — | 15.16 | 15.16 | 1.00 |
| 16 | 中 | 3 | 13-14-15-16 | — | 22.73 | 22.73 | 1.00 |
| 17 | 中 | 4 | 13-14-15-16-17 | — | 30.31 | 30.31 | 1.00 |
| 18 | 中 | 5 | 13-4-0-1-7-18 | 13→4, 1→7 | 37.89 | 30.31 | 1.25 |

**從紅母 7 出發**：與上表南北鏡像（0：7-1-0；1：7-1；2：7-8-9-2〔9→2〕；3：7-8-9-2-3；4：7-1-0-4；5：7-18-17-6-5〔17→6〕；6：7-18-17-6；8～11：沿外圈東側；12：7-1-0-4-13-12（與 7-8-9-10-11-12 同步數同長度，平手規則取法不同所致，V14-A05 只驗步數不受影響）；13：7-1-0-4-13；14：7-1-0-4-13-14；15～18：沿外圈西側）。兩張表都**沒有不可達的塊**。

**格點層級**（0.5m 格、外擴 0.35m、8 鄰接不切角）：44 段崖壁新增 764 格 Blocked（另有場地外圈 316 格）；19 個塔心格全部不是 Blocked，藍、紅出生點都走得到全部 19 個塔心；全場 5320 個空格裡沒有任何一格是藍出生點走不到的（沒有被夾出來的碎片）。

**區域、入口與瓶頸結論**：

1. **沒有孤島、沒有關節點、沒有橋**（拿掉任一塊或任一條可走邊，全圖仍連通）；藍母 13 ↔ 紅母 7 的邊連通度 3（與平面棋盤相同）。
2. **每個非中層區恰好 2 個入口**：峽谷 {0,1,4} 只經 7→1、13→4；東崖台 {2,3} 只經 9→2、11→3；西崖台 {5,6} 只經 15→5、17→6。**峽谷的兩個入口正好是雙方的第一母板塊 7、13**——峽谷是兩基地間最短路（13→7 走峽谷 30.31m，走外圈 45.47m），但進出谷心（核心）一律要經過某一方的母板塊。
3. **極端繞行（單點瓶頸以外最值得注意的事）**：平面上相鄰、地形後要繞的組合——0↔2／0↔3／0↔5／0↔6：7.58m → **37.89m（5.0 倍，5.5m/s 約 6.9 秒）**；1↔2、1↔6、4↔3、4↔5：4.0 倍；兩崖台之間 2↔5、3↔6：3.5 倍（53.05m）。地熱點把谷心→2／5 的 6.9 秒縮成 0.6 秒引導，但只有單向（谷底→崖台），其他方向（崖台→谷底、谷底→3／6）沒有捷徑。
4. **斜坡通道很窄**：格點淨寬崖台斜坡 1.72m、峽谷斜坡 1.99m；一面 4m 寬的符印牆（`RuneTuning.WallWidth`）可以完全封住一條斜坡 5 秒（見 §13 Q9）。

判定：照裁定的 6 處斜坡**全圖可達、沒有不可達的塊**；是否「極不合理」屬手感判斷——第 3 點的 5 倍繞行與第 2 點「谷心只能經母板塊進出」是本版面的最大特徵，列入 §13 Q1 請使用者確認（附 2 個加斜坡的選項）。

---

## 3. 核心：谷心 (0,0)、半徑 2.5m（Q1 甲，推翻 v0.13.1 修-1）

> **主對話凍結前修訂（依 `vow-toolchain/v0140-prefreeze-tests.md` 缺口 G3／G9）**：核心位置改為**跟著棋盤規格走**，不改 `new AbyssalVanguardTuning()` 的預設值。`AbyssalVanguardTuning` 新增靜態工廠 `ForSpec(CaptureBoardSpec spec)`：`spec.Terrain != null`（`V0140Canyon`）→ (0,0)、半徑 2.5；否則回傳預設（v0.13.1：(4.375,0)、1.8）。`Phase1Bootstrap` 以目前規格取用。效果：①關地形的平地夾具（§13 Q2）完整保留 v0.13.1 行為，`AbyssalVanguardPlayTests` 與 `AbyssalVanguardLogicTests` 全部條文的**斷言一字不改、仍應全綠**（`AbyssalVanguardPlayTests` 只在 `SetUp` 新增 §8 G1 的一行呼叫）；②§13 Q3 不必刪舊條文（它測的是預設／平地，仍成立），使用者原同意刪除，現改為保留、不刪——此偏離已由使用者確認保留（§13 Q14，2026-09-30「按照建議」）；③峽谷的核心行為只由 V14-A10、B11 驗收。

- `AbyssalVanguardTuning.ForSpec(V0140Canyon)`：`CoreX = 0f`、`CoreZ = 0f`、`CoreRadius = 2.5f`；引導仍 3.5 秒（連動 `CaptureTuning.CaptureSeconds`）。`new AbyssalVanguardTuning()` 的預設值**不改**（4.375f／0f／1.8f）。核心圈與 0 號佔塔圈（圓心 (0,0)、`CaptureTuning.CircleRadius`＝2.5）**完全重合**：站在圈內同時引導核心與佔 0 號塔，受傷兩者都中斷。
- **r1（審稿 L-6）新增介面**：`AbyssalVanguardTuning.IsInCore(float x, float z)`（實例方法，`dx²+dz² ≤ CoreRadius²`，含邊界）；`AbyssalVanguardLogic` 私有的 `InsideCore` 改為呼叫它（單一事實來源，判定式不變）。V14-A10 以它斷言。
- 先鋒生成位置 `(CoreX, HeightAt(CoreX, CoreZ), CoreZ)`＝(0, −1, 0)。先鋒 5m 反擊範圍涵蓋整個 0 號塊的大部分；站在 2／3／5／6 崖台邊緣（離 (0,0) 至少 3.79＋0.37＝4.16m）仍在 5m 內，會被反擊（x/z 距離，不看高度，【解讀】同 §4.1）。
- 核心半徑不再與 `CaptureTuning.CircleRadius` 分開：v0.13.1 修-1 的「核心半徑自此不再連動 CircleRadius」維持（`ForSpec` 寫字面 2.5），不改成讀同一欄位，避免日後調佔塔圈時連帶移動核心。
- **受影響的既有測試**（r1 改寫，審稿 M-1）：因核心改為依規格取用、預設值不改，`AbyssalVanguardLogicTests` 三條（`Tuning_UsesFrozenGreyboxValuesAndCaptureCircleRadius`、`CoreCircle_OverlapsNoCaptureCircle_OnNineteenBoard`、`Core_CircleBoundaryAndUnopposedChannelClaimForBothSides`）與其餘 6 條 `TickCore` 條文**全部不改**；`AbyssalVanguardPlayTests` 第 74、146、197 行的 `(4.375f, 0f, …)` 在平地夾具下仍是核心，**不改**。草案時期的「字面值改寫／刪除舊條文」（原 §8 T1～T4）全部作廢。

---

## 4. 規則

### 4.1 崖台射程 +10%（雙方）

- **判定位置**：看**攻擊者**出手當下的地形類別；`ClassAt(攻擊者 x, z)==Cliff` 才加成。目標在哪一層不影響。斜坡（`Ramp`）不算崖台。【解讀】
- **作用對象**【解讀，§13 Q5】：只作用在「普攻射程」——藍方英雄 `HeroTuningAsset.AttackRange` 5m → 5.5m；紅方對手的出手距離 `DuelTuning.OpponentStopDistance` 1.8m → 1.98m（對手的傷害圈 `AttackRadius` 1.5m 是落在英雄腳下的閃避圈，不是射程，不變）。元素（施放距離 3m、火浪 6m）與符印（極速 4m、拖曳 1.2～8m）不加成。
- **計算式**：`range × (100 + CliffRangePercent) / 100f`，`CliffRangePercent = 10`（整數，放 `CanyonTuning`）。5×110/100＝5.5 在 float32 精確；邊界測試用二進位有限小數（V14-A11）。
- **介面（r1，審稿 H-1）**：`CanyonRules.AttackRange(float baseRange, float ax, float az, ITerrainQuery terrain)` 回傳攻擊者所在位置的射程（`terrain==null` → `baseRange`）；`CanyonRules.InAttackRange(float baseRange, float ax, float az, float tx, float tz, ITerrainQuery terrain)` ＝ `(tx−ax)²+(tz−az)² ≤ AttackRange(baseRange, ax, az, terrain)²`（含邊界）。`HeroController.IsTargetInAttackRange` 與 `TrainingOpponent` 的停步判定都改呼叫 `InAttackRange`，不在呼叫端另算距離。
- 距離一律水平（`offset.y = 0`，沿用 `HeroController.IsTargetInAttackRange`），高度差不影響射程。崖壁不擋攻擊（`TrainingOpponent.HasBlockingWall` 只認符印牆／測試牆，維持）。

### 4.2 局部視野 6→8m

- 【解讀】只有**站在崖台上的觀看者**局部視野是 8m；其他位置維持 6m（`CaptureVisibilityLogic.LocalVisionRadius`）。對齊紀錄寫在「站崖台射程 +10%」同一句，解讀為崖台專屬。

### 4.3 谷底看不到崖台上的敵人，與 v0.12 迷霧的合成（【解讀】順序）

`CanSee(match, tracker, targetUnit, viewerSide, viewer, viewerKO, target)`（r1 介面見 §4.7 末「介面」）：

1. 不適用迷霧（非 Active、非正式規則集、`FogEnabled==false`）→ 看得到。
1.5 **目標處於「開火顯形」且顯形對象是觀看方 → 看得到**（§4.7；蓋過下面所有「看不到」的規則，含倒地、谷底看崖台、距離）。
2. **目標所在塊由觀看方持有（真視野）→ 看得到**（先於一切地形規則，對應「除非該塊己方真視野」）。
3. 觀看者倒地 → 看不到。
4. **觀看者 `ClassAt==Canyon` 且目標 `ClassAt==Cliff` → 看不到**（不論距離）。
5. 距離 ≤ 半徑 → 看得到；半徑＝觀看者 `ClassAt==Cliff` ? 8 : 6。

- 只限制「谷底看崖台」這一個方向；崖台看谷底、平原看崖台、谷底看平原都照距離（第 5 步）。斜坡上的觀看者與目標都不算谷底／崖台。
- 迷霧合成的所有下游（`Phase1Bootstrap` 的顯隱、`CaptureBoardView` 板塊霧、`RageAuraView`、`HeroController` 點選、`PlayerInputService` 點擊、`TrainingOpponent.CanSeeHero`）都只呼叫 `CanSee`／`HasTrueVision`，**不在下游另寫地形判斷**（單一事實來源；§6.1-7 分母＝`grep -rn "CaptureVisibilityLogic\." Assets/Scripts` 起草時 11 處（凍結前複查為 15 行，清單見 `v0140-prefreeze-tests.md`；B 步以 15 為分母），全部經過這兩個函式）。
- `AppliesTo` 目前寫死 `ReferenceEquals(match.Spec, CaptureBoardSpec.V0100Sanctuary)`：正式規則集換成 `V0140Canyon` 後迷霧會整個消失。改為讀規格旗標 `Spec.FogEnabled`（`V0100Sanctuary`、`V0140Canyon` 為 true，舊兩套夾具 false）。

### 4.4 谷底淺水：水元素半徑 +1m

- 【解讀】看**水域圓心**的地形類別：`ClassAt(圓心)==Canyon` → 半徑＋`CanyonWaterBonusMeters`（1.0）。與天賦「潮汐牽引」的＋2m **相加**（谷底＋潮汐＝3＋1＋2＝6m）。斜坡上不算谷底。雙方的水域都適用（地形規則，不分陣營）。
- 水域仍是 x/z 平面圓（圓心在谷底、半徑 4m 時會覆蓋到崖台邊緣的 x/z 範圍，崖台上的目標也受影響）【解讀，§13 Q6】。
- 分母：起草時水域半徑有 2 個入口——`Phase1Bootstrap.cs:1089`（`WaterRadius + 潮汐`）與 `ElementField.cs:213`（`CastWater(groundPoint, factionId, _tuning.WaterRadius)`）。兩處都改為呼叫同一個純邏輯函式 `CanyonRules.WaterRadius(baseRadius, talentBonus, x, z, terrain)`；B 步開工時重跑 `grep -rn "WaterRadius" Assets/Scripts` 確認分母。

### 4.5 地熱點

| 項目 | 規則 |
|---|---|
| 觸發者 | 【解讀，§13 Q7】只有**玩家操控的英雄**（本版單人模式＝藍方英雄；判準寫成「玩家操控」而非「藍方」，日後雙人對戰兩邊玩家都能用，不必改規則）。AI 對手、先鋒、巨獸站上去都不引導（落實「紅方 AI 不用地熱點」：不只是 AI 不去找，而是站上去也不會被彈上去——否則追擊時誤踩就會違反「只走斜坡」）。 |
| 引導 | 英雄中心在踏點圓內（含邊界）、存活、未在移動鎖定、對局 Active、該點不在冷卻 → 每 tick 累加引導時間；連續滿 **0.6 秒**（`≥`）當 tick 發射。 |
| 中斷 | 受傷（`NotifyDamaged`）→ 引導歸 0，**且下一個 tick 不累加**（沿用 `AbyssalVanguardLogic` 核心的「受傷清進度並跳過下一 tick」模式）；離開踏點、倒地 → 歸 0。中斷不觸發冷卻。 |
| 冷卻 | **每點各自** 8 秒，從**發射那個 tick** 起算；冷卻中站上去不累加。 |
| 發射 | 【解讀】發射後進入 0.5 秒的拋物線飛行：水平位置由踏點圓心線性移到落點、高度 `lerp(−1, +1) + 4·1.0·t(1−t)`（頂點高出 1m）；飛行中英雄不能下指令、可以被攻擊但不打斷飛行；落地後 `TerrainLayer=0`、y＝`HeightAt(落點)`。對手在飛行中失去目標的判定照 §4.3（英雄在飛行中的 `ClassAt` 以水平位置計算）。 |
| 落點被佔 | 落點被符印牆蓋住時，落地後照既有 `EjectFromBox` 推到最近空格，但限定「與落點同一樓地板」（§5.2、`IsSameFloor`），不會被推回谷底。 |
| 重開局 | 第二局兩點冷卻與引導全部歸 0。Off／Lobby／Ended 不引導。 |
| **介面（r1，審稿 M-7／L-6）** | `GeothermalVentLogic(CanyonTerrainSpec terrain, CanyonTuning tuning)`（踏點與落點從 terrain 讀，固定 2 點、零配置）；`void Tick(float dt, float heroX, float heroZ, bool heroCanTrigger)`；`void NotifyDamaged()`；`void Reset()`；唯讀 `LaunchCount`、`LastLaunchPad`、`LastLandingX`、`LastLandingZ`、`Progress(int pad)`、`Cooldown(int pad)`。**邏輯層不追蹤飛行、存活與對局狀態**：`heroCanTrigger`＝「玩家英雄存活 && 未在移動鎖定（含地熱點飛行） && 對局 Active」，由呼叫端（`Phase1Bootstrap`）每 tick 算好傳入；`heroCanTrigger==false` 等同 ④ 的「不可觸發」（`progress = 0`）。飛行 0.5 秒的鎖定只在 `HeroLocomotion`（§7.3）。發射時呼叫端清除英雄既有的移動目的地（落地後停在落點，與 V14-B07 ④ 一致；r1 定義補寫，非規則改動）。 |
| **Tick 順序**（V14-A14 的字面值依此推導，凍結） | 對每一點依序：① `cooldown = max(0, cooldown − dt)`；② `cooldown > 0` → `progress = 0`，本點結束；③ 若本 tick 有待處理的受傷旗標 → `progress = 0`、清旗標，本點結束；④ 英雄可觸發且在圓內 → `progress += dt`，否則 `progress = 0`；⑤ `progress ≥ VentChannelSeconds` → 發射、`progress = 0`、`cooldown = VentCooldownSeconds`。兩點依索引 0、1 處理；同一 tick 至多一點發射（英雄只能在一個圓內，兩圓相距 5.9m）。 |

### 4.7 開火顯形（使用者 2026-09-30 裁定，新規則）

- **觸發**：任一單位的普攻或技能（元素、符印牆撞擊、裂風矢、巨獸攻擊）對**敵方英雄／對手**實際造成傷害（扣血 > 0 的那一次受擊）時，攻擊者對**受害者所屬陣營**顯形 `RevealSeconds = 1.5f`（放 `CanyonTuning`；與 GDD 蒸氣迷霧受擊顯影同長）。先鋒、巨獸被打不算（中立／建物不觸發）。
- **計時**：每個可被顯形的單位（藍英雄、紅對手、雙方巨獸）各一個 `RevealRemaining`；再次命中重設為 1.5（不累加）；每 tick 減 dt、夾 0；`> 0` 時顯形。第二局歸 0。
- **作用範圍**：只在迷霧適用時有意義（§4.3 第 1 步不適用就本來全可見）；`V0100Sanctuary` 夾具也適用（它有迷霧），但夾具的既有 EditMode 條文不觸發命中，不受影響。
- **單一來源**：純邏輯 `RevealTracker`（零配置、固定容量），`CanSee` 第 1.5 步讀它；Unity 端在傷害入口（`TakeDuelDamage`／`ReceiveDamage` 的扣血成功分支）呼叫 `NotifyHit`（簽章見下方「介面」）。B 步開工先數分母：`grep -rn "TakeDuelDamage\|ReceiveDamage" Assets/Scripts` 逐一標明是否經過 `NotifyHit`。
- **AI 反應**：紅方 AI 的 `CanSeeHero` 讀 `CanSee`，顯形期間照既有追打規則追擊（甲案要走斜坡繞路才上得了崖台）；不另寫 AI 規則。

> **主對話凍結前修訂（缺口 G4）**：`TakeDuelDamage` 沒有攻擊者參數、元素傷害的 instigator 為 null。規則改寫為**以陣營歸屬**：`NotifyHit(attackerSide, victimSide)` 讓「攻擊方陣營的英雄／對手」顯形；元素、符印牆撞擊的攻擊方＝該區域／牆的 `factionId`；巨獸打人時顯形的是巨獸本身（`NotifyHit` 另一多載帶單位 id）。B 步開工逐一標明 15 個實際造成傷害的呼叫點（`v0140-prefreeze-tests.md` §2）各自如何取得 attackerSide，漏一處即 V14-B16 範圍不完整。

> **介面（r1，審稿 M-6；寫死，實作與 V14-A20 照此）**：
> ```csharp
> public enum RevealUnit { BlueHero = 0, RedOpponent = 1, BlueBehemoth = 2, RedBehemoth = 3 }   // 固定 4 格
> public sealed class RevealTracker          // Vow.Core.Logic，零配置
> {
>     public RevealTracker(float revealSeconds);                // 本批傳 CanyonTuning.RevealSeconds＝1.5f
>     public void NotifyHit(int attackerSide, int victimSide);   // 顯形「attackerSide 的英雄／對手」（藍→BlueHero、紅→RedOpponent），顯形對象＝victimSide
>     public void NotifyUnitHit(RevealUnit unit, int victimSide);// 巨獸打人
>     public void Tick(float dt);                                // 各格 remaining −= dt、夾 0
>     public bool IsRevealedTo(RevealUnit unit, int viewerSide); // remaining > 0 && viewerSide == 該格的顯形對象
>     public float Remaining(RevealUnit unit);
>     public void Reset();
> }
> // CaptureVisibilityLogic 新多載（第 1.5 步；tracker==null 時跳過 1.5 步）：
> public static bool CanSee(CaptureMatchLogic match, RevealTracker tracker, RevealUnit targetUnit, int viewerSide,
>     float viewerX, float viewerZ, bool viewerKnockedOut, float targetX, float targetZ);
> // 既有多載 CanSee(match, viewerSide, viewerX, viewerZ, viewerKnockedOut, targetX, targetZ) 保留，
> // 等同 tracker==null 的新多載（地形規則照樣由 match.Spec.Terrain 決定），T7 不改即可編譯。
> ```
> `RevealTracker` 由 `Phase1Bootstrap` 持有並傳入（`CaptureMatchLogic` 屬 V14-A18 零改動檔，不放進 match）。

### 4.6 不變的規則（【解讀】）

- **歸屬連通**（v0.9.0 BFS 斷能、包夾、圍城）照舊用原 42 條邊：崖壁擋的是「走」，不是「地脈」。若改成走路圖（22 條），7／13 以外的母板塊連通與斷能結果會大幅改變，屬策略改動，本批不做。
- 佔塔圈、核心圈、聖所判定只看 x/z（本來就是）。

---

## 5. 高度：−1.0／0／+1.0，各實體怎麼處理

### 5.1 單一來源

`CanyonTerrainSpec : ITerrainQuery`（純邏輯、零 UnityEngine、建構後零配置）是所有地形事實的唯一來源：層別、斜坡矩形、地熱點、`HeightAt`、`ClassAt`、`WalkNeighbor`（板塊層級可走鄰接）、崖壁線段清單（`CliffSegmentCount`／`GetCliffSegment(i, out x0, out z0, out x1, out z1)`）。蓋格（§7.2）、場景的崖壁碰撞體與地形碰撞體（§9-B）、巨獸路線、可達性測試**全部從它讀**，不得另外手抄座標。

`HeightAt(x, z, 0)`：①落在某條斜坡矩形內（|沿軸| ≤ 2、|橫向| ≤ 1.75，含邊界；多條重疊取索引小，實際不重疊）→ 沿軸線性內插；②否則 `TileAt(x,z)` 的層高度；③棋盤外 → 0。`ClassAt`：斜坡內 → `Ramp`；否則依所在塊層別 `Canyon`／`Cliff`／`Plain`；棋盤外 `Plain`。

### 5.2 各實體

| 實體／系統 | 本批處理 |
|---|---|
| 英雄、紅方對手（共用 `HeroLocomotion`） | 水平位移照舊（`ApplyDisplacement` 的 SphereCast 裁切＋格點繞路）；**每次改動水平位置後** y＝`HeightAt(x,z,TerrainLayer)`。會改位置的入口（起草時清點，B 步開工重數）：`Step` 走路、`MicroCadenceMover` 滑步（兩者經 `ApplyDisplacement`）、`EjectFromBox`、`WarpTo`、`SyncAgent`、`HeroController.ResetForDuel`、`TrainingOpponent.RespawnAt`、地熱點飛行（新）。y 更新集中在 `HeroLocomotion` 一個私有方法，其他入口呼叫它。`_terrain == null`（Off 模式）時 y 不動，行為與 v0.13.1 逐行相同。 |
| `EjectFromBox`（被牆壓到時推出） | 最近空格搜尋加上「`IsSameFloor`」條件（**使用者 2026-09-30 裁定改寫**：兩點都不在崖壁格，且水平線段 p0→p1 `CrossesCliff==false`——與 §9 共同遵守的穿崖判準同一個函式；不再用坡度門檻，因為「高度差 ≤ 0.25×距離」在相距 ≥ 8m 時會把谷底與崖台誤判為同層，日後陡坡更嚴重）。否則谷底貼崖壁被牆壓到時，最近的空格可能在崖台上，等於穿崖。**「崖壁格」定義（r1，審稿 M-4；純幾何、與格點無關）**：點到 `CanyonTerrainSpec` 任一崖壁線段（有限線段）的水平距離 ≤ 0.35（＝`NavGridTuning.BodyRadius`，與崖壁蓋格的外擴量相同）即為在崖壁格；**不得**用 `BlockGrid.IsBlocked` 判斷（格點無法區分崖壁引用與牆引用，推出時英雄所在格必被牆蓋住）。有條件搜尋為 `BlockGrid` **新增**方法 `TryFindNearestFreeSameFloor(float x, float z, int maxRadiusCells, ITerrainQuery terrain, int layer, out int cx, out int cz)`：掃描順序與比較式和 `TryFindNearestFree` 逐行相同，只多跳過 `!terrain.IsSameFloor(x, z, layer, 格心x, 格心z, layer)` 的候選；零配置。 |
| NavMeshAgent | **不重烘**：維持 v0.13.1 的平地 NavMesh，只用它的水平 `desiredVelocity`；崖壁靠 `BlockGrid` 蓋格讓 `GridNavigator` 判「沒有視線」而改走整合場（和符印牆同一套機制）。`SyncAgent` 已經把 y 從 agent 讀回時覆寫成角色自己的 y（`HeroLocomotion.cs` 的 `clamped.y = _self.position.y`），B 步要實測 y 偏離 NavMesh ±1m 時 `isOnNavMesh` 與 `nextPosition` 夾回行為不變（V14-B03 探針）。 |
| 崖壁實體 | 每段崖壁線段一個 `BoxCollider`（厚 0.1m、y 從 −2 到 +4，放 Ignore Raycast 層：擋身體 SphereCast〔`Physics.AllLayers`〕、不擋點地射線〔`DefaultRaycastLayers`〕）。沒有它，從高處往低處走時 SphereCast（高度 +0.9）掃不到下方的地形塊，會直接走下崖。 |
| 地形碰撞體（點地與地面） | 19 塊各用 3 個旋轉 90°／±60° 的 `BoxCollider`（R×2h 矩形三個一組＝正六角，ARCHITECTURE §壹「碰撞器一律 Primitive」），頂面在該層高度、底在 −2；6 條斜坡各一個傾斜 `BoxCollider`；棋盤外 4 塊長條（頂面 0）。全在 Default 層。進入正式佔領局時啟用，Off 模式停用並恢復平地 `Ground_40x40` 的碰撞體（§13 Q2）。**已知灰盒限制（r1，審稿 M-12，接受不修）**：高端塊的六角盒頂面在該層高度，會蓋住斜坡伸進高端塊的那一半（交界處最多高出坡面 0.5m）；點地射線點在斜坡上半段會打到盒頂而非坡面（高度誤差 ≤0.5m、52° 俯角下水平誤差 ≤0.39m）。身體位移（r2 依審稿 N5 改為掃掠推導；**只適用於有地面放行 `normal.y>0.7` 的 `HeroLocomotion`**，巨獸另見巨獸列）：英雄 SphereCast 球心高出腳底 0.9、半徑 0.35；腳底在交界前水平距離 d 處時，球心比高端盒頂邊緣高 g＝0.4−0.25d。水平掃掠只有在 g < 0.35（d > 0.2）且掃掠長度到得了邊緣時才會接觸，接觸法線 y 分量＝g／0.35。g ≥ 0.245（d ≤ 0.62）時法線 y ≥ 0.7，被 `ApplyDisplacement` 當地面放行。1/60 下單步 0.092＋skin，第一次接觸發生在 d ≤ 0.35 附近（g ≥ 0.31，法線 y ≥ 0.89）→ 放行；只有單步 ≥ 約 0.37m（低幀率）時才可能在 d > 0.62 處以法線 y < 0.7 接觸而被裁切一步，裁切後 d ≤ 0.35，下一步即放行，不會卡死（V14-B05(a) 的 45 幀窗口對 C# 名目 18／24 幀留有裕度）。本批 y 一律由 `HeightAt` 決定。V14-D08 未驗證清單須列「斜坡上半段的點地落點誤差」；本批沒有條文點擊斜坡上半段。 |
| 點地射線 | 沿用 `PlayerInputService` 的 `Physics.RaycastNonAlloc(…, DefaultRaycastLayers)`，打到地形頂面／坡面拿到正確的 (x,y,z)。平地 `Ground_40x40` 在峽谷模式必須停用，否則點谷底會先打到 y＝0 的平面（52° 俯角下水平誤差 1/tan52°＝0.78m）。 |
| 先鋒（中立） | 生成於 (0, −1, 0)；不移動。 |
| 巨獸 | 路線 BFS 改用 `WalkNeighbor`（原本用 `CaptureBoardSpec.Neighbor`，會規劃出 0→2 這種崖壁邊）；沿路 y＝`HeightAt`；位移 SphereCast（半徑 0.48、高度 +1）被崖壁碰撞體擋住。**實作要求（r2，審稿 N1；原計畫漏寫）**：巨獸的位移裁切（`AbyssalVanguardTarget` 的 `SphereCastNonAlloc` 迴圈）**必須略過 `CanyonTerrain` 根物件底下的命中**——高度由 `HeightAt` 決定，水平阻擋只來自 `CliffBarriers`、牆與單位。理由：現行裁切不過濾地面法線，巨獸沿 R4／R5 上坡時球心比高端盒頂邊緣高 g＝0.5−0.25d，d > 0.08 就會碰到盒頂邊緣並把 step 裁成 0，永久卡在交界（d≈0.22）。照此實作後 V14-B11 可通過的依據（r3 依審稿 NEW-3 改寫為實際路徑）：r3 覆審以 `MoveAlongRoute` 逐幀模擬實際路徑（換段條件＝距塔心 < 0.6，不是走到塔心；含 `GridNavigator` 的 Follow 段）——案 1 藍 0→7 路線 [1,7]：294 幀到終點、實際路徑最近崖壁 1.750、Follow 0 幀；紅 0→13 [4,13]：294 幀、1.750；案 2 藍 2→7 [9,8,7]：432 幀到終點、第 76 幀首次 `TileAt==9`、實際路徑最近崖壁 0.780（Direct 316／Follow 113 幀）；全程裁切 0 幀、穿崖 false（`v0140-plan-review-r3.md` §四，重建模型、對手與牆不在場）。最近值都大於半徑 0.48＋崖壁碰撞體半厚 0.05＝0.53；另 C# r2 的塔心連線距離（≥1.75／≥0.674）只作粗估；地形盒不再參與裁切，上坡不卡。B11 的幀數與斷言不放寬。 |
| 符印牆 | 【解讀，§13 Q8】牆中心 y＝`HeightAt(中心)`；跨崖壁時照樣成牆（不拒絕施放）；牆的碰撞盒高度不變（2m），落在崖台邊時下緣在崖頂。格點蓋格照舊（崖壁格本來就 Blocked，重疊用引用計數）。 |
| 元素區域 | 圓心取點地命中點（已含 y）；判定照舊只看 x/z；顯示貼在 `HeightAt(圓心)`。**主對話凍結前修訂（缺口 G6）**：`ElementField` 以新增的 `SetTerrain(ITerrainQuery)` 取得地形（預設 null＝無淺水加成），不改建構子與既有公開簽章，避免 `PactDamagePlayTests` 等既有測試編譯失敗。 |
| 佔塔圈、核心圈、聖所 | 判定只看 x/z（不變）；光圈、進度盤、塔的顯示位移到所在塊高度。**主對話凍結前修訂（缺口 G5）**：核心圈沿用既有 `Vanguard_CoreCircle` 物件，位置與縮放於進入模式時依 `ForSpec` 設定，**不新增 Renderer**（守住 V9_B01 的 Renderer 計數）；新增的地形、斜坡、地熱圓盤 Renderer 只掛在 `CanyonTerrain` 根物件下，B 步回報列出 V9_B01 計數前後。**主對話審稿補**：①每條斜坡兩端各伸進相鄰兩塊的光圈 0.711m（斜坡端點距塔心 1.789m < 2.5m），光圈平貼在塊高度時，坡面那一小段會高出或低於光圈最多 0.178m（低端塊光圈被坡面蓋住、高端塊光圈浮在坡面上）；站在坡面那段照 x/z 算在圈內（判定不變）。灰盒接受，D04 截圖要看得到光圈，§25 未驗證清單列「光圈與坡面交界的顯示」。②核心圈與 0 號佔塔圈同心同半徑，兩個圓盤要錯開顯示高度，避免 z-fighting 閃爍（D04 目視）。 |
| 裂風矢貫穿射線（`HeroController.cs:427`） | 【解讀】起點 +0.9、方向改為指向目標的 +0.9（含 y 分量），否則谷底往崖台射時水平射線從目標腳下穿過。 |
| 對手遮擋射線（`TrainingOpponent.cs:510`） | 射線緩衝滿時直接判「被擋」；地形碰撞體會多佔命中數。B 步要實測谷底對崖台出手時命中數 < 緩衝大小（或把地形碰撞體排除在這條射線的 LayerMask 外）。 |
| 鏡頭 | `FollowCameraRig` 跟隨目標含 y，谷底時鏡頭一起下降 1m；52°／17m／FOV 40 不變。 |
| 佔領待機（Lobby）的英雄與對手位置（r1 審稿 M-13；r2 N3；r3 依使用者裁定「甲」改寫） | 現行 `_spawn`＝(−4,8) 在峽谷模式落在 1 號谷底、距 1\|18 崖壁線段只有 0.1138m（在 0.35 外擴帶內，膠囊與崖壁碰撞體相交；C# r1）。**規則（使用者 2026-09-30 裁定「甲」）**：峽谷規格（`spec.Terrain != null`）下，**進入佔領模式**（Off→佔領，`TryEnterCaptureMode` 成功後的 `HandleCaptureButton` 分支）與**結算後回 Lobby** 兩個入口的當幀：英雄傳送到藍出生點 (0, 0, −16.65625)（13 號平原、距最近崖壁線段 3.7256m）；對手傳送到 `CanyonLobbyOpponentSpawn`＝**(4, 0, −12.5)**（12 號藍母平原、英雄前方偏右 5.768m、距最近崖壁線段 2.1374m、不在任何斜坡矩形內）；兩者 y 經 §5.2 英雄／對手列的 y 更新（＝`HeightAt`＝0）。點對手開局後，雙方照原本開局流程（`StartCapture` 的 `ResetForDuel`／`RespawnAt` 母塊復活點）行動，Lobby 位置不影響對局。`Terrain == null` 時兩個入口都維持 v0.13.1（對手 `_spawn`、英雄不傳送；V9_C01 不受影響）。常數放 `CanyonTuning`。**可點性驗證（C# r3，`v0140-refcs/refcs-r3-output.txt`，`dotnet run -c Release -- r3`）**：鏡頭依 `FollowCameraRig`（pitch 52°、yaw 0、距離 17）跟隨英雄＝(0, 13.3962, −27.1225)，垂直 FOV 40、844×390；對手點擊點＝對手位置＋up＝(4,1,−12.5) → 螢幕投影 **(536.2, 306.1)**（原點左下，深度 18.771），離四邊最小 84px ≥ 16px。（r2 的 (−4,0,12) 離英雄 28.9m、投影 y＝562 在畫面外，開局點不到對手，r3 審稿 NEW-1，已廢。）投影點與 HUD 按鈕是否重疊不在 C# 模型內，由 V14-C04「真實點對手→開局」斷言在 Unity 端把關。由 V14-C04 驗。 |

§6.1-7 分母（B 步開工時必做，寫進回報）：①`grep -rn "Physics\.\(Raycast\|SphereCast\|OverlapSphere\|OverlapBox\|Linecast\|CapsuleCast\)" Assets/Scripts`（起草時 6 個呼叫點：`AbyssalVanguardTarget:271`、`Projectile:99`、`TrainingOpponent:510`、`HeroController:427`、`HeroLocomotion:453`、`PlayerInputService:387`）逐一判定是否受高度影響；②`grep -rn "\.y = 0f\|, 0f, " Assets/Scripts/{Combat,Bootstrap,Core}`（起草時 52 行；凍結前複查 58 行：世界位置 5、水平方向 36、其他 17）逐行分類為「水平方向向量（不用改）」或「世界位置（改讀 HeightAt）」，回報兩類各幾行；③會改英雄／對手／巨獸 `transform.position` 的入口（`grep -rn "position = \|\.Warp(\|WarpTo(" Assets/Scripts`）逐一標明是否經過 y 更新與 `IsSameFloor`。**補掃（r1，審稿 M-14；②③ 兩條 grep 有盲區，見 `v0140-prefreeze-tests.md` §2 ④⑤）**：④`grep -rn "new Vector3(" Assets/Scripts/{Combat,Bootstrap,Core}` 逐行分類為「世界位置（改讀 HeightAt）／水平方向／常數偏移（如 `0.03f` 貼地）／其他」，已知盲區至少含 `TrainingOpponent.cs:446`、`:475`、`RageAuraView.cs:78`、`ElementZoneView.cs:48`、`CombatFeedbackService.cs:134`；⑤`grep -rn "_self.position\|clamped" Assets/Scripts/Core/HeroLocomotion.cs` 逐一標明（`EjectFromBox`、`SyncAgent`、`ApplyDisplacement` 尾端）。回報寫出 ①～⑤ 各自的 N 與分類。

---

## 6. 紅方 AI

> **主對話凍結前修訂（缺口 G2、G7）**：走路距離選塔與巨獸走路圖**只在 `spec.Terrain != null` 時啟用**；`Terrain == null`（平地夾具、舊規格）一律沿用 v0.13.1：選塔直線距離、巨獸路線用 `CaptureBoardSpec.Neighbor`。`SelectTargetTile` 以新增多載帶入 `ITerrainQuery`（null＝直線），不改既有簽章。新增 **V14-A21 選塔走路距離**：`V0140Canyon`、對手在 (0,0)、只剩 2 號與 13 號兩塊可選（其餘紅方）→ 選 13（走路 15.16 vs 2 號 37.89）；同盤面 `terrain=null` → 選 2（直線 7.58 vs 15.16）。**紅燈實作**：峽谷仍用直線（選 2）；夾具也改走路（選 13）。突變 **K17**（r1 由 C17 改名，§10）：`SelectTargetTile` 忽略 terrain 一律直線 → A21 第一組紅。**起點塊（r1，審稿 M-8）**：走路距離的起點塊＝`TileAt(對手 x, z)`；`TileAt==−1`（棋盤外，例如場邊復活點）時取塔心平面距離平方最小的塊（嚴格 `<`，平手取索引小）；站在斜坡上時 `TileAt` 照常回傳所在塊（斜坡不另算）。此規則與 §12.1-1 C# 參考模型的假設逐行相同（`v0140-refcs/Sim.cs` `SelectTarget`），V14-C01 窗口不變。

- **只走斜坡**：對手移動本來就經 `HeroLocomotion`＋`GridNavigator`；崖壁蓋格後，它到任何目標只能沿整合場經斜坡繞行，不需要另寫「AI 專用路線」。
- **不用地熱點**：由 §4.5「只有藍方英雄會觸發」在結構上保證；另以 V14-B06 驗「對手站在踏點上 3 秒不會被彈上去」。
- **選目標**【解讀，§13 Q4】：`CaptureOpponentPolicy.SelectTargetTile` 目前用「到塔心的直線距離平方」。峽谷上直線最近的塊可能要繞 5 倍路（谷心 → 2 號）。建議正式規則集改用「走路距離」（`WalkNeighbor` 圖上以塔心連線長為權重的最短路，平手取索引小），舊夾具維持直線距離。
- 追打英雄的遲滯門檻（`ChaseStartDistance`／`ChaseGiveUpDistance`）與先鋒、核心的優先序不變；英雄在崖台而對手在谷底時，§4.3 讓對手看不到英雄（除非真視野），自然放棄追打。

---

## 7. 放在哪裡（工程邊界）

### 7.1 新檔（純邏輯，`Assets/Scripts/Core/Logic/`）

- `ITerrainQuery.cs`（§1.3）、`CanyonTerrainSpec.cs`（§5.1；靜態唯讀實例 `CanyonTerrainSpec.V0140`）、`CanyonTuning.cs`（`CliffRangePercent=10`、`CliffVisionRadius=8f`、`CanyonWaterBonusMeters=1f`、`VentChannelSeconds=0.6f`、`VentCooldownSeconds=8f`、`VentFlightSeconds=0.5f`、`VentPadRadius=0.375f`（二進位有限小數，邊界可精確構造）、`RampWidth=3.5f`、`RampLength=4f`、`CliffBarrierThickness=0.1f`）、`CanyonRules.cs`（`AttackRange`、`InAttackRange`、`WaterRadius`）、`GeothermalVentLogic.cs`（§4.5，固定 2 點、零配置）、`RevealTracker.cs`（§4.7 介面）。`CanyonTuning` 另含 `RevealSeconds=1.5f`、`CanyonLobbyOpponentSpawnX=4f`／`Z=-12.5f`（r3）（§5.2）。
- `CaptureBoardSpec`：新增 `Terrain`（`ITerrainQuery`，舊三套為 null）、`FogEnabled` 旗標、靜態實例 `V0140Canyon`（幾何與 `V0100Sanctuary` 逐值相等、6 個規則旗標相同、另加地形）。`V0100Sanctuary` 保留為夾具（既有 EditMode 逐字不動）。

### 7.2 崖壁蓋格

`CanyonTerrainSpec.StampCliffs(BlockGrid grid, float inflate, int delta)`：對 44 段線段逐段呼叫 `grid.StampBox(中點, 法線, 半長, 0, inflate, delta)`。進入正式佔領局（`TryEnterCaptureMode`）時 `delta=+1`、回 Off 時 `delta=−1`（引用計數，重複進出不殘留；V14-C05）。蓋格只在模式切換時發生，對局中 `BlockGrid.Version` 只因符印牆變動。

### 7.3 Unity 端改動清單（B 步）

`Phase1Bootstrap`（換 `V0140Canyon`、蓋格、地形啟停、地熱點 tick、水域半徑、射程查詢、種子入口）、`HeroLocomotion`（y 更新、`TerrainLayer`、飛行鎖定、`EjectFromBox` 同樓地板）、`HeroController`（射程、裂風矢方向）、`TrainingOpponent`（出手距離、遮擋射線）、`AbyssalVanguardTarget`（生成 y、路線圖、y 更新）、`ElementField`（水域半徑）、`CaptureBoardView`（顯示高度）、`CaptureVisibilityLogic`（§4.3）、`CaptureOpponentPolicy`（§13 Q4 定案後）、`VOWPhase1SceneBuilder`（地形碰撞體、崖壁碰撞體、斜坡、地熱點圓盤；全部由 `CanyonTerrainSpec` 產生）、`DebugHud`（地熱點冷卻讀數，灰盒字串）。

**本批新增的 editor-only 測試入口（r1，審稿 H-2／H-4／M-5；全部 `#if UNITY_EDITOR`，受 V14-D01 檢查）**：
- `Phase1Bootstrap.UseFlatCaptureSpecForTest()`：§8 G1。
- `Phase1Bootstrap.HoldOpponentForTest(bool hold)`：`hold==true` 期間 `TrainingOpponent` 的佔領 AI **不執行任何行動**——不下移動指令、不走、不放符印牆、不進入前搖；當下的移動指令清掉。**保持** `_active`（可被鎖定、可受傷），`CanSeeHero()` 照常可查詢、視野與顯形照常計算。`hold==false` 的當幀起恢復原行為。取代草案的「以種子停止移動」（草案語意未定義；`StopRound()` 會把 `_active` 設 false、連受傷都關掉，不可用）。
- `Phase1Bootstrap.SpawnRuneWallForTest(int side, float x, float z, float normalX, float normalZ)`：以真實 `RuneWall` 物件在 (x, `HeightAt`, z) 立一面牆（尺寸 4×2×0.6，`forward`＝(normalX,0,normalZ)），走與真實施放相同的 `RegisterNavBlocker`→`Stamp(+1)`→推出路徑。
- `Phase1Bootstrap.SeedBehemothForTest(int side, float x, float z)`：以與「核心引導完成」相同的程式路徑讓 side 取得巨獸（邏輯層進入 Behemoth 階段、`BehemothOwner==side`），唯一差別是巨獸物件先傳送到 (x, `HeightAt`, z) 再呼叫 `ActivateBehemoth(side)`（路線由該位置的 `TileAt` 起算，與正式路徑同一個 `BuildRoute`）。
- `AbyssalVanguardTarget.CopyRouteForTest(int[] buffer)`：回傳目前路線長度並把 `_route[0.._routeLength)` 原樣寫進 buffer（零配置）。**表示法（r2，審稿 N2）**：與 repo 的 `_route` 相同——**不含起點塊**，依行進順序到終點為止（`BuildRoute` 以 `while (cursor != start)` 回溯後反轉，`AbyssalVanguardTarget.cs:311-321`）。

### 7.4 紅線與零配置

不加 `NavMeshObstacle`、不 carving、不執行期烘焙；碰撞器只用 `BoxCollider`；`Update`／`LateUpdate` 零配置（新邏輯全部固定容量）；不用 Linq。

---

## 8. 既有測試受影響清單（讀碼推導，**未實跑**）

| 編號 | 測試 | 原因 | 建議處理 |
|---|---|---|---|
| T1～T4 | （r1 作廢）EditMode `AbyssalVanguardLogicTests` 三條、PlayMode `AbyssalVanguardPlayTests` 第 74／146／197 行 | 核心改由 `ForSpec` 依規格取用、預設值不改（§3），平地夾具下這些條文仍成立 | **不改**（斷言一字不改；`AbyssalVanguardPlayTests` 只屬 T5 的 SetUp 新增一行） |
| T5 | PlayMode 佔領類套件：`Capture19PlayTests`（22）、`CapturePlayTests`（21）、`CaptureSanctuaryPlayTests`（9）、`CaptureSanctuaryMatchPlayTests`（2）、`CaptureFogTargetPlayTests`（5）、`PactDamagePlayTests`（5）、`AbyssalVanguardPlayTests`（6）、`Capture19RageScript`（18）、`CaptureDummyPlayTests`（4） | 正式規則集改成峽谷後，傳送座標（起草時 grep 到 79 處 `WarpTo`／`RespawnAt`／`position =`）、點地路線、幀數窗口都以平地為前提；例如出生點走到 4 號、0 號的路線現在要經 13→4 斜坡，塔心 2／3／5／6 要繞外圈 | **必須裁定（§13 Q2）**：建議這批套件在 `SetUp` 以 editor-only 入口把地形關掉（仍用 `V0100Sanctuary` 規則＝v0.13.1 平地行為，斷言一字不動），另外新增峽谷專屬套件（§9-B/C）覆蓋同樣的端到端流程 |
| T6 | `LowFrameRateArrivalPlayTests`、`WallDetourPlayTests`、`DuelPlayTests` 等 Off 模式套件 | Off 模式維持平地（§13 Q2 建議） | 不改；V14-B15 要求它們保持綠燈（Off 零影響的證據） |
| T7 | EditMode `CaptureVisibilityLogicTests` | `AppliesTo` 改讀旗標；`V0100Sanctuary` 旗標為 true → 行為不變 | 不改，應維持綠燈 |

**主對話凍結前修訂（缺口 G1）— 關地形入口的定義**：editor-only `Phase1Bootstrap.UseFlatCaptureSpecForTest()`（`#if UNITY_EDITOR`，受 V14-D01 檢查），須在進入佔領模式前呼叫；效果＝正式佔領局改用 `CaptureBoardSpec.V0100Sanctuary`（`Terrain==null`），因此由規格推導出的一切同時回到 v0.13.1：地形／崖壁物件停用、平地碰撞體啟用、不蓋崖壁格、核心 `ForSpec`＝(4.375,0)／1.8、選塔直線距離、巨獸用原鄰接、無崖台射程／視野／淺水／地熱點；開火顯形仍生效（規則不依賴地形）。T5 中**使用 `Phase1Bootstrap` 的 8 個套件**（r1，審稿 M-2：`PactDamagePlayTests` 不經 `Phase1Bootstrap`，`grep -c Phase1Bootstrap` 為 0，不加、也不受地形影響）只在 `SetUp` 加這一行呼叫（新增行，刪除行數 0，斷言一字不改，符合 V14-A18）。判讀結果（`vow-toolchain/v0140-prefreeze-tests.md`）：92 條中確定會紅 2 條（都是核心座標，已由 §3 修訂框改為依規格取用而消解）、不確定 2 條（V9_C03、V10_C05 零配置，靠實作守零配置）、其餘 88 條不受影響。

§12.1-3 的逐檔三分類（r1，審稿 L-10）：**以 `vow-toolchain/v0140-prefreeze-tests.md` §1 為準**（已完成，逐方法列出；92 條＝確定會紅 2＋不確定 2＋確定不受影響 88），本表不重抄。確定會紅的 2 條（核心座標）已由 §3 的 `ForSpec` 消解；不確定的 2 條（V9_C03、V10_C05 零配置）由 B 步實跑判定，紅了算實作未守零配置。

---

## 9. 實作順序與每步驗收

順序 `A → B → C → D`，每步一個可審查的提交；開工前保存 `git status --short --branch` 與 `git rev-parse HEAD`。任一步沒過就回到該步起點修，不在壞掉的基線上疊下一步。

**所有 V14 條文共同遵守**（承接 V0100 §3）：
- 期望值一律寫成字面值，**不得讀 `CanyonTerrainSpec`／`CanyonTuning`／`CaptureBoardSpec` 的欄位**來算期望值。**明文例外（r1，審稿 L-11）**：Unity 層（B／C 步）的「場景與規格一致」及「行為落在規格上」這兩類斷言，可以拿被測物的 `GetCliffSegment`、`HeightAt`、`ClassAt`、`CrossesCliff`、`TileAt` 當判準（例：B01 比對崖壁碰撞體端點、B03～B06 的 |y−H| 與穿崖檢查）——這些函式在 A 步已被字面值釘死（A02、A03、A06、A16、A22），B／C 步量的是「場景／接線是否照它走」。除此之外不得用它們產生期望值。
- 純邏輯時序 dt＝1/64（地熱點 0.6 秒門檻）或 0.25（其餘）；PlayMode 固定 `Time.captureDeltaTime = 1/60`（低幀率條文另註），在 TearDown 還原；窗口幀數寫死，**不得拉長 timeout、加 retry、加 sleep**。
- 「沒發生／相等」的斷言都附活性證據。每條新測試都要證明錯誤實作會紅：純邏輯由 `mutation_check.py`（§10）；Unity 測試由實作者把關鍵行改壞跑一次紅、寫進回報（附紅在哪個斷言），用**改壞前的備份**還原。
- 「不穿崖」一律用同一個判準函式 `CrossesCliff(p0, p1)`：線段 p0→p1（水平）與 `CanyonTerrainSpec` 任一崖壁線段相交（含端點）即為真。PlayMode 每幀記錄 (x,z)，相鄰兩幀連線不得穿崖。
- 點擊前先斷言投影離畫面四邊都 ≥ 16px，不成立就判紅，不得換點。Unity 端只用 `ProfilerRecorder` 量配置。
- 線上（D 步）不使用按秒數重播的點擊劇本；時間判斷只讀畫面。

**指令**（前綴 `v0140-`）：`verify.sh`（`UNITY_REFS_DIR` 用絕對路徑）→ 建場景 → EditMode → PlayMode（依序）；突變只在獨立 worktree 跑，不與 Unity batchmode 同時跑。

### A. 純邏輯：地形規格、高度、崖壁蓋格、可達性、規則、地熱點、核心

**工作**：§7.1 新檔；`AbyssalVanguardTuning.ForSpec`／`IsInCore`（§3，預設值不改）；`CaptureVisibilityLogic` §4.3／§4.7 新多載；`BlockGrid.TryFindNearestFreeSameFloor`（只新增）；新增 `Assets/Tests/EditMode/Canyon*Tests.cs`；`mutation_check.py` 尾端加 §10 突變（K1～K18）。既有測試檔一律不改（r1：原「§8 T1～T3 經同意的改寫」已作廢）。不碰場景與 MonoBehaviour。

以下 `T＝CanyonTerrainSpec.V0140`，`S＝CaptureBoardSpec.V0140Canyon`。

- **V14-A01 規格字面值**：`S` 的 19 個中心、42 條邊、雙方母板塊與復活點、2 個場邊點與 `V0100Sanctuary` 逐值相等（容差 0），6 個規則旗標相同，`S.FogEnabled==true`、`S.Terrain` 非 null；`V0100Sanctuary.Terrain`、`V090Nineteen.Terrain`、`V080Seven.Terrain` 為 null，`V090Nineteen.FogEnabled`、`V080Seven.FogEnabled` 為 false。`T` 的 19 塊層別依 §2.1（0,1,4＝−1；2,3,5,6＝+1；其餘 0）。6 條斜坡的（低端塊, 高端塊）依序為 (9,2)、(11,3)、(15,5)、(17,6)、(1,7)、(4,13)，寬 3.5、長 4。取樣 400 點（(−19.75+0.5i, −19.75+0.5j)，i、j 每隔 4 取 1）`LayerCountAt==1`、`ResolveLayer(x,z,任意 y)==0`。**紅燈實作**：2 號設成中層；少一條斜坡；夾具也掛上地形；`LayerCountAt` 回 2。
- **V14-A02 高度表**：`HeightAt(x,z,0)`：塔心 0／1／4 → −1；2／3／5／6 → 1；7～18 → 0；(19,19) → 0（棋盤外）；斜坡 R5 中心 (0,−11.3671875) → −0.5；R5 低端 (0,−9.3671875) → −1；R5 高端 (0,−13.3671875) → 0；R5 側邊內 (1.7421875,−10.5) → −0.716796875±1e-5；側邊外 (1.7578125,−10.5) → −1；R0 中心 (9.84375,5.68359375) → 0.5±1e-5；R4 中心 (0,11.3671875) → −0.5。**紅燈實作**：內插方向反（低端得 0）；斜坡半寬用 3.5（側邊外得坡面值）；斜坡判定排在 `TileAt` 之後（R5 中心得 −1）。
- **V14-A03 地形類別**：`ClassAt`：(0,0)→Canyon；(6.5625,3.7890625)→Cliff；(13.125,0)→Plain；R0 中心→Ramp（雖然 `TileAt`＝2）；R5 中心→Ramp；(19,19)→Plain；G0 落點 (4.59375,2.0703125)→Cliff；G0 踏點 (2.09375,2.0625)→Canyon。**紅燈實作**：斜坡照所在塊分類（R0 中心得 Cliff）。
- **V14-A04 可走鄰接**：`WalkNeighbor` 逐塊（依索引遞增）：0:{1,4}、1:{0,7}、2:{3,9}、3:{2,11}、4:{0,13}、5:{6,15}、6:{5,17}、7:{1,8,18}、8:{7,9}、9:{2,8,10}、10:{9,11}、11:{3,10,12}、12:{11,13}、13:{4,12,14}、14:{13,15}、15:{5,14,16}、16:{15,17}、17:{6,16,18}、18:{7,17}；總數 44（22 條無向邊，雙向對稱）。**紅燈實作**：沿用 42 條邊；斜坡只登記單向。
- **V14-A05 全圖可達（板塊層級）**：以 `WalkNeighbor` 做 BFS（寫在測試裡，不呼叫被測物的路線函式），從 13 的步數逐塊為 §2.5 表第 3 欄（0:2、1:3、2:4、3:3、4:1、5:3、6:4、7:4、8:5、9:4、10:3、11:2、12:1、13:0、14:1、15:2、16:3、17:4、18:5），從 7 的步數為其鏡像（0:2、1:1、2:3、3:4、4:3、5:4、6:3、7:0、8:1、9:2、10:3、11:4、12:5、13:4、14:5、15:4、16:3、17:2、18:1）；兩次都 19 塊可達。另：拿掉任一條可走邊後仍全連通（22 次，活性：每次確實少一條邊）。**紅燈實作**：走路圖用原 42 條邊（從 13 到 2 得 3 步、到 8 得 4 步）；漏掉 7→1（從 13 到 7 得 6 步）。
- **V14-A06 崖壁線段**：`CliffSegmentCount==44`；依種類 20／12／12；20 段完整崖壁的端點集合等於 §2.3 列出的 20 組共用邊頂點（逐組比對，容差 1e-5，端點順序不拘）；每條斜坡邊的兩段邊端長度彼此相等（±1e-5）、中間開口 3.5±1e-5 且中點＝斜坡中心、兩段邊端＋開口＝該共用邊的實際長度（±1e-5）（**主對話凍結前修訂**：原條文「各 0.4375±1e-5」在斜向邊上不可能與開口 3.5±1e-5 同時成立——六角頂點座標是 float 近似，斜向共用邊實長不是恰好 4.375，C# 重建邊端為 0.437587；屬 02 §2.1「無論實作對錯都不可能通過」例外，改寫後仍會抓「開口不置中」與「邊端長度錯」）；每條坡道兩段側牆長 4、與長軸平行、距長軸 1.75。**紅燈實作**：漏 1|8；開口不置中；側牆沒產生。
- **V14-A07 崖壁蓋格**：新 `BlockGrid(−20,−20,0.5,80,80)`，`StampCliffs(grid, 0.35f, +1)` 後 `BlockedCount==764`（§12.1-1 C# float32 重建值，與 float64 模型相同；外擴 0.3499／0.3501 也都是 764，沒有格子卡在邊界）；以下格子不是 Blocked：19 個塔心、6 條斜坡中心、2 個踏點、2 個落點；以下格子是 Blocked：20 段完整崖壁中點。再 `StampCliffs(grid, 0.35f, −1)` → `BlockedCount==0`、`NegativeStampCount==0`。**紅燈實作**：沒外擴（BlockedCount 小很多、崖壁中點旁的格仍空）；斜坡邊整條蓋掉（斜坡中心 Blocked）；撤銷時少撤。
- **V14-A08 格點全圖可達（真實 GridNavigator）**：`BlockGrid` 蓋場地外圈（316 格，同 `Phase1Bootstrap.RegisterArenaBoundary` 的規則）＋崖壁；`new GridNavigator(grid, new NavGridTuning())`。(a) 從藍出生點 (0,−16.65625) 對 19 個塔心各 `ResolveGoal` → 全部 `substituted==false`；從紅出生點 (0,16.65625) 同樣。(b) `grid.HasLineOfSight(0,0 → 6.5625,3.7890625)==false`；沿每條斜坡長軸，低端往內 0.25 到高端往內 0.25 的線段 `HasLineOfSight==true`。(c) 從 (0,0) 出發的整合場成本（`FlowField` 以 2 號塔心為源建場後讀 (0,0) 格）≥ 600（≈30m；平地約 7.6m＝152 左右），且 < int.MaxValue。**紅燈實作**：沒蓋崖壁（(b) 視線為真、(c) 成本 ~150）；斜坡封死（2／3／5／6 的 `substituted==true`）。
- **V14-A09 高度連續不變量（掃全格）**：A08 的格點上，對每對「FlowField 規則下可一步走到」的相鄰空格（8 鄰接、不切角），|H(格心₁)−H(格心₂)| ≤ 0.25×格心距＋1e-5 → 違反 0 組；活性：高度差不為 0 的相鄰對 ≥ 100（斜坡上確實有連續爬升）、三種高度的空格都存在。**紅燈實作**：斜坡側牆沒蓋（坡面格與旁邊地面格相鄰、差 >0.125）；斜坡寬度與蓋格寬度不一致。
- **V14-A10 核心移位**（r1 改寫，審稿 M-1／L-6）：令 `C＝AbyssalVanguardTuning.ForSpec(CaptureBoardSpec.V0140Canyon)`。**第一個斷言** `C.CoreX==0f`；再 `C.CoreZ==0f`、`C.CoreRadius==2.5f`、`C.CoreChannelSeconds==3.5f`。活性（兩規格真的不同）：`ForSpec(V0100Sanctuary)` 的 `CoreX==4.375f`、`CoreRadius==1.8f`。`C.IsInCore(0,2.5)==true`、`C.IsInCore(0,2.5078125)==false`、`C.IsInCore(2.5,0)==true`。對 `S` 的 19 塊：0 號塔心距 0；其餘 18 塊塔心距 ≥ 5.0（核心圈與它們的佔塔圈不重疊）。取樣：(0,0)、(1.5,1.5)、(0,2.5)、(0,2.5078125)、(2.5,0.0078125) 各點 `C.IsInCore` 與 `S.CircleAt(x,z,2.5f)==0` 的真假逐點相同（活性：真假兩種都出現）。**紅燈實作**：`ForSpec` 對峽谷仍回傳 v0.13.1 值（半徑 1.8、圓心 (4.375,0)）；`ForSpec` 對兩規格回傳同一組值（活性斷言紅）。
- **V14-A11 崖台射程 +10%**（r1 拆成兩個測試方法，審稿 H-1／M-9；兩方法各自獨立，順序照下列寫死）：
  - **A11a `AttackRange` 取值**：`CanyonRules.AttackRange(5f, ax, az, T)`，**第一個斷言**＝攻擊者在 R0 中心 (9.84375,5.68359375)（斜坡，`TileAt`＝2）→ 5f；再：攻擊者在 2 號塔心 → 5.5f；10 號塔心 → 5f；0 號塔心 → 5f；`terrain==null` 時攻擊者在 2 號塔心 → 5f；`AttackRange(1.8f, 2 號塔心, T)` → `1.8f*110/100f`（= 1.98f，逐位相等）、`AttackRange(1.8f, 10 號塔心, T)` → 1.8f。
  - **A11b `InAttackRange` 邊界**（§4.1 介面；目標水平距離平方與射程平方比較，含邊界）：**第一組（目標所在層不影響，排第一）**：攻擊者 10 號塔心 (13.125,0)、目標 (8.125,0)（`TileAt`＝2 崖台，距 5.0）→ 在；目標 (8.109375,0)（`TileAt`＝2 崖台，距平方 25.156494 > 25）→ **不在**。第二組（攻擊者在崖台）：攻擊者 2 號塔心 (6.5625,3.7890625)、目標 (12.0625,3.7890625)（9 號平原，距 5.5）→ 在；(12.078125,3.7890625)（距 5.515625）→ 不在。第三組（對手出手距離）：`InAttackRange(1.8f, 2 號塔心, 目標, T)`，目標 (8.5390625,3.7890625)（距 1.9765625）→ 在、(8.546875,3.7890625)（距 1.984375）→ 不在；`InAttackRange(1.8f, 10 號塔心, 目標, T)`，目標 (11.328125,0)（距 1.796875）→ 在、(11.3203125,0)（距 1.8046875）→ 不在。各點 `TileAt` 已以 C# r1 重算（`refcs-r1-output.txt` H-1 節）。
  - **紅燈實作**：看目標所在層（A11b 第一組 (8.109375,0) 判「在」）；斜坡也加成（A11a 第一個斷言得 5.5）；乘 1.1 並在平原也套用。（r2，審稿 N6：「加成只給藍方」在 `InAttackRange` 沒有陣營參數的純邏輯層寫不出來，移到 V14-B08 的紅燈欄。）
- **V14-A12 視野合成**：`V0140Canyon` Active 局，`TryStart` 後以 `SeedOwnershipForTest` 設盤面。(a) 崖台觀看者 (6.5625,3.7890625)：目標 (14.5625,3.7890625)（距 8，9 號、中立）→ 看得到；(14.578125,3.7890625) → 看不到。(b) 平原觀看者 (13.125,0)：目標 (13.125,6.0) → 看得到；(13.125,6.015625) → 看不到。(c) 谷底觀看者 (0,0)：目標 G0 落點 (4.59375,2.0703125)（距 5.04，2 號中立）→ **看不到**；同盤面 2 號改為觀看方持有 → 看得到（真視野優先）；2 號改為對方持有 → 看不到。(d) 谷底觀看者 (0,7.578125)：目標 (4.5,10.5)（8 號平原，距 5.37）→ 看得到。(e) 崖台看谷底：觀看者 (4.59375,2.0703125)、目標 (0,0) → 看得到。(f) 觀看者倒地、目標所在塊由觀看方持有 → 看得到；倒地且不持有 → 看不到。(g) `V0100Sanctuary` 局同 (a) 座標：距 6.0 以內才看得到（崖台加成不存在；`(12.5625,3.7890625)` 看得到、`(12.578125,3.7890625)` 看不到）、同 (c) 座標看得到（無地形）。紅藍兩方各跑一次（互換 `viewerSide`）。**紅燈實作**：谷底規則排在真視野之前（(c) 持有時仍看不到）；8m 給所有人（(b) 6.015625 看得到）；谷底規則雙向（(e) 看不到）；夾具也套地形規則（(g)）。
- **V14-A13 淺水**：`CanyonRules.WaterRadius(3f, 0f, x, z, T)`：4 號塔心 (0,−7.578125) → 4f；13 號塔心 → 3f；R5 中心 → 3f；2 號塔心 → 3f。潮汐 `WaterRadius(3f, 2f, 4 號塔心)` → 6f、13 號塔心 → 5f。`terrain==null` → 3f／5f。**紅燈實作**：乘法（谷底 ×4/3，潮汐時得 6.67）；斜坡算谷底；潮汐與淺水擇一。
- **V14-A14 地熱點時序（dt＝1/64，`GeothermalVentLogic` 直接驅動）**：以 §4.5「介面」的 `Tick(dt, heroX, heroZ, heroCanTrigger)` 驅動；除 (g) 外 `heroCanTrigger` 恆為 true（邏輯層不追蹤飛行，(e) 的第二次發射時間不含飛行鎖）。英雄位置為輸入，「在 G0」＝(2.09375,2.0625)，「遠處」＝(1000,1000)。(a) 第 1 個 tick 起在 G0：第 38 個 tick 後 `LaunchCount==0`、`Progress(G0)==0.59375`；第 39 個 tick 後 `LaunchCount==1`、`LastLaunchPad==0`、`LastLandingX==4.59375f`、`LastLandingZ==2.0703125f`、`Cooldown(G0)==8f`、`Cooldown(G1)==0f`。(b) 冷卻：(a) 之後英雄仍在 G0（測試驅動）→ 第 550 個 tick 後 `LaunchCount==1`、`Progress(G0)==0`；第 589 個 tick 後 `LaunchCount==2`（第 551 個 tick 起重新累加）。(c) 受傷：同 (a)，第 20 個 tick 後 `NotifyDamaged()` → 第 21 個 tick 後 `Progress==0`（不累加），第 59 個 tick 後仍 1 次都沒發射、第 60 個 tick 後 `LaunchCount==1`；`Cooldown(G0)` 在第 59 個 tick 後為 0（中斷不觸發冷卻）。(d) 離開：同 (a)，第 31 個 tick 在遠處、其餘在 G0 → 第 69 個 tick 後 0 次、第 70 個 tick 後 1 次。(e) 各點獨立：(a) 發射後第 40 個 tick 起英雄改在 G1 (−2.09375,−2.0625) → 第 78 個 tick 後 `LaunchCount==2`、`LastLaunchPad==1`、落點 (−4.59375,−2.0703125)。(f) 邊界：英雄在 (2.46875, 2.0625)（距踏點圓心恰 0.375）→ 視為在踏點（第 39 個 tick 發射）；(2.4765625, 2.0625)（距 0.3828125）→ 跑 100 個 tick 0 次。(g) 英雄在 G0 但 `heroCanTrigger==false`（代表倒地／飛行鎖定／非 Active，由呼叫端合成）：跑 100 個 tick 0 次、`Progress(G0)==0`；接著改 true → 第 39 個 tick 後發射（活性：同一位置可觸發）。(h) dt＝0.25：第 2 個 tick 後 0 次、第 3 個 tick 後 1 次。(i) `Reset()` 後兩點冷卻與進度為 0。**紅燈實作**：受傷不歸零（(c) 第 39 個 tick 發射）；冷卻兩點共用（(e) 不發射）；冷卻只擋發射、不擋累加（(b) 第 550 個 tick 後 `Progress≠0`、第 551 個 tick 就發射）；離開不歸零（(d) 第 40 個 tick 發射）；受傷後當 tick 仍累加（(c) 第 59 個 tick 發射）。
- **V14-A15 巨獸路線**：`T.FindWalkRoute(start, dest, buffer)`（BFS、平手取索引小）：0→13＝[0,4,13]；0→7＝[0,1,7]；2→13＝[2,3,11,12,13]；6→7＝[6,17,18,7]；每一步相鄰兩塊都在 `WalkNeighbor` 內。零配置（呼叫端給緩衝）。**紅燈實作**：沿用 `CaptureBoardSpec.Neighbor`（2→13 得 [2,0,4,13]，穿 0|2 崖壁）。
- **V14-A16 同樓地板**：`T.IsSameFloor`：(0,0)↔(0,−7.578125)（谷底對谷底）→ true；(3.5,0)↔(5.0,0)（谷底對崖台，跨 0|3／0|2 崖壁線）→ false；R5 低端 (0,−9.3671875)↔R5 高端 (0,−13.3671875) → true（坡度 0.25）；(1.7421875,−10.5)↔(1.7578125,−10.5)（坡面對側邊外地面）→ false。崖壁格（§5.2 定義，距任一崖壁線段 ≤ 0.35）：(3.5,0.5)（距 0|2 線段 0.5078）→ 與 (0,0) 同樓地板 true；(1.7421875,−10.5)（距 R5 側牆 0.0078）↔ (1.5,−10.5) → false（端點在崖壁格，雖然兩點都在坡面上且不穿崖）。另（推出條件，r1 寫死盤面，審稿 M-5）：`BlockGrid(−20,−20,0.5,80,80)` 蓋場地外圈（316 格）＋崖壁（外擴 0.35），英雄點 **p0＝(3.875, 0)**（谷底，距 0|2／0|3 崖壁線段 0.4330 > 0.35），再以 `StampBox(3.625f, 0f, 1f, 0f, 2f, 0.3f, 0.35f, +1)` 蓋一面與真實符印牆同尺寸的牆（中心 (3.625,0)、法線 (1,0)、半寬 2、半厚 0.3、外擴 0.35；與 V14-B05(c) 的真實牆同一盤面）。以 `TryFindNearestFreeSameFloor(3.875f, 0f, 20, T, 0, …)` 找到的格子，格心 `HeightAt == −1` 且 `CrossesCliff(p0, 格心)==false`（C# r1：格心 (2.25,−0.25)，距 1.644）；**活性**：同一盤面用既有 `TryFindNearestFree(3.875f, 0f, 20, …)`（無條件）找到的格子 `HeightAt == 1`（C# r1：格心 (4.75,−0.75)，距 1.152；兩者距離差 0.49m，非 float 邊界）。**紅燈實作**：`IsSameFloor` 恆真（推到崖台上）；「崖壁格」改用 `grid.IsBlocked` 判定（p0 格被牆蓋住 → 有條件搜尋找不到任何格，斷言紅）。
- **V14-A17 純邏輯零配置（只在 dotnet 跑；Unity 端 Ignore 寫明理由）**：`HeightAt`、`ClassAt`、`IsSameFloor`、`CanSee`（V0140，新舊兩個多載）、`AttackRange`、`InAttackRange`、`WaterRadius`、`GeothermalVentLogic.Tick`、`RevealTracker.Tick`／`IsRevealedTo`、`BlockGrid.TryFindNearestFreeSameFloor`、`FindWalkRoute` 各呼叫 10000 次，配置 0 byte；活性：`HeightAt` 三種高度與坡面值都出現、`CanSee` 真假都出現、發射 ≥ 1 次。**紅燈實作**：`FindWalkRoute` 每次 new 佇列。
- **V14-A18 夾具與零改動**：`git diff 96730fd -- Assets/Scripts/Core/Logic/CaptureMatchLogic.cs Assets/Scripts/Core/Logic/HexBoardLayout.cs Assets/Scripts/Core/Logic/GridNavigator.cs Assets/Scripts/Core/Logic/FlowField.cs Assets/Tests/EditMode/Capture19BoardTests.cs Assets/Tests/EditMode/Capture19EncircleTests.cs Assets/Tests/EditMode/CaptureSanctuary*.cs Assets/Tests/EditMode/GridNavigatorTests.cs Assets/Tests/EditMode/BlockGridTests.cs Assets/Tests/EditMode/CaptureVisibilityLogicTests.cs` 輸出為空（`BlockGrid.cs` 若需加有條件的最近空格搜尋，只能新增方法、不得改既有方法；`git diff` 的刪除行數為 0）；`git diff --numstat 96730fd -- Assets/Tests` 中，既有檔刪除行數全為 0（r1：無例外——原「除 §8 經同意改寫的檔外」所指的 T1～T4 已作廢；T5 的 8 個套件只有新增行）；`verify.sh` 純邏輯 0 失敗、通過數 ≥ 基線＋本步新增數（基線於 §12.1-2 記錄）。**紅燈實作**：為了讓新測試過而改既有斷言；改了 `GridNavigator` 行為。
- **V14-A20 開火顯形**（r1 依 §4.7「介面」改寫，審稿 B-3／M-6）：`V0140Canyon` Active 局，`SeedOwnershipForTest` 設 19 塊全中立；`tr＝new RevealTracker(1.5f)`；以下 `CanSee` 一律用新多載。**①（第一組，排第一）** 紅方觀看者 (0,0)（谷底）、目標 `BlueHero` 在 G0 落點 (4.59375,2.0703125)（崖台）→ 看不到（活性：§4.3 第 4 步生效）；`tr.NotifyHit(藍, 紅)` 後**立刻**看得到。**② 只顯形攻擊方、不顯形受害方**：同一時刻，藍方觀看者 (0,−7.578125)（4 號谷底）、目標 `RedOpponent` 在 G1 落點 (−4.59375,−2.0703125)（5 號崖台）→ 看不到（受害方的單位不因被打而顯形）；活性：另外 `tr.NotifyHit(紅, 藍)` 後同一查詢看得到（③ 之前以 `tr.Reset()` 後重新 `NotifyHit(藍, 紅)` 復原 ① 的狀態）。**③ 計時**（dt＝0.25 呼叫 `tr.Tick`）：① 的查詢在第 5 個 tick 後（1.25 秒）仍看得到、第 6 個 tick 後（1.5 秒）看不到（`> 0` 才顯形，1.5−6×0.25＝0）。**④ 重設不累加**：重新 `NotifyHit(藍, 紅)`，第 4 個 tick 後再 `NotifyHit(藍, 紅)` → 從那一刻起再 6 個 tick 才消失（總共第 10 個 tick 後看不到、第 9 個 tick 後仍看得到）。**⑤** 倒地觀看者、顯形中 → 看得到（第 1.5 步在倒地之前）；**⑥** `Reset()` 後 `Remaining(BlueHero)==0`、① 的查詢看不到；**⑦** `tracker==null`（舊多載）→ ① 的查詢看不到（命中不影響舊多載）；**⑧** `V0100Sanctuary` 局：紅方觀看者 (0,0)、`BlueHero` 在 (13.125,0)（距 13.125 > 6，10 號中立）→ 看不到；`NotifyHit(藍, 紅)` 後看得到。**紅燈實作**：顯形放在谷底規則之後（① 命中後仍看不到）；顯形給錯陣營——攻擊方／受害方對調（① 仍看不到）或受害方單位也對攻擊方陣營顯形（② 看得到；r2 依審稿 N10 寫明：「受害方單位對自己陣營顯形」在兩陣營下等價於不顯形，不在本條範圍）；重設改累加（④ 第 10 個 tick 後仍看得到）；用 `>=0` 判定（③ 第 6 個 tick 後仍看得到）。
- **V14-A21 選塔走路距離**：見 §6 修訂框（含 r1 起點塊規則）。另加一組起點塊 −1：對手在 (0,19)（棋盤外、`TileAt==−1`，塔心平面距離最近＝7 號）、只剩 2 號與 12 號可選 → 起點塊 7：到 2 走路 22.73（7-8-9-2）、到 12 走路 37.89（7-1-0-4-13-12）→ 選 2。**紅燈實作**：起點塊 −1 時直接回 −1 或當成 0 號（從 0 算：到 2 為 37.89、到 12 為 22.73 → 選 12）。
- **V14-A22 斜坡任意落差**（r1 新增，審稿 M-11；落實使用者裁定「斜坡支援任意落差」）：以 §2.2 的公開建構子建一個**測試用**規格 `T7＝new CanyonTerrainSpec(CaptureBoardSpec.V0140Canyon, 高度{0,1,4＝−1；2,3,5,6＝+1；其餘 0}, low{9,11,15,17,1,4,0}, high{2,3,5,6,7,13,3})`——比本批多第 7 條斜坡 0→3（谷底 −1 → 崖台 +1，**落差 2m**、坡度 0.5；只是測試資料，不是版面改動）。第 7 條斜坡中心＝0|3 共用邊中點 (3.28125,−1.8945312)、長軸≈(0.8660139,−0.5000199)（C# r1）。斷言（±1e-5）：**第一個斷言** `T7.HeightAt(3.28125f,−1.8945312f,0)==0`（中心）；`HeightAt(2.415236f,−1.3945113f,0)==−0.5`（沿軸 −1m）；`HeightAt(4.147264f,−2.3945513f,0)==0.5`（沿軸 +1m）；`ClassAt(中心)==Ramp`。`IsSameFloor`：沿軸 −1.75 (1.7657257,−1.0194964) ↔ 沿軸 +1.75 (4.7967744,−2.769566) → **true**（兩點高差 1.75、相距 3.5，坡度 0.5；兩點距崖壁 1.75、不穿崖）；沿軸 −1 的坡面點 (2.415236,−1.3945113) ↔ 橫向 −2.75 的谷底點 (1.0401813,−3.7760496)（0 號，H＝−1，距崖壁 1.0）→ **false**（穿過第 7 條斜坡的側牆；高差 0.5／距 2.75＝0.18，任何坡度門檻寫法都會誤判為 true）。活性：`CrossesCliff` 對第 7 條斜坡的側牆有反應——同一對點在 `T7` 上 `CrossesCliff==true`；且 `T`（本批 V0140）在中心點 `HeightAt==−1`、沿軸 ±1.75 那對點 `CrossesCliff==true`（證明第 7 條斜坡確實由資料產生）。全部點位與結果以 C# r1 重算（`refcs-r1-output.txt` M-11 節）。**紅燈實作**：內插寫死落差 1m（`low + t×1`：中心得 −0.5）；`IsSameFloor` 用坡度門檻（0.25：第一對得 false；或放寬到 0.5：第二對得 true）；崖壁線段寫死 V0140 的 44 段（第 7 條沒有開口與側牆：第一對 false、第二對不穿崖）。
- **V14-A19 突變全抓**：§10 的 K1～K18 在獨立 worktree 實跑：`突變被抓到：N / N`、還原後全綠、`RESULT: ALL MUTATIONS CAUGHT`；既有突變全部仍 CAUGHT（沒有因原文改動變成 SKIP）。**紅燈實作**：任一新條文只測到「有回傳值」而沒測行為（對應突變 MISSED）；本批改動讓既有突變原文失效卻沒重新定位（SKIP）。

**回退**：回到 A 的起點；不得帶著半套邏輯進 B。

### B. Unity：地形幾何與接線

**工作**：§7.3；新增 `Assets/Tests/PlayMode/CanyonPlayTests.cs`；§8 G1 的 8 個套件 `SetUp` 各新增一行 `UseFlatCaptureSpecForTest()`（只新增、斷言不動），放在同一提交，提交訊息逐檔列出（r1：草案的「§8 經同意的改寫」已作廢）。「開局」＝真實點 `CAPTURE`、**等 30 幀（鏡頭跟上英雄傳送；同 v0.13 回 Lobby 先例，r3fix R3F-1）**、再真實點對手；「幀」＝一次 `yield return null`；**「倒地保持」**（r2，審稿 N8）＝以 `TakeDuelDamage(1000)`（英雄）／`ReceiveDamage(1000)`（對手）擊倒，並在每次復活的當幀再次以同一呼叫擊倒，直到該案例結束（B04、B06、B07、C02 等凡寫「倒地／倒地保持／保持倒地」者皆照此）；captureDeltaTime＝1/60（另註者除外）。

- **V14-B01 場景單一來源**：建場景後：`CanyonTerrain` 根物件下**恰好** 19×3＋6＋4＝67 個 `BoxCollider`（19×3 個地形、6 個斜坡、4 個棋盤外長條），其他任何 `Collider` 0 個（r2，審稿 N7：地熱圓盤等顯示物件不得帶碰撞體，否則會混進 B13 的遮擋量測）；`CliffBarriers` 是**獨立根物件**（不在 `CanyonTerrain` 底下），其下恰 44 個 `BoxCollider`，第 i 個的兩端（中心 ± 半長×切線）與 `T.GetCliffSegment(i)` 相差 ≤ 1e-3、`bounds.min.y ≤ −2`、`bounds.max.y ≥ 4`、layer＝Ignore Raycast；全場沒有 `MeshCollider`、沒有 `NavMeshObstacle`。另以純幾何取樣驗證「3 個旋轉矩形＝正六角」：每塊 441 個取樣點，`TileAt` 判在塊內 ⇔ 落在 3 個盒子之一的 x/z 投影內（邊界 1e-4 內的點略過）。**紅燈實作**：手抄座標漏一段；崖壁放 Default 層（點谷底打到隱形牆）；六角只用 1 個盒子。
- **V14-B02 模式啟停**：Off 模式：`CanyonTerrain`、`CliffBarriers` 停用、`Ground_40x40` 碰撞體啟用、`_navGrid.BlockedCount==316`（加上存活中的牆）。點 `CAPTURE` 後：地形啟用、平地碰撞體停用、`BlockedCount==1080`（316＋764）。回 Off → 恢復、`NegativeStampCount==0`。英雄回 Off 時 y＝0。**紅燈實作**：Off 模式也有峽谷；回 Off 沒撤格（Off 模式 `BlockedCount` 仍含崖壁，單挑被看不見的崖壁擋路）。
- **V14-B03 高度跟隨與 NavMesh 探針**：開局後英雄在藍出生點；點 R5 低端往北 1m 的地面投影點 (0,−8.3671875) → 360 幀內到達（≤0.05m；C# r1 名目 90 幀、路徑 8.25m，4 倍裕度）；全程每幀 |y − H(x,z)| ≤ 1e-3、`NavMeshAgent.isOnNavMesh==true`；經過 R5 矩形的幀數 ≥ 20（活性：真的走坡道）；最後 y＝−1±1e-3。**紅燈實作**：y 沒更新（谷底仍 0）；y 用 agent 的 y（恆為 0）。
- **V14-B04 走斜坡到崖台、不穿崖（1/60）**：開局後英雄傳送到 (0,0)（y 自動 −1）、對手 `ReceiveDamage(1000)` 倒地並在復活時再次擊倒；等 30 幀；真實點 2 號塔心 (6.5625,1,3.7890625) 的投影點（點擊當幀＝f0，以下幀數自 f0 起算）。斷言：① f0＋198 幀前**沒有**進入 2 號光圈（證明有繞路；r1 依審稿 H-6 重訂：C# r1 以 5.5m/s 實跑名目 **340 幀**〔路徑 31.17m，經 R4 首幀 103、R0 首幀 304〕，直線到光圈邊 5.08m＝**55.4 幀**，門檻取兩者中點 197.7→198；草案的 300 只比名目少 40 幀）；② f0＋600 幀前進入 2 號光圈（名目 340，裕度 1.76 倍）；③ 全程相鄰幀 `CrossesCliff==false`；④ 依序經過 R4（7→1）與 R0（9→2）矩形各至少 1 幀；⑤ 每幀 |y−H| ≤ 1e-3。**紅燈實作**：崖壁沒蓋格＋沒碰撞體（① 紅：直線穿崖）；只有碰撞體沒蓋格（貼著崖壁滑到卡死，② 紅）。
- **V14-B05 低幀率不穿崖（(a)(b)(d) 在 dt＝1/3 與 1/4 各跑一次；(c) 為一次性推出，1/60）**：(a) 同 B04 的起點與點擊，跑 45 幀：③⑤ 成立、最後在 2 號光圈內（C# r1 名目：1/3 為 18 幀、1/4 為 24 幀）。(b) 英雄在 (3.25,0)（谷底貼 0|2／0|3 崖壁），面向 +x，連續 3 次微滑步（真實輸入）→ 每幀 `CrossesCliff==false`、y 恆為 −1。(c)（r1 改寫，審稿 M-5）英雄傳送到 V14-A16 的 p0＝(3.875,−1,0)，以 `SpawnRuneWallForTest(藍, 3.625f, 0f, 1f, 0f)` 立一面真實符印牆（與 A16 的 `StampBox` 同一盤面：中心、法線、4×0.6、外擴 0.35）→ 推出後英雄 y＝−1、`HeightAt(推後)==−1`、`CrossesCliff(推前, 推後)==false`、推後位置與 p0 不同（活性：真的被推過）；**活性（量在 B05 本身的實際格點上）**：立牆後、推出前（或以同一 `_navGrid` 於推出後）呼叫無條件 `TryFindNearestFree(3.875f, 0f, 20, …)`，其格心 `HeightAt == 1`（C# r1：(4.75,−0.75)）。(d)（r1 新增，審稿 M-10；Q7「崖台不能直接跳下」的行為驗收）英雄傳送到 G0 落點 (4.59375,1,2.0703125)（2 號崖台，距 0|2 崖壁線段 1.2246），面向 −x（谷底方向），連續 3 次微滑步（真實輸入；`CombatTuning.DashDistances` 1.4／0.9／0.5，前兩步累計 2.3m 已超過到崖壁線的 −x 距離 1.41m）→ 每幀 `CrossesCliff==false`、y 恆為 1、`ClassAt==Cliff`；活性：最後英雄到 0|2 崖壁線段的距離 ≤ 0.55（真的貼到崖壁碰撞體；名目 0.40＝半徑 0.35＋碰撞體半厚 0.05）。**紅燈實作**：`EjectFromBox` 不限同樓地板（(c) 被推上 2／3 號崖台）；崖壁碰撞體缺失或太矮（(d) 第 2 次滑步從崖台走下谷底，`CrossesCliff` 為真）；崖壁碰撞體放在 SphereCast 掃不到的層（同 (d)）。
- **V14-B06 紅方 AI 走斜坡**：開局後種子歸屬：2 號中立、其餘 18 塊紅方（讓 `SelectTargetTile` 必選 2 號）；英雄傳送到 (−17,0,−17) 並倒地保持；對手傳送到 (0,0)（f0）。斷言：① f0＋272 幀前沒有進入 2 號光圈（r1 重訂：直線到光圈邊 5.08m，4m/s＝**76.2 幀**；C# r1 以 4m/s 實跑名目 **467 幀**〔路徑 31.13m〕；門檻取中點 271.6→272）；② f0＋900 幀前進入 2 號光圈並開始引導（名目 467，裕度 1.93 倍）；③ 全程 `CrossesCliff==false`、|y−H| ≤ 1e-3；④ 經過至少 1 條峽谷斜坡（R4 或 R5）與 1 條崖台斜坡（R0 或 R1）的矩形。另（r2 依審稿 N4 改寫；重新載入場景、英雄存活）：開局後對手傳送到 G0 踏點中心 (2.09375,−1,2.0625) 並 `HoldOpponentForTest(true)`（§7.3；草案的 `Stop()` 只清一次指令，對手下一 tick 就走開，零鑑別力），英雄在 (−17,0,−17)；停 180 幀 → 每一幀對手都在踏點圓內（距圓心 ≤ 0.375；活性：對手在踏點內 180 幀 ≥ 引導所需 36 幀）、地熱點 `LaunchCount` 不變（仍為 0）、對手 y 恆為 −1。活性對照：接著把對手傳送到 (−17,0,17)、英雄傳送到同一踏點中心 → 38 幀內 `LaunchCount` 變為 1（踏點此時可觸發，名目 36 幀同 B07）。**紅燈實作**：對手走另一套沒有格點的移動（穿崖）；地熱點對任何人都觸發（180 幀內發射）。
- **V14-B07 地熱點接線**：開局後英雄傳送到 G0 踏點中心（f0）、對手倒地保持。① f0＋35 幀時仍在谷底（y＝−1）；② f0＋38 幀前發射；③ 發射後 30～32 幀（0.5 秒）內落地，落地點距 (4.59375, 2.0703125) ≤ 0.05、y＝1±1e-3、`ClassAt==Cliff`；④ 飛行中點地指令被忽略（飛行第 10 幀真實點地，落地後 5 幀內英雄仍在落點 0.05 內）。受傷（另一個測試案例、重新載入場景）：英雄傳送到 G0（f0），f0＋20 幀 `TakeDuelDamage(1)` → f0＋55 幀前不發射、f0＋64 幀前發射（受傷當幀歸零並跳過下一 tick，約第 22 幀起重新累加 36 幀）。冷卻（接第一組）：落地後 5 幀內把英雄傳回 G0 並停住 → 第一次發射後第 510 幀前不再發射（8 秒＝480 幀＋重新引導 36 幀）、第 530 幀前第二次發射。**紅燈實作**：地熱點沒接到受傷事件；冷卻沒接；落地 y 沒設（仍 −1，在崖台下方）。
- **V14-B08 射程接線**：先鋒未出現前（已過時間 < 600 秒）。(a) 英雄傳送到 G0 落點 (4.59375,1,2.0703125)（2 號崖台）、對手傳送到 (0,−1,0)（谷底，水平距離 5.03872m），並呼叫 `HoldOpponentForTest(true)`（§7.3：對手不走、不放牆、不前搖，但可被鎖定、可受傷）；2 號、0 號種子為藍方（真視野，排除視野干擾）。等 30 幀後真實點對手 → 90 幀內第一次命中，且命中前英雄水平位移 ≤ 0.02（射程 5.5 內，不必走近；r1 依審稿 H-3 由 0.05 收緊：錯誤實作〔射程仍 5.0〕至少要走 5.03872−5.0＝0.0387m 才進射程，0.02 門檻對它必紅）。(b) 對照：英雄在 (13.125,0,0)（10 號平原）、對手在 (8.0859375,1,0)（2 號崖台，`TileAt`＝2，水平 5.0390625m）並 `HoldOpponentForTest(true)`，同樣種子與點擊 → 300 幀內第一次命中，且命中前英雄水平位移 ≥ 0.03（r1 依審稿 H-3 重訂：射程 5.0 下，由三角不等式，任何正確實作進入射程前的水平位移 ≥ 5.0390625−5.0＝**0.0390625**，門檻 0.03 留 0.009 裕度；錯誤實作〔看目標層、射程 5.5〕不必走、位移 ≈0。C# r1 名目：流場沿 R0 繞行，108 幀後進射程、位移 5.93m；300 幀＝108＋(a) 的 90 幀出手上限，再留 1.5 倍）。(c) 對手（不 hold）：英雄在谷底 (2.8125,−1,1.625)（距 0|2 崖壁線段 0.5407m，C# r1），對手在英雄沿 (0.8660254, 0.5) 方向水平 1.9m 處＝(4.457948,1,2.575)（2 號崖台內、距崖壁 1.3593，C# r1），0 號種子紅方（對手看得見英雄）→ 120 幀內對手開始前搖（名目：第一次佔領 AI 更新即判定追打並進入前搖，≤ 3 幀；120 幀只防開局初始化順序），且前搖時水平位移 ≤ 0.05（出手距離 1.98 內；錯誤實作〔停步距離仍 1.8〕至少要走 0.1m，必紅）。(d) 對照 (c)：兩者都放在平原（英雄 (13.125,0,0)、對手 (13.125,0,1.9)，10 號塊內相距 1.9m，10 號種子紅方）→ 120 幀內前搖，且對手先移動 ≥ 0.05 才前搖（正確實作至少要走 1.9−1.8＝0.1m）。**紅燈實作**：`HeroController.IsTargetInAttackRange` 仍讀原始 `AttackRange`；對手的停步距離沒加成；崖台加成只給藍方（呼叫端只替英雄呼叫 `InAttackRange`、對手仍用 1.8：(c) 對手要先走 0.1m 才前搖而紅；r2 由 A11 移來，審稿 N6）。
- **V14-B09 視野接線**（r1 補座標與幀窗口，審稿 L-9）：開局、雙方存活，全程 `HoldOpponentForTest(true)`（對手不移動，量測不受 AI 走位影響）；每種配置＝傳送雙方後等 2 幀再斷言，斷言持續到該配置第 60 幀。① 英雄在 (0,−1,0)、對手在 G0 落點 (4.59375,1,2.0703125)（2 號中立）→ 第 2～60 幀對手 renderer 皆不可見、第 30 幀真實點對手不會鎖定；② 接著 2 號種子藍方 → 1 幀內可見。③ 英雄在 (6.5625,1,3.7890625)（崖台）、對手在 (13.5,0,5.0)（距 7.04、9 號中立）→ 第 2～60 幀皆可見；④ 英雄改在 (13.125,0,0)（平原）、對手在 (13.125,0,7.04)（9 號中立，距 7.04）→ 第 2～60 幀皆不可見。⑤ 紅方：英雄在 G0 落點（2 號崖台）、對手在 (0,−1,0)（0 號谷底；2 號中立）→ 第 2～60 幀 `TrainingOpponent.CanSeeHero==false`（「不追打」由 `CaptureOpponentPolicy.Decide` 的 `heroVisible==false` 在結構上保證）；活性：接著 2 號種子紅方 → 1 幀內 `CanSeeHero==true`。各點類別已以 C# r1 重算。**紅燈實作**：下游自己另算半徑；對手端沒接地形規則。
- **V14-B10 淺水接線（兩個入口）**：開局後英雄在 4 號塊，對 4 號塔心施放水 → 生成的水域半徑 4（讀 `ElementZoneView`／邏輯場的半徑欄位，容差 1e-4）；在 13 號施放 → 3。另從 `ElementField.CastWater` 入口（元素反應路徑）在谷底生成 → 4。**紅燈實作**：只改了其中一個入口。
- **V14-B11 先鋒、核心、巨獸**（r3 審稿 NEW-2：兩案開局後都把對手傳送到 (−17,0,−17) 並 `HoldOpponentForTest(true)`，遠離巨獸路線與核心，避免紅方對手膠囊擋住藍方巨獸〔巨獸裁切只略過己方英雄〕、讓結果取決於 AI 決策）：`SeedCaptureMatchElapsedForTest(599.75)` → 先鋒出現在 (0,−1,0)±1e-3；擊倒後英雄站 (0,−1,0) 引導 3.5 秒取得巨獸，且同時翻下 0 號塔（若未持有）；巨獸從 (0,0) 走向紅母：`CopyRouteForTest` 得 **[1,7]**（表示法不含起點塊，§7.3；即 0→1→7）、全程 `CrossesCliff==false`、|y−H| ≤ 1e-3、2400 幀內抵達 7 號塔心 0.6m 內（名目 15.16m÷3m/s＝303 幀）。**第二案（r1 新增，審稿 H-2；重新載入場景）**：開局後 `SeedBehemothForTest(藍, 6.5625f, 3.7890625f)`（巨獸在 2 號崖台塔心生成）→ 下一幀 `CopyRouteForTest` 得 **[9,8,7]**（表示法不含起點塊，§7.3；即 `WalkNeighbor` 圖上 2→7 唯一最短路 2→9→8→7；原 42 條鄰接的 `BuildRoute` 會得 [1,7]〔2→1→7〕，C# r1 以同一 BFS 寫法重算；r2 依審稿 N2 由 [2,9,8,7]／[2,1,7] 更正）；240 幀內巨獸 `TileAt==9`（名目：沿 R0 長軸 3m/s，第 76 幀越過 2|9 邊）、全程 `CrossesCliff==false`、|y−H| ≤ 1e-3。**紅燈實作**：`AbyssalVanguardTarget.BuildRoute` 仍用 `CaptureBoardSpec.Neighbor`（第二案路線 [1,7]，且朝 1 號走會被崖壁擋住、進不了 9 號；第一案 [1,7] 兩種寫法相同，抓不到，所以第二案必要）；生成 y 為 0（浮在谷底上方 1m）；巨獸位移裁切沒略過 `CanyonTerrain`（§5.2 巨獸列；第一案卡在 R4 與 7 號交界，2400 幀內到不了 7 號塔心）。
- **V14-B12 符印牆高度**：英雄在 (0,−1,−3) 面向 +z 極速施放 → 牆中心 y＝−1；英雄在 G0 落點 (4.59375,1,2.0703125)（2 號崖台）面向 −x 極速施放 → 牆中心 (0.59375,2.0703125)（`TileAt`＝0、谷底，C# r1）→ 牆中心 y＝−1、牆仍成形、格點計數增加（r1 依審稿 B-1 更正：草案的英雄 (6.5625,3.7890625) 施放後牆心 (2.5625,3.7890625) 其實落在 2 號崖台內〔`TileAt`＝2、`HeightAt`＝+1〕，照字面必紅）。**紅燈實作**：牆 y 固定 0。
- **V14-B13 谷底遮擋閘門（Q4）**：螢幕 844×390、`FollowCameraRig.SnapToTarget()` 後（52°、17m、FOV 40）。位置清單＝`docs/v0140-b13-positions.csv`（258 點＝腳本最差 8 個＋谷底所有崖壁段谷底側 0.37m、每 0.25m 一點；§12.1-1 C# 產生，測試逐點寫死）；每個位置把英雄傳送過去、等 1 幀、再 `SnapToTarget`，對英雄取 15 個取樣點（腳底往上 0.1／0.5／0.9／1.3／1.7m × 橫向 −0.3／0／+0.3m，橫向沿鏡頭右方），從鏡頭到取樣點的線段沒有被遮擋的算可見。**遮擋量法（r1 寫死，審稿 B-2）**：以 `Physics.RaycastNonAlloc(鏡頭, 方向, buffer, 線段長, Physics.AllLayers, QueryTriggerInteraction.Ignore)`（buffer ≥ 64，命中數＝buffer 長度即判紅，不得靜默截斷）取得線段上所有命中，**只計 `collider.transform.IsChildOf(CanyonTerrain 根物件)` 的命中**；英雄與對手膠囊、塔、牆、`CliffBarriers`、先鋒等其他碰撞體一律不算遮擋。理由：地形與英雄膠囊都在 Default 層，15 個取樣點全在英雄膠囊（半徑 0.35、高 1.8）內，用層遮罩的 `Physics.Linecast` 必先打到英雄自己而 0/15；C# 參考模型本來就只含地形盒與坡道（`refvalues` §3），所以下列數字不變。——**只算看得見的地形盒子，不含崖壁隱形碰撞牆**（**主對話凍結前修訂**：原條文遮罩含崖壁層，但 §5.2 規定崖壁碰撞牆高到 +4m、不渲染；照字面 C# 重建有 140 點 0/15、161 點不符門檻，任何符合 §5.2 的實作都必紅，屬 02 §2.1 例外。隱形牆不擋玩家視線，量它不是在量「英雄被遮住」。改寫後 C# 重建最差 3/15、頭頂全可見、0 點不符；仍會抓「崖台頂做成 +2」「鏡頭不含 y」兩種紅燈實作）。**通過判準（§13 Q10 裁定）**：建議＝每個位置 ≥ 2／15 個點可見且頭頂點（1.7m、橫向 0）可見；腳本模型最差 3／15（0.20，(4.055,−0.185) 與 (4.055,7.393)）、頭頂點全部可見、完全被遮 0 個。**紅了＝停手回報，依 Q4 提前做網點透視，不得調低崖壁或改鏡頭來過關**。**紅燈實作**（會讓這條紅的地形／鏡頭實作）：地形盒子比 §2 的高度高（例如崖台頂做成 +2）；鏡頭跟隨目標改成不含 y（谷底時鏡頭仍在平地高度、俯角變淺）。（r1，審稿 L-4：草案的「崖壁碰撞體比 §2 高」已不會讓它紅——崖壁不在量測內——刪除。）假綠防護：量法漏了地形（例如過濾條件寫錯、一個都不算）會讓它假綠，所以回報要附一個「英雄腳底放在 (6.5625,−1,3.7890625)、埋進 2 號崖台地形盒內」的對照點（`HeightAt` 會把傳送的 y 拉回 +1，所以對照點不傳送英雄：鏡頭位置與 15 個取樣點都由這個合成腳底位置照同一套幾何算出〔鏡頭＝腳底沿鏡頭 forward 後退 17m，即 (x, y+13.396, z−10.466)〕，只做遮擋量測），必為 0／15（C# 參考模型 0/15）。
- **V14-B14 既有測試**：依 §13 Q2 裁定處理 §8 T5（8 個套件 SetUp 加關地形入口，`PactDamagePlayTests` 不動）；T6、T7 與其餘全部既有 EditMode／PlayMode 0 失敗（基線 §12.1-2）。**紅燈實作**：地形漏到 Off 模式；迷霧 `AppliesTo` 沒接新規則集。
- **V14-B16 開火顯形接線**（r1 改寫，審稿 H-4／L-9）：開局、`SeedOwnershipForTest` 讓 2 號、0 號中立；英雄在 G0 落點 (4.59375,1,2.0703125)（崖台）、對手在 (0,−1,0)（谷底，水平距離 5.03872）並 `HoldOpponentForTest(true)`（§7.3 語意：不走、不放牆、不前搖，保持可受傷，`CanSeeHero` 照常計算）。**案例一**：① 開局後第 1～30 幀 `TrainingOpponent.CanSeeHero==false`、對手 renderer 對藍方可見（崖台看谷底）；② 真實點對手、英雄第一次命中的那幀（h）→ h 或 h＋1 幀 `CanSeeHero==true`；③ h 當幀英雄下 Stop 指令（停止攻擊），h＋85 幀仍 `true`、h＋95 幀 `false`（1.5 秒＝90 幀，兩側各留 5 幀）。**案例二（重新載入場景，驗 AI 反應）**：同一設定，h 當幀英雄下 Stop 並 `HoldOpponentForTest(false)` → h＋1～h＋90 幀內對手水平位移 ≥ 0.05（顯形後追打：水平距離 5.04 ≤ `ChaseStartDistance` 6）；活性對照：案例一的 h＋95 幀之後（顯形已消失）`HoldOpponentForTest(false)`，再 60 幀對手與英雄的水平距離減少量 ≤ 0.05（看不到就不追；它站在 0 號光圈內佔 0 號）。**案例三（反向，重新載入場景）**：英雄在谷底 (2.8125,−1,1.625)、對手（不 hold）在 (4.457948,1,2.575)（2 號崖台，與 B08(c) 同座標）；2 號中立、0 號種子紅方（對手靠真視野看得見英雄而出手）。開局後藍方對對手 `CanSee==false`（谷底看崖台）；對手第一次命中英雄的那幀（h′，名目：前搖 0.7 秒＝42 幀）→ h′ 或 h′＋1 幀藍方 `CanSee(對手)==true`，h′＋85 幀仍 `true`、h′＋95 幀 `false`（對手下一次命中在 h′＋102 幀〔恢復 1.0＋前搖 0.7 秒〕之後，不會提早重設）。**紅燈實作**：傷害入口沒接 `NotifyHit`（① 以後全紅）；只接了普攻沒接元素（另以一次元素命中重跑案例一 ②）；對手受傷入口接了、英雄受傷入口沒接（案例三紅）。
- **V14-B15 Off 模式零影響**：`LowFrameRateArrivalPlayTests`、`WallDetourPlayTests`、`DuelPlayTests`、`ShieldAndProjectilePlayTests`、`RuneWallPlayTests`、`ElementReactionPlayTests`、`GreyboxSmokeTests` 逐字不改、全綠；Off 模式英雄 y 全程 0。**紅燈實作**：`HeroLocomotion` 在 `_terrain==null` 時也改了 y。

**指令**：建場景 → `verify.sh` → EditMode → PlayMode（依序、不並行）。**回退**：回到 B 的起點；不改 A 的驗收值遷就場景。

### C. 完整一局、AI、效能

- **V14-C01 峽谷放置整局（captureDeltaTime＝1/10）**：開局後英雄完全不輸入，直到回 Lobby 或 9600 幀。斷言：① `LAST: RED WINS`；② 紅方在結束前至少翻過 2 塊崖台與 1 塊谷底（活性：AI 真的上崖、下谷）；③ 全程對手相鄰幀 `CrossesCliff==false`、|y−H| ≤ 1e-3；（原 ④ `LaunchCount==0` 於 r1 刪除，審稿 L-5：英雄不輸入、對手不觸發，對任何實作恆綠；「AI 不觸發地熱點」由 B06 負責）；⑤ 結束時已過時間 ∈ [336.8, 505.2] 秒（§12.1-1 C# 參考模型名目 421.016 秒 ±20%；同模型跑平地得 411.016，與 v0.10 Unity 實跑 412.016 差 1.0 秒；先鋒不會出現，600 秒前就結束）。**紅燈實作**：AI 卡在某條斜坡（②紅或時間到）；穿崖（③）。
- **V14-C02 AI 不卡斜坡（回歸）**：開局後英雄傳送到 (−17,0,−17) 並保持倒地（寫法同 B06；r1 依審稿 H-5 補：英雄預設站在藍出生點、距第 14 段起點約 1.5m < `ChaseStartDistance` 6，對手會改去追打英雄而超時；參考模型未模擬英雄）。對手依序被指定 19 塊為目標（種子歸屬讓每次只剩該塊非紅），從上一個目標出發；每一段在「參考模型格點路徑長 ÷ 4m/s × 60 × 1.5（向上取整）」幀內進入光圈；起點：第 0 段從紅出生點 (0,16.65625)、第 k 段從第 k−1 塊塔心出發；目標依 0,1,…,18；每段窗口幀＝372,169,629,169,629,634,169,510,184,179,169,180,179,184,195,179,180,169,179（§12.1-1 C#，`vow-toolchain/v0140-refvalues.md` §4；移速已確認 4m/s）；任何一段超時即紅、不得加 retry。**紅燈實作**：斜坡淨寬不足讓整合場繞進死角。
- **V14-C03 佔領對局零配置**：寫法同 V10-C05（暖機局只碰量測局不會用到的東西）；量測局：開局後英雄走 R5 下谷、站 G0 引導並發射、落地；對手追逐；量 480 幀；活性：y 更新 ≥ 400 次、`CanSee` 真假都出現、地熱點發射 1 次、巨獸路線不在此局；`UpdateBytes==0`、`LateUpdateBytes==0`。另：對局中 `_navGrid.Version` 只在符印牆出現／消失時改變（不因崖壁重蓋）。**紅燈實作**：`HeightAt` 或路線每幀配置；每幀重蓋崖壁。
- **V14-C04 模式切換與第二局**：Off → 佔領 → 結算 → Lobby → Off → 佔領，兩次進入後 `BlockedCount` 相同、`NegativeStampCount==0`、地熱點冷卻歸 0、先鋒與核心重置、英雄 y 正確。另（r1 M-13／r2 N3／r3 NEW-1，使用者裁定「甲」）：峽谷模式下**兩個入口都驗**——(i) **第一次** Off→佔領、(ii) 結算後回到 Lobby——各自在該幀起 5 幀內：英雄在 (0, 0, −16.65625) 的 0.05m 內、y＝0±1e-3；對手在 (4, 0, −12.5) 的 0.05m 內、y＝0±1e-3，對手到任一崖壁線段的水平距離 > 0.35（C# r3：2.1374）；`FollowCameraRig.SnapToTarget()` 後對手點擊點（對手位置＋up）的 `WorldToScreenPoint` 深度 > 0 且離 844×390 四邊 ≥ 16px（C# r3 名目 (536.2, 306.1)，回報附 Unity 實測像素）；接著以 `TapOpponent`（真實點）→ 同幀 `CaptureState==Active`（可點、點了能開局；投影被 HUD 攔截時此句紅）。(ii) 的開局驗完後再以 `SeedCaptureScoresForTest` 結算、回 Lobby，繼續原流程（→ Off → 佔領）。**紅燈實作**：第二次進入重複蓋格或少撤；峽谷模式仍傳回 (−4,8)（距 1\|18 崖壁 0.114m，距崖斷言紅）；對手放在畫面外（如 r2 的 (−4,0,12)，投影 y＝562，可點斷言紅）；只處理其中一個入口（(i) 或 (ii) 紅）。
- **V14-C05 全套回歸**：`verify.sh` ALL PASS；EditMode 0 失敗；PlayMode 0 失敗；突變 ALL CAUGHT；V14-A18 的 diff 檢查再跑一次。**紅燈實作**：任一套件失敗或新增略過（新增 Ignore 只允許 V14-A17）。
- **效能（記錄、不當及格線，同 V0100 §4-9 的精神）**：Editor 內 `ProfilerRecorder("Main Thread")` 在 C03 量測局的中位數與 P95 寫進回報，與 v0.13.1 同一局型對照；WebGL 實際幀率在 D 步以截圖時間戳粗估並列為「未驗證」。

**回退**：回到 C 的起點；A、B 保留，不推送 WebGL。

### D. WebGL 部署與線上實測

**工作**：版本 0.14.0（`VowVersion.cs`＋bundleVersion）；WebGL 建置與部署；驗收指南新增 §25、`HANDOFF.md`、`ARCHITECTURE.md` 補「地形查詢介面」一節；線上腳本 `<T>/v0140-online-check.py`、`<T>/v0140-webkit-check.py`，截圖放 `<T>/browser-screenshots/v0140-*`。截圖判讀由主對話逐項勾選，任一項看不到就算紅。

- **V14-D01 版本與建置**：`VowVersion.Version=="0.14.0"`；三套測試全綠；WebGL 建置成功；每一個 `ForTest` **方法宣告**都在 `#if UNITY_EDITOR` 內：`grep -rnE "(public|internal|private|protected)[^(=]*ForTest\s*\(" Assets/Scripts --include=*.cs`，排除 `Assets/Scripts/Core/Logic/`（純邏輯既有慣例：`CaptureMatchLogic.cs:295` 註解說明權限收斂由 Unity 端入口負責，dotnet 測試也要呼叫得到）後逐一核對（r1，審稿 M-2：草案的 `Seed.*ForTest` 比對不到 `UseFlatCaptureSpecForTest`、`HoldOpponentForTest`、`SpawnRuneWallForTest`、`CopyRouteForTest`，且會誤中 `Phase1Bootstrap` 既有的 `?devvanguard` 呼叫點——那是呼叫不是宣告，不在本條範圍）。**紅燈實作**：版本沒改；關地形／種子／hold 入口被編進正式版。
- **V14-D02 送達證明**：`SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 成功；`git log origin/gh-pages -1` 顯示本次部署；Chromium（844×390、DPR 2、觸控）讀到 `v0.14.0 · build … · <來源 sha7>`。**紅燈實作**：線上仍舊版（先排除 PWA 快取）。
- **V14-D03 WebKit 載入**：同 V10-D03（WebKit 載入到可點 `CAPTURE`、版本列正確）。**紅燈實作**：地形材質或新 shader 在 WebKit 下編譯失敗導致黑畫面或 console error。
- **V14-D04 開局畫面**：點 `CAPTURE` → 點對手 → 截圖：看得出峽谷比兩側低、崖台比平原高、13→4 斜坡；HUD 不被地形遮住。**紅燈實作**：WebGL 下地形沒畫或整片黑。
- **V14-D05 線上下谷**：點 (0,−8.3671875) 的地面投影點（用 `v0140_coords.py` 以鏡頭參數算，點擊前斷言離四邊 ≥16px）→ 不做其他輸入、等 6 秒 → 截圖：英雄在峽谷底、沒有卡在坡道上。**紅燈實作**：WebGL 建置裡平地碰撞體沒停用（點擊落點偏北約 0.78m、英雄停在坡頂）；斜坡碰撞體缺失（英雄卡在 13 號與 4 號交界）。
- **V14-D06 線上地熱點**：接 D05，點 G0 踏點 (2.09375,−1,2.0625) 的投影點 → 等 5 秒不輸入 → 截圖（r1 依審稿 H-6 由 3 秒改 5 秒：C# r1 名目＝走進踏點圓 112 幀＝1.867 秒〔路徑 10.27m〕＋引導 0.6＋飛行 0.5＝**落地 2.967 秒**，原 3 秒只留 33ms；5 秒留 2.03 秒給點擊延遲與幀抖動。落地後英雄停在落點〔§4.5 發射時清除移動目的地〕，多等不會改變畫面）：英雄在 2 號崖台上（比谷底高、位於東北崖台）。**紅燈實作**：WebGL 下地熱點不觸發。
- **V14-D07 console 乾淨**：D02～D06 全程 `pageerror`＝0、`console.error`＝0。**紅燈實作**：`HeroLocomotion` 在 y 偏離 NavMesh 時噴「不在 NavMesh 上」錯誤；地形初始化順序錯誤。
- **V14-D08 文件**：`HANDOFF.md` 寫入來源 SHA、gh-pages SHA 與時間、實跑輸出摘要；驗收指南 §25 寫規則摘要、試玩步驟、實測結果與「未驗證」清單，至少包含：①原生手機手感與觸控點崖台的精準度；②線上 AI 走斜坡（只有 PlayMode 證據）；③線上遮擋（只有 V14-B13 PlayMode＋腳本模型）；④WebGL 實際幀率；⑤PWA 舊版快取處理方式；⑥斜坡上半段的點地落點誤差與光圈／坡面交界顯示（§5.2 灰盒限制，r1）。**紅燈實作**：HANDOFF 的 SHA 與 `git log origin/gh-pages -1` 不一致；未驗證項漏列（例如把 PlayMode 證據寫成線上已驗證）。

**回退**：v0.13.1 的 `e82603c`（來源 `eb3c164`）是可回退的送達點；D 沒過就不宣稱 v0.14.0 已送達。

---

## 10. 突變定義（加入 `mutation_check.py` 尾端）

格式同既有：`(代號, 說明, 檔案, 原文, 改壞後, 至少要變紅的測試)`。代號用前綴 **K**（r1，審稿 L-1：避開 `mutation_check.py` 既有的 C1～C18、C13b、C17b，§14 裁定紀錄中的「C15～C16」＝本表 K15～K16）。原文要在凍結時的實作裡唯一出現（A 步完成後由實作者依實際程式碼填入 before／after 全文，**說明與預期紅的測試不得改**）。每一條都是一份「能編譯、只改行為」的完整錯誤實作，不是只動一個常數；「外層保護不會先擋掉」的意思是：預期紅的那條測試，第一個失敗的斷言必須是下表「紅在哪」欄的行為斷言，不是前面的 `IsNotNull`／計數斷言或編譯錯誤——A 步回報時逐條附第一個失敗斷言的訊息。

| 代號 | 錯誤實作（完整描述） | 預期紅的測試 | 紅在哪（第一個失敗的斷言） | 外層為什麼擋不住 |
|---|---|---|---|---|
| K1 | `WalkNeighbor` 直接回傳 `CaptureBoardSpec` 的 42 條原始鄰接 | V14-A05 | 從 13 到 2 的步數 3≠4（逐塊依索引斷言，0、1 號兩種鄰接同步數，第一個不同的是 2 號） | A04 會一起紅，但 A05 測試內沒有先斷言鄰接數；BFS 寫在測試裡，只讀 `WalkNeighbor` |
| K2 | `StampCliffs` 對斜坡邊也蓋整條（忘了留開口），側牆照蓋 | V14-A08 | (a) 藍出生點對 **0 號**塔心 `substituted==true`（r1 更正：峽谷兩個入口 13→4、7→1 都被封死，依索引第一個就是 0 號，不是草案寫的 2 號） | A07 的斜坡中心格斷言也會紅；A08 不斷言計數，先跑 `ResolveGoal` |
| K3 | 斜坡側牆線段不產生（清單只有 32 段），蓋格照清單 | V14-A09 | 違反組數 >0 | A09 不讀線段數，只掃格子 |
| K4 | `HeightAt` 的斜坡內插用 `h1 + (h0−h1)·t`（方向反） | V14-A02 | R5 低端 0≠−1 | 依 A02 本文順序，前面的塔心、棋盤外在突變下同值，R5 中心 t＝0.5 時兩種內插同為 −0.5，第一個不同的就是 R5 低端（不需另外排序） |
| K5 | `InAttackRange` 以**目標**位置的地形類別算射程（呼叫 `AttackRange(baseRange, tx, tz, terrain)`） | V14-A11b | 第一組：攻擊者 10 號、目標 (8.109375,0)（崖台，距平方 25.156494）得「在」（突變射程 5.5） | r1 依審稿 H-1 改寫：草案的「目標 (8.125,0) 距 5.0」在突變下仍判「在」，恆綠；A11b 第一組就是它 |
| K6 | `ClassAt` 先查 `TileAt` 再查斜坡（斜坡被歸為所在塊） | V14-A11a | R0 中心攻擊者得 5.5 | 同時會紅 A03；A11a 第一個斷言就是 R0 中心（與 K5 分在不同測試方法，排序不再衝突） |
| K7 | `CanSee` 把谷底規則（第 4 步）放到真視野（第 2 步）之前 | V14-A12 | (c)「2 號由觀看方持有 → 看得到」得看不到 | (a)(b) 不涉及谷底、突變下同值；(c) 第一句（中立時看不到）也同值，第一個不同的是持有那一句 |
| K8 | 局部視野一律 8m | V14-A12 | (b) 距 6.015625 看得到 | (a) 的觀看者本來就在崖台（8m）、突變下同值；第一個不同的是 (b) |
| K9 | 淺水改乘法：谷底時回傳 `(baseRadius + talentBonus) * 4f / 3f`（左結合、先乘後除，3f×4f/3f＝4f 在 float32 精確；r1 依審稿 L-2 寫明乘在含潮汐的總和上） | V14-A13 | 潮汐 4 號塔心 (3＋2)×4/3＝6.6667≠6 | 無潮汐組 3×4/3＝4 與非谷底組都同值，第一個不同的就是潮汐組 |
| K10 | 地熱點受傷只設旗標、不歸零進度（③ 只跳過累加） | V14-A14 | (c) 第 21 個 tick 後 `Progress==0`（突變保留 20/64＝0.3125）（r1 依審稿 M-9 更正：草案寫第 59 個 tick） | (a)(b) 不涉及受傷、同值 |
| K11 | 冷卻用單一欄位（兩點共用） | V14-A14 | (a) 第 39 個 tick 後 `Cooldown(G1)==0f`（共用欄位＝8）（r1 依審稿 M-9 更正：草案寫 (e)） | (a) 前面的 `LaunchCount`、`LastLaunchPad`、落點、`Cooldown(G0)` 都同值 |
| K12 | 冷卻中照樣累加引導，只在 ⑤ 擋發射（冷卻一結束就用累積的進度立刻發射） | V14-A14 | (b) 第 550 個 tick 後 `Progress(G0)` 不為 0 | (a) 第一次發射前沒有冷卻，(a) 全綠；(b) 的 `LaunchCount==1` 在突變下同值（⑤ 仍擋發射） |
| K13 | `ForSpec(V0140Canyon)` 回傳 v0.13.1 值（半徑 1.8、圓心 (4.375,0)） | V14-A10 | 第一個斷言 `ForSpec(V0140Canyon).CoreX==0f` | r1 依審稿 L-3 改寫：預設值不改，既有 `Tuning_*` 在突變下仍綠（它們測的是預設／平地），只有 A10 會紅 |
| K14 | `IsSameFloor` 恆回 true | V14-A16 | (3.5,0)↔(5.0,0)（谷底對崖台）得 true | 第一組谷底對谷底本來就是 true |
| K15 | `CanSee` 把開火顯形檢查放到谷底規則（第 4 步）之後 | V14-A20 | ①`NotifyHit(藍,紅)` 後紅方看藍英雄仍看不到 | ① 命中前那句（看不到）同值 |
| K16 | `RevealTracker.NotifyHit` 把攻擊方與受害方對調（顯形 victimSide 的英雄／對手，顯形對象＝attackerSide） | V14-A20 | ①`NotifyHit(藍,紅)` 後紅方看藍英雄仍看不到（被顯形的是紅對手） | r1 依審稿 B-3 改寫：草案的「忽略 victimSide、對所有陣營生效」在兩陣營遊戲裡只差「看自己人」的查詢，A20 沒有這種查詢，是等價突變，已刪除；本突變改變了「誰被顯形」，① 必紅 |
| K17 | `SelectTargetTile` 忽略 terrain、一律直線距離 | V14-A21 | 峽谷盤面選 2 而非 13 | A21 第一組就是峽谷盤面 |
| K18 | `HeightAt` 斜坡內插寫死落差 1m（`low + t × 1f`） | V14-A22 | 第一個斷言：第 7 條斜坡中心得 −0.5≠0 | r1 新增：本批 V0140 的 6 條斜坡落差都是 1m，A02 在突變下全綠，只有 A22 的 2m 測試斜坡抓得到 |

A 步另外要依 V0100 R10 的做法重新定位受本批改動影響的既有突變原文（例如 `AbyssalVanguardTuning` 相關），任何一條變成 SKIP 都算 V14-A19 紅。

**等價突變檢查（r1）**：K1～K18 逐條以「突變後，預期紅那一欄的斷言在所有合法實作細節下是否必然改變結果」審過：每條都改變了至少一個被斷言的回傳值（見「紅在哪」欄的突變值），無等價突變。草案 C16 屬等價突變，已由 K16 取代。

---

## 11. 風險與回退

| 風險 | 說明 | 對策／退路 |
|---|---|---|
| **R1 穿崖的入口不只一條** | 「英雄 x/z 跨越崖壁線而沒經過斜坡／地熱點」是本批最危險的效果。會改位置的入口至少 8 個（§5.2），`EjectFromBox` 的最近空格搜尋、`WarpTo`／復活、NavMesh `SyncAgent` 夾回、低幀率大步位移、巨獸自己的位移都可能繞過。 | 防線按效果寫：統一判準 `CrossesCliff`＋每幀記錄（B04～B06、C01）；崖壁同時有格點（導航）與碰撞體（位移）兩道；`EjectFromBox` 加同樓地板；B 步開工先數分母（§5.2 末）。 |
| **R2 平地假設散落各處** | 起草時 52 行 `y = 0f`／`(x, 0f, z)`、6 個物理射線；平地 `Ground_40x40` 會攔截谷底點擊；裂風矢水平射線打不到崖台目標；對手遮擋射線緩衝可能被地形碰撞體塞滿。 | §5.2 分母清點逐行分類；B02、B08、B12 專門覆蓋。 |
| **R3 既有佔領 PlayMode 套件大量失效** | 約 90 條以平地為前提。 | §13 Q2 先裁定；建議用 editor-only 關地形保住舊條文，另寫峽谷套件。 |
| **R4 平地 NavMesh＋高度偏移** | agent 的內部位置在 y＝0 平面，角色在 ±1m；`nextPosition` 夾回與 `isOnNavMesh` 在 y 偏移下的行為未實測。 | B03 探針先做；若失敗，退路是只在正式佔領局關掉 agent 的 `SyncAgent` 夾回（邊界由場地碰撞體保證）或改用第二份預烘焙 NavMeshData（`NavMesh.AddNavMeshData`，仍是靜態預烘焙，不違反紅線）——任一退路都要回報並經主對話同意。 |
| **R5 遮擋閘門臨界** | 模型最差可見 0.20（3／15），Unity 的實際膠囊與鏡頭平滑可能更差。 | B13 在 B 步最早跑；紅了照 Q4 提前網點透視，不調幾何過關。 |
| **R6 斜坡太窄** | 崖台斜坡格點淨寬 1.72m，整合場可能鋸齒；一面符印牆可以封死。 | C02 逐段走訪；Q9 裁定能否在斜坡上放牆。 |

**已知良好 commit（每步完成時記錄）**：起點 `96730fd`；A 完成＝`<A-SHA>`；B 完成＝`<B-SHA>`；C 完成＝`<C-SHA>`；D 送達＝`<來源 SHA>`／`<gh-pages SHA>`。每步回報附 `git rev-parse HEAD`。

**2.5D 導航卡住的退路**（依序，任一條都要主對話＋使用者同意才走）：
1. 若 B03／B04 在 NavMesh 高度偏移上卡住 → R4 的兩個退路。
2. 若斜坡通道讓整合場反覆卡住（C02 紅超過 2 輪）→ 斜坡寬度改 4.375m（整條邊開口，斜坡邊端 0 段、改由側牆定義），這是改 §2.2 數值，走 §12。
3. 若整體 2.5D 無法在 C 步收斂 → 退回 A 的提交：純邏輯地形與規則保留（沒有接線、不影響線上），v0.13.1 線上版本不動，重新規劃（例如先做「崖壁＋斜坡但全平地高度」的視覺版）。

---

## 12. 變更規則與回報

承接 `V0100_SANCTUARY_PLAN.md` §4 全文（凍結範圍、只能由主對話＋使用者針對該條明確同意才能修改、實作者只能回報不能自改、移動及格線的判準、恆真／恆假由主對話修正後事後告知、每步回報格式）。本批另外：

- **【凍結前補】的欄位**只能填 §12.1 實跑出來的值，不得依實作結果回填（r1：計畫內已無待補欄位）。
- **V14-B13 是閘門**：它紅不代表實作錯，而是觸發 Q4 的範圍變更；不得為了讓它綠而改崖壁高度、鏡頭參數、取樣點或門檻。
- **斜坡寬度、地熱點座標**是【解讀】草案值，凍結後改動一律走本節。

### 12.1 凍結前的前置作業（主對話執行，不改 repo 內任何程式）

1. **參考值實跑**：由沒有對話史的 fresh agent 在 repo 外的臨時專案，把 §2 的地形規則寫成 C#（float32，與 `BlockGrid` 同精度）重算：V14-A07 的 `BlockedCount`、A08 (c) 的成本、B13 的位置清單與可見比例、C01 的結束時間窗口、C02 每段幀數；並獨立重跑 `v0140-reachability.py` 比對 §2.5 兩張 BFS 表（重建模型 vs. C# 重建，兩者都不是 Unity 實跑，回報時標明）。
2. **基線**：在 `96730fd` 跑 `verify.sh`、EditMode、PlayMode，記錄通過／略過數量寫進 V14-A18、B14。**主對話審稿補**：`96730fd` 與 v0.13.1 程式提交 `227f0fd` 之間只差 `ProjectSettings.asset` 的 bundleVersion 與文件（`eb3c164`、`96730fd`），測試程式與被測碼逐字相同，可沿用 `227f0fd` 的實跑結果當基線：verify 305 過／1 略過（`vow-toolchain/v0131-verify.log`）、EditMode 306 項 299 過／0 敗／7 略過（`v0131-edit.xml`）、PlayMode 211／211（`v0131-play.xml`）。凍結前以 `git diff --stat 227f0fd 96730fd` 確認只含上述檔案。
3. **§8 T5 逐檔判讀**：逐檔讀 9 個佔領類 PlayMode 套件，分三類（確定會紅／確定不受影響／不確定）。（已完成：`v0140-prefreeze-tests.md` §1，§8 引用之。）
4. **分母複查**：§4.3 的 `CaptureVisibilityLogic.` 呼叫點、§4.4 的 `WaterRadius`、§5.2 末三項 grep 在凍結當下重跑一次。

---

## 13. 規格留白／需使用者裁定

每題附選項與建議；「都照建議」即可開工。

- **Q1 6 處斜坡是否足夠？（可達性已驗證，問的是手感）** 全圖可達、沒有孤島或關節點（§2.5）。但：谷心 0 號與四座崖台相鄰卻要繞 37.9m（5 倍，約 6.9 秒）；谷心只能經 7／13（雙方第一母板塊）進出。**主對話審稿更正**：1 號塊外側鄰居 7、8、18 全是紅母，4 號塊外側鄰居 12、13、14 全是藍母——版面 C 下「峽谷的平原入口一定在母板塊上」是幾何結構，加平原斜坡改不了；草案原寫乙案「多出不經母板塊的入口」有誤，已改。選項：(甲) **照裁定 6 處**，每方一個谷口（7／13），谷心到崖台靠地熱點（單向）與繞外圈；(乙) 加 2 處 8→1、14→4（180° 旋轉對稱）：每方多一個谷口，**仍在母板塊上**（紅 8、藍 14），峽谷變得較好進出；(丁) 加 2 處「崖台↔谷底」斜坡 0→3、0→6（180° 旋轉對稱；落差 2m、坡度 0.5≈26.6°，仍低於 45.6° 可走上限）：這是唯一「不經母板塊」的谷底出入口，谷心走路就能上崖台（東南 3、西北 6；另兩座 2、5 仍只靠地熱點上去），先鋒戰會變成「谷底＋崖台」連成一片的開放戰場，5 倍繞行只剩地熱點沒蓋到的方向。**➡️ 建議：(甲)**，先試玩；覺得太封閉，(乙)(丁) 都只是資料（§2.2 表多兩列、丁案多一個坡度欄位），不改架構。
- **Q2 峽谷地形與既有佔領 PlayMode 套件怎麼處理？（必須裁定）** 地形只在「正式佔領局」啟用（Off／單挑維持平地）是前提。(A) **既有 9 個佔領類套件在 `SetUp` 以 editor-only 入口關閉地形**（規則仍是 v0.13.1 平地的 `V0100Sanctuary` 行為，斷言一字不動），另寫 `CanyonPlayTests`／C 步覆蓋峽谷端到端；(B) 全部遷移到峽谷，逐條重寫傳送座標與幀數窗口（約 90 條，每條都要像 v0.10 §2.6 一樣逐條同意）；(C) 地形在所有模式都啟用（連單挑都在峽谷）→ Off 模式數十條測試一起失效。原條文沒有錯：它們是在平地規則下訂的，本批改的正是地形。**➡️ 建議：(A)＋Off 維持平地**。代價：舊套件從此守的是「平地規則仍正確」而非「正式版」，正式版的保護全落在新套件，所以 C01～C04 必須完整。
- **Q3 v0.13.1 的「核心圈不得與任何佔塔圈重疊」測試怎麼辦？** Q1 甲正好要求重合。**➡️ 建議：刪除舊條文，由 V14-A10（與 0 號重合、與其他 18 塊不重疊）取代**，提交訊息寫明。
- **Q4 紅方選目標用直線距離還是走路距離？** (甲) **正式規則集改用走路距離**（峽谷上不會為了直線最近而繞 5 倍路；舊夾具不變，V9-A15 等不受影響）；(乙) 維持直線距離（零改動，但 AI 可能在谷心選崖台、繞一大圈）。**➡️ 建議：(甲)**。這是 AI 策略改動，需要你點頭。
- **Q5 射程 +10% 作用在哪些攻擊？** (甲) **只有普攻**（藍英雄 5→5.5m、紅對手出手距離 1.8→1.98m）；(乙) 普攻＋元素施放距離（3→3.3m）與火浪射程（6→6.6m）；(丙) 再加符印施放距離。**➡️ 建議：(甲)**：GDD「俯射射程」指射擊；元素與符印是地面落點技能，加成會讓崖台施放壓制谷底過強，也多出十幾條測試。
- **Q6 谷底水域蓋到崖台上的人算不算？** 水域是 x/z 圓，谷底貼崖放 4m 水會蓋到崖台邊緣。(甲) **照 x/z 算（算）**；(乙) 只影響與圓心同一樓地板的目標。**➡️ 建議：(甲)**，簡單且與現有元素判定一致；(乙) 要改元素反應系統，留到網點透視批一併考慮。
- **Q7 地熱點誰能觸發？** (甲) **只有玩家操控的英雄**（本版＝藍方；AI 站上去也不會彈）；(乙) 雙方都能觸發、只是 AI 不主動去（AI 追打時可能誤踩被彈上崖台，違反「只走斜坡」）。**➡️ 建議：(甲)**；日後 AI 要用地熱點時再開放。另外兩個細節一併確認：發射後 0.5 秒拋物線飛行、飛行中不能下指令但可被攻擊（建議照此）；崖台往谷底**不能**直接跳下（只有斜坡），照裁定。
- **Q8 符印牆跨崖壁怎麼辦？** (甲) **照樣成牆、牆中心取所在高度**（跨崖時牆的一半浮在崖頂或埋進崖壁，灰盒可接受）；(乙) 牆的 4 個角不在同一樓地板時施放失敗（不耗冷卻）。**➡️ 建議：(甲)**。
- **Q9 能不能在斜坡上放符印牆？** 4m 牆可以完全封住一條斜坡 5 秒（300 HP 可打破）。(甲) **可以**（戰術選擇，牆會被打或自然消失）；(乙) 禁止牆的碰撞盒與斜坡矩形相交。**➡️ 建議：(甲)**，先試玩觀察是否過強。
- **Q10 「谷底貼崖壁不被完全遮住」的通過門檻？** 腳本模型最差位置可見 3／15（0.20，谷心東西頂點附近）、頭頂全部可見、完全被遮 0 個。(甲) **字面解讀：每個位置至少 2／15 個取樣點可見且頭頂點可見**（留 1 點誤差給 Unity 實際幾何）；(乙) 至少 1／3 可見——依模型會在 4 個角落位置紅，等於本批就要做網點透視。**➡️ 建議：(甲)**；但要知道角落處英雄約 80% 被擋，試玩覺得看不清就提前網點透視。
- **Q11 斜坡寬 3.5m、長 4m 與地熱點座標是否接受？** 【解讀】草案值（§2.2、§2.4），依據是格點淨寬與「格子不被外擴蓋掉」。**➡️ 建議：接受，試玩再調**（改寬度只改 `CanyonTuning` 一個值，但連帶 V14-A06／A07 的字面值要重算、走 §12）。
- **Q12（資訊，不需裁定）** 歸屬連通（BFS 斷能、包夾、圍城）照舊用 42 條邊，崖壁不切斷地脈（§4.6）。若你希望崖壁也切斷地脈，是策略改動，另開一批。
- **Q13（資訊，不需裁定）** GDD §伍-1 寫「4 處固有斜坡」，本批依裁定做 6 處；GDD 是否同步由你決定，本批不改 GDD。
- **Q14（r1 新增；✅ 已裁定 2026-09-30：使用者「按照建議」＝(甲) 保留舊條文、不刪）Q3 的執行方式偏離了你的裁定**：你在 Q3 照建議同意「刪除 v0.13.1『核心圈不得與任何佔塔圈重疊』舊條文、由 V14-A10 取代」。凍結前修訂（§3）改成核心**依棋盤規格取用**（峽谷＝(0,0)／2.5；平地夾具仍是 v0.13.1 的 (4.375,0)／1.8），舊條文測的是平地夾具，照樣成立，於是改為**保留、不刪**。方向是保守的（少刪測試），但這是你沒有裁定過的做法。(甲) **保留舊條文**：舊條文繼續守平地夾具，峽谷的核心由 A10 守；既有測試零刪除，V14-A18「刪除行數全為 0」不需例外。(乙) **照原裁定刪除**：A18 要加「`AbyssalVanguardLogicTests` 例外」，§8 恢復 T1～T3 的刪改列，且平地夾具的核心從此沒有測試。**➡️ 建議：(甲)**。

---

## 14. 修訂紀錄

- **使用者裁定「甲」（2026-09-30，r3 NEW-1）**：進佔領模式（含第一次進入與回 Lobby）時，英雄站藍出生點 (0,0,−16.65625)；對手站英雄前方約 5m、在畫面內的平地（採 (4,0,−12.5)，12 號藍母）；點對手開局後對手照原本開局流程行動。「進模式時英雄傳回出生點」一併視為使用者已同意（r3 NEW-4）。
- **凍結前審稿修訂（r3，2026-09-30）**：依 `vow-toolchain/v0140-plan-review-r3.md`（r2 的 13 項全數真修好；新問題 BLOCKER 1、MEDIUM 1、LOW 2）。處置全部修訂，「LOW 不修」無。新重算：`v0140-refcs/Extra.cs` 的 `RunR3`（`dotnet run -c Release -- r3` → `refcs-r3-output.txt`）。逐項：
  - **NEW-1（BLOCKER）** 依使用者裁定「甲」改寫 §5.2 Lobby 列：對手 Lobby 座標 (4,0,−12.5)，C# r3 驗證 `ClassAt==Plain`、12 號、不在斜坡矩形、距崖 2.1374 > 0.35、距英雄 5.768，點擊點投影 (536.2, 306.1) 離四邊 ≥ 84px；`CanyonTuning` 常數同步；V14-C04 兩個入口都驗位置、投影可點與「真實點對手→Active」，紅燈欄補畫面外與只修一個入口。
  - **NEW-2（MEDIUM）** V14-B11 兩案開局後對手傳送到 (−17,0,−17) 並 `HoldOpponentForTest(true)`。
  - **NEW-3（LOW）** §5.2 巨獸列的依據改寫為 r3 覆審的 `MoveAlongRoute` 逐幀模擬（換段半徑 0.6、含 Follow 段；最近崖壁 0.780／1.750）。
  - **NEW-4（LOW）** 英雄傳回出生點記為使用者已同意（上方裁定「甲」）。

- **凍結前審稿修訂（r2，2026-09-30）**：依 `vow-toolchain/v0140-plan-review-r2.md`（對 `5c33ab7`：r1 的 35 項中 32 項真的修好、3 項表面修好〔H-2、M-12、M-13〕；新問題 HIGH 2、MEDIUM 2、LOW 6）。處置：表面修好 3 項＋新問題 10 項全部修訂，「LOW 不修」無；使用者裁定未改（N1 屬計畫漏寫的實作要求，不是改規則、也不放寬 B11）。新重算：`v0140-refcs/Extra.cs` 追加 r2 節，輸出改寫到 `refcs-r2-output.txt`（不再覆寫 r1 檔）。逐項：
  - **H-2（表面）／N2** B11 期望路線改依 repo `_route` 表示法（不含起點塊）：第一案 [1,7]、第二案 [9,8,7]、紅燈 [1,7]；§7.3 `CopyRouteForTest` 寫明表示法與出處。
  - **M-12（表面）／N1** §5.2 巨獸列新增實作要求：位移裁切略過 `CanyonTerrain` 底下的命中（高度由 `HeightAt` 決定，水平阻擋只來自崖壁碰撞體、牆、單位）；附 C# r2 路線段到崖壁距離（≥0.674 > 0.53）證明 B11 照此可通過；B11 幀數與斷言不變，紅燈欄加「沒略過地形→卡在 R4 交界」。
  - **M-13（表面）／N3** 按效果改寫：峽谷規格下「進入佔領模式」與「回 Lobby」兩個入口都把對手送到 (−4,0,12)、英雄送到藍出生點 (0,0,−16.65625)（距崖 3.7256，C# r2），y 經 `HeightAt`；C04 加「第一次進入」的同組斷言與英雄位置。
  - **N4** B06 附加案例改用 `HoldOpponentForTest(true)`，加「180 幀都在踏點內」與「換英雄 38 幀內發射」的活性。
  - **N5** §5.2 灰盒推導改為水平掃掠推導，註明只適用於有地面放行的 `HeroLocomotion`。
  - **N6** A11 紅燈欄刪「加成只給藍方」，移到 B08 紅燈欄。
  - **N7** B01 改為 `CanyonTerrain` 底下恰好 67 個 `BoxCollider`、其他碰撞體 0；`CliffBarriers` 明寫為獨立根物件。
  - **N8** §9-B 定義「倒地保持」＝擊倒並於每次復活當幀再擊倒。
  - **N9** 更正 r1 條目「24 項全部修訂」為 23 修＋M-12 接受不修；補記 B05(c) dt 變更。
  - **N10** A20 紅燈欄寫明「受害方單位對攻擊方陣營顯形」。
  - §13 Q14 標為已裁定（使用者「按照建議」＝保留不刪），並記入下方使用者裁定條目；同條補記丁案討論後仍選甲。

- **凍結前審稿修訂（r1，2026-09-30）**：依 `vow-toolchain/v0140-plan-review.md`（對 `8860e23`；BLOCKER 3、HIGH 6、MEDIUM 15、LOW 11，共 35 項）。處置：BLOCKER／HIGH／MEDIUM 24 項中 **23 項修訂、M-12 接受不修（記為灰盒限制）**（r2 依審稿 N9 更正原措辭「24 項全部修訂」）；LOW 11 項全部順手修（「LOW 不修」：無）；轉使用者裁定 1 題（§13 Q14，屬 M-15 的告知與確認）。規則一律未改（開火顯形 §4.7、斜坡任意落差、`IsSameFloor` 用 `CrossesCliff`、Q1～Q11、核心於峽谷 (0,0)/2.5 且平地夾具保留 v0.13.1）；改的是量法、座標、字面值、定義與介面。新字面值的 C# 重算：`vow-toolchain/v0140-refcs/Extra.cs`（`dotnet run -c Release -- r1` → `refcs-r1-output.txt`，重建模型、非 Unity 實跑）。逐項：
  - **B-1** V14-B12 第二組：改為英雄在 G0 落點 (4.59375,1,2.0703125) 面向 −x，牆心 (0.59375,2.0703125)，C# `TileAt`＝0、H＝−1。
  - **B-2** V14-B13：量法寫死為 `RaycastNonAlloc`＋只計 `CanyonTerrain` 根物件下的碰撞體（英雄膠囊、`CliffBarriers` 等不算）；C# 模型本就只含地形盒，數字不變；對照點改為合成腳底位置量測。
  - **B-3** §10 C16：確認為等價突變並刪除，改為 K16「NotifyHit 攻擊方／受害方對調」；A20 加 ② 只顯形攻擊方的斷言（藍方看崖台上的紅對手，含活性）。
  - **H-1** V14-A11 拆成 A11a（`AttackRange` 取值，R0 排第一）與 A11b（新定義的 `CanyonRules.InAttackRange`，目標層不影響組排第一、改用 (8.109375,0) 判「不在」）；§4.1 寫出兩個簽章；K5／K6 排序衝突消失。
  - **H-2** V14-B11 加第二案：`SeedBehemothForTest` 讓巨獸從 2 號出發，`CopyRouteForTest`＝[2,9,8,7]（原鄰接為 [2,1,7]）、240 幀內進 9 號（名目 76 幀）；§7.3 新增兩個入口。
  - **H-3** V14-B08：(b) 門檻改「位移 ≥ 0.03」（三角不等式下限 0.0390625），加 300 幀窗口（C# 名目 108 幀＋出手 ≤90）；(a) 門檻由 0.05 收緊為 0.02（錯誤實作至少走 0.0387）；(c)(d) 補座標與 120 幀窗口。
  - **H-4** 定義 `HoldOpponentForTest(bool)`（不走、不放牆、不前搖，保持可受傷與視野計算），B08／B09／B16 改用它；B16 的「對手開始移動」拆成案例二（命中當幀解除 hold），並加反向案例三。
  - **H-5** V14-C02：開局後英雄傳送到 (−17,0,−17) 並保持倒地。
  - **H-6** D06 等待 3→5 秒（C# 名目落地 2.967 秒）；B04 ① 300→198 幀（C# 名目 340 幀與直線 55.4 幀的中點）；順帶 B06 ① 300→272 幀（名目 467 與直線 76.2 的中點），B03／B05(a) 補名目值。
  - **M-1** 刪改 §3 舊清單、§8 T1～T4 列、§9-A 工作、A10 本文（改寫為 `ForSpec`）、K13（原 C13）外層欄、A18 豁免句。
  - **M-2** G1 框改為「8 個套件（Pact 除外）」；D01 改為掃所有 `ForTest` 方法宣告（排除 `Core/Logic/` 既有慣例）。
  - **M-3** A09 維持字面 0.25；§2.2 改寫；任意落差改由新增 A22 驗收。
  - **M-4** §5.2 寫明「崖壁格」＝距任一崖壁線段 ≤ 0.35 的純幾何定義，禁止用 `IsBlocked`；定義新增方法 `TryFindNearestFreeSameFloor`。A16 加兩句崖壁格斷言。
  - **M-5** A16 與 B05(c) 改用同一個寫死盤面（p0＝(3.875,0)、真實牆尺寸 `StampBox(3.625,0,1,0,2,0.3,0.35)`，C# 驗證有條件→(2.25,−0.25) H＝−1、無條件→(4.75,−0.75) H＝1，差 0.49m）；B05(c) 的活性改在 B05 自己的格點上量；新增 `SpawnRuneWallForTest`。（r2 依審稿 N9 補記：B05(c) 由「dt＝1/3、1/4 各一次」改為 1/60 一次——推出是 `Stamp(+1)` 當幀的一次性瞬移，與幀長無關，低幀率不改變推出結果；屬範圍縮小，於此申報。）
  - **M-6** §4.7 寫死 `RevealUnit`、`RevealTracker` 全部簽章與 `CanSee` 新多載；舊多載保留；tracker 由 `Phase1Bootstrap` 持有。
  - **M-7** §4.5 寫死 `GeothermalVentLogic` 簽章；邏輯層不追蹤飛行／存活／對局狀態，由呼叫端傳 `heroCanTrigger`；A14 (g) 改寫並加活性。
  - **M-8** §6 寫明起點塊＝`TileAt`、−1 時取最近塔心（與 C# 參考模型相同，C01 窗口不變）；A21 加起點塊 −1 的一組。
  - **M-9** §10「紅在哪」逐條重核並更正：K10→(c) 第 21 個 tick、K11→(a) `Cooldown(G1)`、K5 見 H-1；另更正 K2（0 號而非 2 號）、K4／K7／K8／K9 的外層說明。
  - **M-10** 新增 B05(d)：英雄在 G0 落點面向谷底連續 3 次微滑步，不穿崖、y 恆為 1，活性為最後貼到崖壁碰撞體（距線段 ≤ 0.55）。
  - **M-11** 新增 V14-A22：落差 2m 的第 7 條測試斜坡（0→3），內插與 `IsSameFloor` 以 C# 重算的字面值驗收；新增 K18。
  - **M-12** 接受不修：§5.2 記為灰盒限制並附推導（位移不卡、只影響斜坡上半段點地），D08 未驗證清單加 ⑥。
  - **M-13** 峽谷模式 Lobby 對手改傳送到 (−4,0,12)（18 號平原、距崖壁 1.8861）；C04 加斷言。
  - **M-14** §5.2 末加補掃 ④ `new Vector3(` 與 ⑤ `_self.position|clamped`，並列出已知盲區。
  - **M-15** 本條記錄＋§13 Q14 請使用者確認「Q3 改為保留不刪」。
  - **L-1** 突變代號改前綴 K（K1～K18）。**L-2** K9 寫明 (base＋talent)×4/3。**L-3** K13 外層改指 A10 的 `ForSpec` 斷言。**L-4** 刪除 B13 紅燈實作「崖壁碰撞體比 §2 高」。**L-5** 刪除 C01 ④（原編號保留空位，⑤ 不重編）。**L-6** 寫出 `IsInCore`（§3）與地熱點 Active 輸入（§4.5）。**L-7** 改為「斷言一字不改」。**L-8** 附表補 §4.7、§6、A21、A22、B05(d) 等對照列。**L-9** B16 反向案補座標與幀窗口；B09 補 hold 與逐配置幀窗口。**L-10** §8 T5 三分類改為以 `v0140-prefreeze-tests.md` §1 為準（已完成）。**L-11** §9 共同遵守加 B／C 步一致性檢查的明文例外。
  - 順帶更正（非審稿編號）：§2.4 G0 落點到崖壁的距離 0.8457→1.2246（C# 重算）。
- **裁定（2026-09-30，使用者）**：§13 Q1～Q11 全部照建議（Q1＝甲 6 處斜坡；使用者先問丁案利弊，主對話回答後仍選甲）；另加兩項工程調整：斜坡支援任意落差（§2.2）、`IsSameFloor` 改用 `CrossesCliff`（§5.2）。開火顯形納入本批（使用者「照建議」）：§4.7、V14-A20、B16、突變 C15～C16（r1 起＝K15～K16）。丁案（加 0→3、0→6 崖台↔谷底斜坡）經使用者詢問利弊、主對話回答後，使用者仍選甲。**§13 Q14（2026-09-30，使用者「按照建議」）**：保留 v0.13.1「核心圈不得與任何佔塔圈重疊」等 `AbyssalVanguardLogicTests` 舊條文、不刪（核心依規格取用，平地夾具仍守 v0.13.1）。
- **草案（2026-09-30）**：依 `vow-toolchain/v0140-alignment.md` 起草。可達性、斜坡淨寬、高度連續與遮擋由 `vow-toolchain/v0140-reachability.py` 推導（重建模型）；斜坡寬度從 3.0m 改為 3.5m 的理由見 §2.2（3.0m 時崖台斜坡格點淨寬只剩 1.15m）；地熱點座標以腳本搜尋（原本畫在示意圖位置的初值 (2.59375,1.5)／落點 (3.96875,2.2890625) 在格點上被外擴蓋掉，改用搜尋結果）。

- **凍結（2026-09-30）**：r3fix 覆審可凍結；凍結前補 R3F-1（開局定義加「點 CAPTURE 後等 30 幀」）。R3F-2（LOW）不修：N1 巨獸逐幀模擬只存在覆審者暫存副本、無法由 toolchain 重現，僅作為凍結前可行性依據；B11 本身在 Unity PlayMode 實跑驗收，不依賴該模擬數字。

## 附表：裁定 → 條文對照

| 裁定 | 規則章節 | 驗收 |
|---|---|---|
| 版面 C、南北鏡像 | §2.1 | A01、A05 |
| Q1 甲 核心谷心 2.5m | §3 | A10、B11 |
| Q2 三層＋6 斜坡 | §2.2、§2.3、§5 | A02～A09、B01～B05 |
| Q2 崖台規則 | §4.1～§4.3 | A11、A12、B08、B09 |
| Q2 地熱點 | §2.4、§4.5 | A14、B07、D06 |
| Q3 雙層接口 | §1.3 | A01 |
| Q4 高度與遮擋 | §5、§9-B13 | A02、B03、B13 |
| Q5 淺水 | §4.4 | A13、B10 |
| Q6 AI 只走斜坡 | §6 | B06、C01、C02、A21 |
| Q7 崖台不能直接跳下（照裁定） | §5.2 崖壁實體 | B05(d)、B01 |
| 開火顯形（使用者 2026-09-30 裁定） | §4.7 | A20、B16 |
| Q4 選塔走路距離 | §6 | A21、C01 |
| 斜坡任意落差（使用者 2026-09-30 工程裁定） | §2.2 | A22 |
| `IsSameFloor` 用 `CrossesCliff`（使用者 2026-09-30 工程裁定） | §5.2 | A16、A22、B05(c) |
