# 《VOW 誓約》Phase 1 灰盒原型 — 驗收測試指南

> 對應規格：`ARCHITECTURE.md` v1.0.0、`GDD.md` v3.4.1
> 引擎：Unity **2022.3.62f1 LTS**（規格禁止 Unity 6）

---

## 0. 先講清楚：哪些驗過了、哪些還沒

下表是誠實的現況。「已驗證」一律附可重跑的指令；沒有實跑證據的一律標「未驗證」。

| 項目 | 狀態 | 證據 |
|------|------|------|
| 手感核心邏輯（狀態機、目押窗口、預輸入緩衝、指令佇列、攻擊週期、充能、動能衰減、手勢換算、輸入路由、觸控槽位、震屏） | **已驗證** | 62 個 NUnit 測試：dotnet 下全綠，**Unity 2022.3.62f1 Test Runner (EditMode) 下也全綠**。另做突變測試（故意改壞實作 11 種），對應測試全部變紅 |
| 在 Unity Editor 內實際編譯、套件解析、asmdef 解析 | **已驗證** | batchmode 首次開專案 exit 0、0 個 `error CS`、我方程式碼 0 警告；`Library/ScriptAssemblies/` 產出全部 8 個 `Vow.*.dll` |
| `#if VOW_HAS_URP` 區塊（建立 URP 管線資產） | **已驗證** | `Vow.Editor.rsp` 內含 `-define:VOW_HAS_URP` 與 `GreyboxAssetFactory.cs`，編譯通過；實跑後產出 `VOW_URP.asset`（Forward+）並寫入 GraphicsSettings |
| `VOW/Phase 1/Build Greybox Scene` 實跑 | **已驗證** | batchmode `-executeMethod` exit 0、無例外；產出場景、NavMesh 資產（非空）、AnimatorController、5 支切片、9 顆材質；場景內 `NavMeshObstacle` 元件 0 個；`activeInputHandler: 1` |
| `OnAttackHit()` 動畫事件由 Animator 實際觸發、前搖約 0.25s | **已驗證（佔位骨架）** | PlayMode 測試 `Attack_IsResolvedByTheAnimationEvent…`；把事件名改壞後該測試變紅（Unity 回報 AnimationEvent has no receiver） |
| 佔位骨架的動畫曲線真的生效 | **已驗證** | 同上測試量到前搖期間右臂偏轉 > 30°；建置時逐條曲線綁定的解析自檢無報錯 |
| 點地移動（NavMeshAgent 手動位移）、窗口內微彈 0 幀切後搖並實際滑出 1.4m | **已驗證** | PlayMode 測試 `TapToMove…`、`FlickInsideTheWindow…` |
| 邊緣滑步不會掉出平台／NavMesh（兩道防線） | **已驗證** | PlayMode 測試 `DashingOutwardAtTheArenaEdge…`；停用邊界牆後該測試變紅（x 由 ≤19.46 變成 19.5，證明牆與 NavMesh 夾回是兩道獨立防線且都有效） |
| Humanoid FBX 連動（紅線 3 的正式載體） | **未驗證** | 專案內沒有 FBX，目前一律走佔位骨架；需要 §5 |
| 真實觸控 → `PlayerInputService` → 射線判定 | **未驗證** | PlayMode 測試以腳本化輸入取代觸控；EnhancedTouch 與 TouchSimulation 的實際相位行為需要人工操作確認（§4） |
| 畫面：Hitbox 線框、預警箭頭是否平貼地面、傷害飄字、血條、閃白、貼花、HUD 排版 | **未驗證** | batchmode 看不到畫面；需要 §4 |
| 手感、120Hz 實機、Profiler 0 GC、Hitstop／震屏觀感 | **未驗證** | 需要 §4、§6 |

> **綠燈的涵蓋範圍**：62 個 EditMode 測試只覆蓋純邏輯層；4 個 PlayMode 冒煙測試覆蓋「場景載得起來、走得動、打得到、滑得出去、出不了界」。
> 材質與渲染結果、真實觸控、手感——**沒有任何自動化測試**，只能靠 §4~§6 的人工實測。請不要把「測試全綠」解讀成整體健康度。

重跑驗證：

