# 計畫：v0.6.1 —— 元素反應的回饋（反應名稱飄字＋受困狀態顯示）（2026-09-21）

> 已知良好狀態：main `da38634`（v0.6.0，`origin/gh-pages ded1c4d`；純邏輯 193＋1 略過、突變 110/110、EditMode 190 過／0 紅／4 略過、PlayMode 89/89）。工作分支 `v061-feedback`。
> 本檔 §3 為凍結驗收條件；要動它只能走 `02 §2.1`。

## 0. 起因與使用者裁定

使用者真機試玩 v0.6.0（橫式）回報「流沙困住你」「爆沸」沒有效果。線上 Playwright 四組重現，反應全部有發生；使用者確認「黃褐色圈有出現、但照樣走得動」。以人手時序實測（按完 `ENEMY WALL` 後 1.1s 才點地）：1.48s 英雄仍在圈緣、2.0s 已在圈外——縛足 1.2s 大半耗在手指移動上，看得到的受困只剩約 0.1s（截圖 `vow-toolchain/browser-screenshots/v060-r1-D1～D3`）。爆沸那一項使用者「不確定有沒有扣血」，成因未定（FIRE 冷卻中／`ELEM` 停在 RED 變救援／80 只佔血條 13% 且被符印牆血條蓋住）。**共同問題：畫面上沒有任何東西告訴玩家剛才發生了哪個反應。** 批 4 的驗收全部量「狀態」，沒有一條量「人看不看得出來」。

使用者裁定（2026-09-21）：「加回饋」——照主對話提案：**反應成立時跳反應名稱飄字；受困時 `STATE` 列顯示 `ROOTED`／`SLOWED`；不動任何裁定數值。**

主對話自決（回報時載明，可推翻）：縛足開始時在英雄頭上另跳一次 `ROOTED`（玩家看的是英雄不是左上角面板）；飄字用英文大寫（與 HUD 一致）；空地火、風沒掃到燃燒區、單放水**不跳字**。

## 1. 要做的事

- 新增 `Core/Logic/ElementCalloutLogic.cs`（純邏輯、零配置）：
  - `public static int LabelIndexFor(ElementReaction reaction)`：Quicksand→0、Steam→1、Firestorm→2、Boil→3、Rescue→4；None／PlainFire／WaterPool→−1。
  - `public static int HeroStatusIndex(bool isRooted, float speedMultiplier)`：縛足→1（`ROOTED`）；未縛足且 `speedMultiplier < 1f`→2（`SLOWED`）；否則 0。縛足優先。
- 新增 `Combat/Feedback/ReactionCalloutDisplay.cs`：池化的 `TextMesh` 飄字（作法比照 `Combat/TargetOverheadDisplay.cs:52-67`：`Awake` 內 `new GameObject`＋`AddComponent<TextMesh>`、`LegacyRuntime.ttf`；**不重建場景**），永遠正對鏡頭。`Show(Vector3 worldPosition, int labelIndex)`；池 6 個，滿了重用最舊的；顯示 `CalloutSeconds = 1.2f`，上飄。字要比傷害數字大（`characterSize ≥ 0.10`）、各反應不同色、離地 ≥2.5m 不被牆擋。標籤表在初始化時建好一次：`"QUICKSAND"`、`"STEAM"`、`"FIRESTORM"`、`"BOIL " + BoilDamage`（＝`"BOIL 80"`）、`"RESCUE"`、`"ROOTED"`；**執行期不得字串串接**。對外唯讀：`ShowCount`、`LastLabel`、`LastWorldPosition`、`ActiveCount`。
- `Combat/ElementField.cs`：反應結算處（`:214-241` 一帶，與 Combo 反饋同一處）呼叫 `Show`。位置：流沙／蒸氣＝被消耗水域的圓心；爆沸／救援＝被終止流沙的圓心；火浪＝被消耗燃燒區的圓心。`_callouts == null` 時完全跳過。
- `Core/HeroController.cs`：縛足由 false→true 的那一幀送出一次事件或回呼（**同一個流沙重入不再縛足、所以也不再跳**）；`Bootstrap/Phase1Bootstrap.cs` 接到後在英雄位置 `Show(…, ROOTED)`，並在初始化時 `AddComponent<ReactionCalloutDisplay>()` 後注入 `ElementField`。
- `UI/DebugHud.cs`：`STATE` 那一列的值，`HeroStatusIndex` 為 1／2 時改顯示 `ROOTED`／`SLOWED`（常數字串），為 0 時照舊顯示狀態機名稱。**不加列、`InfoRows` 不動、`DebugHudLayout.cs` 零改動。** `Phase1Bootstrap` 對外多開唯讀 `HudStateLabel` 供 PlayMode 測試讀（PlayMode asmdef 看不到 `Vow.UI`，比照批 4 的做法）。
- `Core/VowVersion.cs`→`0.6.1`（`ProjectSettings.asset` 的 `bundleVersion` 同步，比照 `0d9654c`）；`docs/PHASE1_ACCEPTANCE_GUIDE.md` 新增 §15（玩法變化、自決、已知限制、驗證紀錄；線上實測小節留 `（待主對話部署後以 Playwright 實測填入）`，不得編數字）。

## 2. 不做什麼

不動 `ElementTuning.cs` 任何數值、`ElementReactionLogic`／`QuicksandStatusLogic`／`SteamConcealmentLogic` 等步驟 A 邏輯檔；不改 `ARCHITECTURE.md`／`GDD.md`／`Core/Contracts` 既有介面／任何 asmdef／`DebugHudLayout.cs`／`verify.sh`／`PureLogic.Tests.csproj`；不重建場景（`Assets/Scenes/` 零改動）；不修「符印牆血條蓋住木樁血條」；不加音效、粒子、著色器。