- 不需要 Unity：雙擊 `Tools/DotnetCheck/verify.bat`（需 .NET 8 SDK 與 `../vow-toolchain/refs` 參考組件）
- 需要 Unity（把 `<U>` 換成 `"C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"`，`<P>` 換成本專案路徑）：
  - 重建場景：`<U> -batchmode -quit -projectPath <P> -executeMethod Vow.EditorTools.VOWPhase1SceneBuilder.Build -logFile build.log`
  - EditMode：`<U> -batchmode -projectPath <P> -runTests -testPlatform EditMode -testResults edit.xml -logFile edit.log`
  - PlayMode：`<U> -batchmode -projectPath <P> -runTests -testPlatform PlayMode -testResults play.xml -logFile play.log`

---

## 1. 開啟專案

1. Unity Hub →「新增 (Add)」→ 選本資料夾 → 用 **2022.3.62f1** 開啟。
2. 第一次開啟會解析 `Packages/manifest.json`（URP、Input System、AI Navigation、Test Framework）。
3. 若跳出「是否啟用新的 Input System 後端並重啟」→ 選 **Yes**。
4. **驗收點**：Console 沒有紅色編譯錯誤。有的話請整段貼回來——這是第一個要排除的風險。

### 1.1 開 Unity 後的前 15 分鐘（依此順序，前一項沒過就先停下來回報）

這 7 步對應獨立審查點名的高風險項，全部通過才輪得到「手感對不對」的問題：

1. 開專案 → Console **有沒有紅色 CS 編譯錯誤**。特別留意 `GreyboxAssetFactory.cs` 的 URP 區塊——它是全專案唯一沒經過任何編譯器的程式碼；若它編不過，整個 Editor 組件失效、`VOW` 選單不會出現。
2. **VOW ▸ Phase 1 ▸ Build Greybox Scene** → 若出現「已將 Active Input Handling 切換…請重新啟動」，**重啟 Editor 後再 Build 一次**。（若 Unity 自己先跳出「是否啟用新 Input System 後端」對話框，請選 **Yes**；選了 No 的症狀是英雄完全不動、Console 只有一行黃字。）
3. Play → Console 有沒有紅字「前搖逾時仍未收到動畫事件」。有＝動畫事件沒觸發，每一刀都在等 0.6 秒保險，所有手感數值失真。
4. 佔位骨架的四肢**會不會動**（跑步擺手、揮刀）；點地板會不會走。
5. 走到場地邊緣，朝外連滑三次 → 應被隱形邊界擋住，不會滑出平台。
6. 兩指測試：一指按住左下微輪盤，另一指點「Switch to A/B」→ 英雄不得憑空走動。
7. 按 **Hitbox ON** → 線框畫不畫得出來。

## 2. 生成灰盒場景

選單 **VOW ▸ Phase 1 ▸ Build Greybox Scene**。它會：

- 建立並啟用 URP 管線資產（Forward+），存於 `Assets/Settings/`
- 生成 40m × 40m 棋盤格平地（**每格 1 公尺**，滑步距離可直接用地磚目測）
- **在只有地板時**烘焙靜態 NavMesh，存成 `Assets/Scenes/VOW_Phase1_NavMesh.asset`
- 生成 `Hero_Player`、`Dummy_Target`、`TestWall_A`、`TestWall_B`、鏡頭（52° 俯視）與 `VOW_Systems`
- 存成 `Assets/Scenes/VOW_Phase1_Greybox.unity` 並加入 Build Settings

**驗收點**：
- [ ] Console 出現 `[VOW] Phase 1 灰盒場景已生成`，沒有例外
- [ ] 沒有 FBX 時會出現一則黃色警告「改用程式生成的佔位骨架」——這是預期行為（見 §5）
- [ ] Hierarchy 裡任何物件都**沒有** `NavMeshObstacle` 元件（紅線 5）
- [ ] 若 Console 提示「已將 Active Input Handling 切換…請重新啟動」→ 重啟 Editor

## 3. 自動化測試（Unity 內）

Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All。
**驗收點**：EditMode 62 個、PlayMode 4 個全綠（EditMode 與 dotnet 下跑的是同一份原始碼：`Assets/Tests/EditMode/`）。

## 4. 手感驗收（按 Play）

左上角 HUD：`FPS`／`PANEL HZ`（螢幕實際刷新率）／`STATE`／`PIPS`（3 格充能）／`WINDOW`（目押窗口倒數條）／`MODE`／`RIG`（`HUMANOID` 或橘字 `PLACEHOLDER`），以及兩顆按鈕 **Switch to A/B**、**Hitbox ON/OFF**。
桌機上滑鼠會被模擬成觸控，與手機走同一條程式路徑。

### 4.1 點地移動與普攻
- [ ] 點地板 → 英雄走過去，`STATE` = `Moving` → 到點變 `Idle`
- [ ] 點木樁 → 走進射程（5m）後自動普攻：`AttackWindup` → `AttackRelease` → `AttackRecovery` → `Idle` 循環
- [ ] 木樁受擊：閃白、晃動、頭頂飄黃色傷害數字、血條縮短；血量歸零後 2.5 秒復活
- [ ] 每一刀命中都有短促頓挫（40ms）與輕微震屏

### 4.2 前搖可打斷（0.25s）
- [ ] 舉刀瞬間點地板 → **立刻**轉身走開，木樁**不掉血**

### 4.3 目押切後搖（核心）
- [ ] **模式 A**：命中瞬間 `WINDOW` 黃條滿格並開始縮短。黃條還在的時候，在螢幕任意處快速微彈（約 3.5mm 以上）→ 英雄**瞬間**朝該方向滑出，`STATE` = `CadenceDashing`，`PIPS` 少一格
- [ ] 稍微早一點彈（命中前約 0.1 秒）也會在命中幀自動滑出（120ms 預輸入緩衝）
- [ ] 連續三刀都切後搖：滑步距離依序約 **1.4 → 0.9 → 0.5 格地磚**
- [ ] 停手超過 1 秒再切 → 回到 1.4 格
- [ ] 充能用完後微彈 → 什麼都不發生，英雄照常收刀、照常出下一刀（不罰站）
- [ ] 充能每 2.5 秒回一格，`PIPS` 上看得到正在回充的那格由左往右填滿

### 4.4 Attack Frame Lock
- [ ] 站著不打、或走路中微彈 → **不會**滑步、不扣充能

### 4.5 紅線 1：狂點不卡刀
- [ ] 對著木樁瘋狂連點 10 秒 → 出刀節奏穩定不變，英雄從不原地發呆；Console 沒有 `非法狀態轉換` 的紅字
- [ ] 狂點途中隨時點地板 → 最慢 0.15 秒內就會走開
- [ ] 在射程內左右交替狂點木樁與石牆 → 照常出刀（改鎖不重置前搖），不會永遠舉著刀打不出去

### 4.6 紅線 2：走 A 不加攻速
- [ ] 計時 10 秒：「每刀都切後搖」與「站樁不動」的**出刀數相同**（週期 0.8s → 約 13 刀）

### 4.7 模式 B（雙手分離微輪盤）
按 **Switch to B**。左下角出現淡色方塊＝微輪盤判定區。
- [ ] 右手點地／點目標：**按下瞬間**就反應（不用等放開）
- [ ] 左手在左下區按住並往某方向推過內圈 → 英雄腳下出現**青色箭頭**預告滑步落點，箭頭長度隨連段衰減變短
- [ ] 推著不放去攻擊 → 每次命中幀自動朝該方向滑出
- [ ] **高壓紅線**：只推微輪盤、不點任何東西 → 英雄**絕對不會走動**
- [ ] 桌機可用 WASD／方向鍵代替左手拇指

### 4.8 防誤觸
- [ ] 從螢幕最左、最右、最底 8px 內起手的觸控完全無效
- [ ] 點 HUD 按鈕時，英雄不會朝按鈕底下的地面走

### 4.9 石牆與打擊反饋
- [ ] 走路與滑步都會被石牆擋住、沿牆滑動，**不會穿牆**
- [ ] 場地四周有隱形邊界：走路與滑步都出不了 40m × 40m
- [ ] 點石牆可攻擊；打碎瞬間**重震**並在地面留下紫色貼花（3 秒後淡出）；6 秒後石牆重生
- [ ] 鎖定目標時英雄腳下有一圈青色射程環

### 4.10 Hitbox
- [ ] 按 **Hitbox ON** → 英雄（綠）、木樁與石牆（橘）的碰撞線框出現，且不被模型遮住

## 5. 換上 Mixamo Humanoid（紅線 3）