## 3. 驗收條件（凍結；括號＝什麼樣的壞實作會讓這條變紅）

容差沿用批 4：位置 0.10m、時間 0.034s（兩個 60fps tick）、`Time.captureDeltaTime = 1/60f`。新測試一律非參數化。

- **F1-a `LabelIndexFor_MapsTheFiveReactions_AndReturnsMinusOneForTheRest`**：五個反應回 0～4 且互不相同；None／PlainFire／WaterPool 回 −1。（全部回同一個索引；空地火也跳字）
- **F1-b `HeroStatusIndex_RootedWins_ThenSlowed_ThenNone`**：`(true, 0.65f)→1`、`(true, 1f)→1`、`(false, 0.65f)→2`、`(false, 1f)→0`。（優先序反過來；永遠回 0）
- **F1-c 突變**：`mutation_check.py` 尾端追加 ≥4 筆（Boil↔Rescue 索引對調、PlainFire 回 0、`isRooted` 分支拿掉、`< 1f` 改 `<= 1f`），各指向上面兩條之一；既有 110 筆一字不動，總數全部 CAUGHT。
- **F2-a～e（PlayMode，各一條，真實路徑做出反應）**：流沙／蒸氣／火浪／爆沸／救援成立的那一幀起——① `ShowCount` 恰 +1（流沙那條另有英雄頭上的 `ROOTED`，見 F3，所以該條以 `LastLabel` 序列或分開計數驗「反應飄字恰一次」，作法自定但要寫進回報）② 標籤逐字＝`"QUICKSAND"`／`"STEAM"`／`"FIRESTORM"`／`"BOIL 80"`／`"RESCUE"`（測試裡寫死字面值，不讀標籤表）③ 位置的 x、z 與 §1 指定的圓心相差 ≤0.10m ④ 場上存在一個 `activeInHierarchy` 的 `TextMesh`，其 `text` 等於該標籤。（沒接線；爆沸與救援標籤寫反；位置用了英雄座標）
- **F2-f 反例（同一條測試內先做活性）**：先做一次蒸氣確認 `ShowCount` 會動，再依序：單放 `WATER`、空地 `FIRE`、沒有燃燒區時 `WIND`、牆立在水域外——`ShowCount` 都不變。（什麼施放都跳字）
- **F3-a `Rooting_ShowsRootedOverTheHero_Once_AndTheStateRowFollows`**：紅流沙困住英雄的那一幀起：英雄頭上 `ROOTED` 飄字恰一次（位置 x、z 與英雄相差 ≤0.10m）；`HudStateLabel == "ROOTED"`；縛足結束（成形後 1.21s）且仍在圈內→`"SLOWED"`；走出圈外→回到狀態機名稱（不是這兩個字）。走出去再走回同一個流沙：`ROOTED` 飄字**不再出現**、標籤直接是 `"SLOWED"`。（每幀都 Show；重入又跳；標籤沒接 HUD）
- **F3-b 藍流沙對藍英雄**：不跳 `ROOTED`、`HudStateLabel` 維持狀態機名稱。（不看陣營）
- **F4 顯示時長**：`Show` 後 1.15s 該 `TextMesh` 仍 `activeInHierarchy`；1.25s 後已停用。（不會消失；一閃即逝）
- **F5 池滿**：連續 `Show` 7 次→`ActiveCount == 6`、無 `LogError`／例外、第 7 次的標籤確實出現在某個啟用中的 `TextMesh` 上。（第 7 次靜默丟掉）
- **F6 零配置**：`ZeroAllocationTests` 的既有量測窗口內（`Batch4Driver` 本來就會做出三個 Combo）斷言「窗口內 `ShowCount` 增量 ≥3」且 `HudStateLabel` 曾為 `ROOTED`／`SLOWED` 各 ≥1 幀；`UpdateBytes == 0`／`LateUpdateBytes == 0`、既有所有 deadline／幀數／容差**一個數字都不准動**，`ZeroAllocationTests.cs` 對既有行刪除數＝0。**鑑別力證據（必附）**：在 `Show` 裡注入一次 `label + ""` 之類的配置→該測試必須紅；用改壞前的備份副本還原；貼紅、綠兩次實際輸出。
- **F7 不退步**：`verify.sh` ALL PASS（純邏輯 ≥193＋1 略過＋新增）；Unity EditMode 0 紅；PlayMode 既有 89 條全在全綠＋新增全綠；`git diff --stat da38634..` 對 §2 清單全空；`git diff --numstat da38634.. -- Assets/Tests/` 對既有測試檔刪除行數＝0；無新增 `[Ignore]`／`[Explicit]`／`[Retry]`。
- **F8 線上（主對話做）**：`git log origin/gh-pages -1` 為 v0.6.1、線上首頁版本列 `v0.6.1`；Playwright（844×390、`deviceScaleFactor: 2`、CDP 觸控）console 無 error；**人手時序**：按 `ENEMY WALL` 後 0.3s 的截圖同時看得到 `QUICKSAND` 飄字、英雄頭上 `ROOTED`、`STATE ROOTED`；清單第 3 項按 `FIRE` 後 0.3s 截圖看得到 `BOIL 80`；同盤面 `ELEM: RED` 則看得到 `RESCUE`。截圖 `vow-toolchain/browser-screenshots/v061-*`。