1. 到 mixamo.com 下載 **Y Bot** 或 **X Bot**：Format = FBX for Unity，**With Skin**。
2. 再下載五個動作（**Without Skin** 即可，勾 *In Place*），檔名需含關鍵字：

   | 切片 | 檔名需含其一 |
   |------|--------------|
   | Idle | `idle` |
   | Run | `run` / `jog` / `sprint` |
   | Attack | `attack` / `slash` / `punch` / `swing` / `shoot` / `strike` |
   | Dash | `dash` / `dodge` / `roll` / `step` / `slide`（缺少時借用 Run） |
   | Hit | `hit` / `react` / `impact` / `damage`（缺少時借用 Idle） |

3. 全部丟進 `Assets/Art/Characters/`，重跑 **VOW ▸ Phase 1 ▸ Build Greybox Scene**。

**驗收點**：
- [ ] Console 出現 `[VOW] 已採用 Humanoid FBX`
- [ ] 選取 Attack 的 FBX ▸ Animation 分頁 ▸ Events：時間軸 40% 處有 `OnAttackHit` 事件
- [ ] Play 後不再出現「佔位骨架」警告，也**沒有**「前搖逾時仍未收到動畫事件」的紅字
- [ ] 若揮刀的視覺命中點與傷害不同步 → 調 `HeroRigFactory.AttackHitNormalizedTime`（預設 0.4）後重建場景

## 6. 效能紅線（Profiler）

- [ ] Window ▸ Analysis ▸ Profiler，Play 後連打 30 秒：CPU ▸ **GC Alloc 欄在穩定狀態下為 0 B**
  （Editor 內 `GetComponent` 找不到物件時會配置，屬 Editor 專有；如有疑慮以 Development Build 為準）
- [ ] **量 GC 的那一輪請先停用 `VOW_Systems` 上的 `DebugHud` 元件**：IMGUI 引擎內部每幀可能有配置，開著 HUD 量到的數字無法歸因給遊戲本體
- [ ] 另外確認這兩處（靜態審查無法判定，只能實測）：`PlayerInputService.Update` 讀 `Touch.activeTouches`、`DebugHud.OnGUI`
- [ ] 120Hz 螢幕上 HUD 的 `FPS` 貼近 120，且 `PANEL HZ` 顯示 120（顯示 60 代表面板或系統把刷新率鎖住了；iPhone 需確認 ProMotion 已對 App 開放）

## 7. 調手感

所有數值集中在 `Assets/Settings/HeroTuning.asset`（重建場景**不會**洗掉已調過的值）。

| 欄位 | 預設 | 來源 |
|------|------|------|
| Combat ▸ WindupSeconds | 0.25 | GDD |
| Combat ▸ CadenceWindowSeconds | 0.22（可調至 0.25） | GDD |
| Combat ▸ InputBufferSeconds | 0.12 | GDD |
| Combat ▸ RecoverySeconds | 0.15 | GDD |
| Combat ▸ DashDistances | 1.4 / 0.9 / 0.5 | GDD |
| Combat ▸ ChargeRecoverySeconds / MaxCharges | 2.5 / 3 | GDD |
| **Combat ▸ AttackPeriodSeconds** | **0.8** | **GDD 未定義，灰盒暫定值** |
| **Combat ▸ DashDurationSeconds** | **0.12** | **GDD 未定義，灰盒暫定值** |
| MoveSpeed / AttackRange / AttackDamage | 5.5 / 5 / 60 | 灰盒暫定值 |

## 8. 規格留白處的解讀（請確認或推翻）

1. **1.0 秒衰減窗口是「距上一次滑步」的滾動窗口**，不是從第一次起算。理由：滑步綁定普攻命中，攻擊週期 ≥0.6s 時，「從第一次起算 1 秒內滑三次」在物理上不可能發生，第三段 0.5m 永遠觸發不了。
2. **3.5mm 是觸發半徑，7.5mm 是飽和半徑**：位移越過 3.5mm 的瞬間即判定微彈（不等手指離開，延遲最低）；超過 0.25 秒才越過的慢速拖曳既不算微彈也不算點擊。
   → **此項請在盲測前裁定**：GDD 字面是「位移 3.5mm ~ 7.5mm 範圍內觸發」。若你的本意是「超過 7.5mm 的大幅滑動不算微彈」，
   現行實作會把它也算成微彈（只靠 0.25 秒時限排除慢速拖曳）。兩種解讀的手感不同，不能等 10 人測完才發現理解不一致。
3. **目押窗口中點地板 = 提前關窗**，走 0.15s 收招後步行（GDD 流程圖的「常規走 A」）；收招與滑步期間的指令一律排隊、後到覆蓋先到。
4. **攻擊週期以「前搖起手時刻」為錨**，被打斷的前搖不消耗週期。
5. **模式 B 微輪盤推著不放 = 每次命中都滑**（直到放手或充能用完）。若希望「每推一次只滑一次」，是一行改動，請告知。
6. **Hitstop 只凍結動畫播放速度，不動 `Time.timeScale`**——頓挫期間 220ms 窗口照常倒數、輸入照常取樣；目押成功會立即解除頓挫，滑步動畫不被卡住。
7. **前搖中改點射程內的另一個目標**：只換目標、保留前搖進度，這一刀打在新目標上；改點射程外的目標才會中斷前搖去追。
8. **動畫事件遺失的保險**：前搖超過 0.6 秒仍未收到 `OnAttackHit()` 會強制結算並噴紅字，避免英雄永遠卡住。這是設定錯誤的警報，不是正常路徑。

## 9. 刻意沒做的（冷庫協議與範圍界線）

- 19 塊圍棋演算法、戰爭迷霧、誓約天賦、深淵先鋒、大廳匹配、高級著色器、粒子特效 —— 冷庫協議
- `IRuneWall`（壽命、穿透、坍塌）—— 只保留介面契約；測試石牆走 `ICombatTarget`
- 繞牆尋路（0.5m 格點向量場）—— 屬 Phase 2；Phase 1 只保證撞牆會被擋住
- **`ARCHITECTURE.md` §貳 列在 Phase 1、但不在本次交付清單內的兩項**：50~80ms 網路延遲注入、CoreHaptics 震覺管理 —— **尚未實作**，需要的話另開一輪

### 已知但這一輪沒處理的項目（來自獨立審查，`vow-toolchain/REVIEW-r1.md`）

- 射程內、外兩個目標交替狂點會一直打不出去——判定為設計如此：點射程外目標等同「去追它」的移動型指令，前搖依規格可被打斷（與交替點目標／點地板同理）
- 預警指示器的 `LineRenderer` 以 `LineAlignment.TransformZ` ＋ 旋轉 90° 平貼地面——在 `useWorldSpace = true` 下是否成立，需實機確認；若箭頭變成面向鏡頭的緞帶，就是這裡
- 傷害飄字用內建字型材質（`GUI/Text Shader`），URP 下應可顯示但未實測；飄字看不見時先查這裡
- `ICadenceMover.OnDashExecuted` 送出的是起手時宣告的距離，撞牆被裁切時不是實際位移

## 10. 程式架構速覽

```
Assets/Scripts/
  Core/Contracts/   ARCHITECTURE §參／§陸 的介面與列舉（簽章逐字對應）
  Core/Logic/       純 C#、零 UnityEngine 依賴：HeroCombatBrain、PlayerStateMachine、CadenceSim、GestureMath、TraumaShake
  Core/             HeroController（組裝）、HeroLocomotion、MicroCadenceMover、HeroTuningAsset
  Input/            PlayerInputService（EnhancedTouch）、InputRoutingManager
  Combat/           目標、木樁、測試石牆、頭頂血條與飄字
  Combat/Feedback/  CombatFeedbackService、SkillTelegraphService
  Animation/        HeroAnimationDriver（OnAttackHit 入口）、HeroAnimatorContract
  UI/               DebugHud、HitboxVisualizer
  Bootstrap/        Phase1Bootstrap（組裝根）、FollowCameraRig、CadenceAimPreview
  Editor/           VOWPhase1SceneBuilder、HeroRigFactory、GreyboxAssetFactory
Assets/Tests/EditMode/   與 dotnet 共用的 NUnit 測試
Tools/DotnetCheck/       不需 Unity 的驗證工具鏈
```

依賴方向只有一條：`Bootstrap → {UI, Combat, Input, Animation} → Core`，由 asmdef 在編譯期強制。
`Core/Logic` 的 `CadenceSim.SimulateStep` 是純函數，30/60 Tick 客戶端預測可直接重播。
