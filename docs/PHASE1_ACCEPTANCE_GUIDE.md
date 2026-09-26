# 《VOW 誓約》Phase 1 灰盒原型 — 驗收測試指南

> 對應規格：`ARCHITECTURE.md` v1.0.0、`GDD.md` v3.4.1
> 引擎：Unity **2022.3.62f1 LTS**（規格禁止 Unity 6）

---

## 0. 先講清楚：哪些驗過了、哪些還沒

下表是誠實的現況。「已驗證」一律附可重跑的指令；沒有實跑證據的一律標「未驗證」。

| 項目 | 狀態 | 證據 |
|------|------|------|
| 手感核心邏輯（狀態機、目押窗口、預輸入緩衝、指令佇列、攻擊週期、充能、動能衰減、手勢換算、輸入路由、觸控槽位、震屏） | **已驗證** | 70 個 NUnit 測試：dotnet 下全綠，**Unity 2022.3.62f1 Test Runner (EditMode) 下也全綠**。另做突變測試：`python Tools/DotnetCheck/mutation_check.py` 把實作故意改壞 18 種，18 種都使對應測試變紅、還原後全綠 |
| 在 Unity Editor 內實際編譯、套件解析、asmdef 解析 | **已驗證** | batchmode 首次開專案 exit 0、0 個 `error CS`、我方程式碼 0 警告；`Library/ScriptAssemblies/` 產出全部 8 個 `Vow.*.dll` |
| `#if VOW_HAS_URP` 區塊（建立 URP 管線資產） | **已驗證** | `Vow.Editor.rsp` 內含 `-define:VOW_HAS_URP` 與 `GreyboxAssetFactory.cs`，編譯通過；實跑後產出 `VOW_URP.asset`（Forward+）並寫入 GraphicsSettings |
| `VOW/Phase 1/Build Greybox Scene` 實跑 | **已驗證** | batchmode `-executeMethod` exit 0、無例外；產出場景、NavMesh 資產（非空）、AnimatorController、5 支切片、9 顆材質；場景內 `NavMeshObstacle` 元件 0 個；`activeInputHandler: 1` |
| `OnAttackHit()` 動畫事件由 Animator 實際觸發、前搖約 0.25s | **已驗證（佔位骨架）** | PlayMode 測試 `Attack_IsResolvedByTheAnimationEvent…`；把事件名改壞後該測試變紅（Unity 回報 AnimationEvent has no receiver） |
| 佔位骨架的動畫曲線真的生效 | **已驗證** | 同上測試量到前搖期間右臂偏轉 > 30°；建置時逐條曲線綁定的解析自檢無報錯 |
| 點地移動（NavMeshAgent 手動位移）、窗口內微彈 0 幀切後搖並實際滑出 1.4m | **已驗證** | PlayMode 測試 `TapToMove…`、`FlickInsideTheWindow…` |
| 邊緣滑步不會掉出平台／NavMesh（兩道防線） | **已驗證** | PlayMode 測試 `DashingOutwardAtTheArenaEdge…`；停用邊界牆後該測試變紅（x 由 ≤19.46 變成 19.5，證明牆與 NavMesh 夾回是兩道獨立防線且都有效） |
| Humanoid FBX 連動（紅線 3 的正式載體） | **已驗證** | 專案附 KayKit Adventurers 的 `Knight.fbx`（CC0，授權檔在 `Assets/Art/Characters/`）；Unity 自動對應成 Humanoid Avatar 成功、五個切片全數配對、`OnAttackHit` 事件已掛上。PlayMode：`isHuman == true`、動畫事件約 0.25s 驅動命中、前搖期間右上臂偏轉 > 30°；線上版 HUD 顯示 `RIG: HUMANOID`。Mixamo Y-Bot 仍可替換（見 §5） |
| 真實輸入 → `PlayerInputService` → 射線判定（觸控與滑鼠） | **已驗證（瀏覽器）** | 以 Playwright 對線上 WebGL 版實際操作：模擬手機觸控（Pixel 8、DPR 2）點地移動、點木樁攻擊、窗口內快速滑動消耗充能切後搖、點 HUD 按鈕不滲透成移動；桌機真實滑鼠點地移動。v0.1.1 時滑鼠完全沒反應（WebGL 一律註冊 Touchscreen 使 TouchSimulation 不啟用），v0.1.2 改為直接讀滑鼠後通過 |
| 畫面：血條、傷害飄字、射程環平貼地面且顏色正確、Hitbox 線框、佔位骨架跑步擺動、HUD 排版 | **已驗證（WebGL 截圖）** | 同上操作的截圖；Console 僅剩 Unity 自身的 `INVALID_ENUM` 能力探測警告與預期的佔位骨架提示（v0.1.1 曾有 19 次 `MeshCollider doesn't exist` 與 `no valid NavMesh`，已修） |
| 舊網站的 service worker 不再遮蔽新版 | **已驗證** | 帶著舊 PWA 註冊的瀏覽器開啟後數秒內自動換成新版，註冊數歸 0 |
| 畫面：模式 B 微輪盤與預警箭頭、破牆地裂貼花、NET 延遲按鈕 | **已驗證（WebGL 截圖）** | 以模擬手機觸控對線上 v0.2.0 操作：`NET delay` 按鈕切到 50 ms、模式 B 微輪盤的判定區／原點／外圈／搖桿頭正確繪製且命中幀自動滑出、打碎石牆後地面出現紫色地裂貼花；Console 零錯誤 |
| 震覺疲勞管理（WebGL 後端） | **已驗證（瀏覽器）** | 攔截 `navigator.vibrate` 的實際呼叫：普攻三刀 `[10,10,10]`、之後靜音；微輪盤連續切後搖新增 `[35,35,35]`；打碎石牆那一刀 `35`。Android 原生與 iOS 原生兩個後端**未驗證**（沒有建置環境與實機）；iOS Safari 沒有震動 API，網頁版在 iPhone 上不會震 |
| 斬殺反饋（閃白＋焦痕貼花） | **已驗證** | PlayMode：只有致命的那一刀觸發、且只觸發一次。閃白的實際觀感未以人眼確認 |
| 網路延遲注入 50／80 ms | **已驗證（邏輯）** | 純邏輯測試＋突變檢查：不早送、保序、佇列滿不丟、OFF 同呼叫直通。**真人在延遲下的手感未驗證**——這正是這個功能要讓你測的東西 |
| 每幀零 GC 配置（紅線 4） | **已驗證（Editor）** | PlayMode：以探針夾住全場 MonoBehaviour 的 Update／LateUpdate，讀 Profiler「GC Allocated In Frame」計數器，腳本化戰鬥 240+ 幀（含命中與滑步）配置 **0 bytes**；正向對照證實探針抓得到每幀 256 B 的故意配置。`OnGUI`（調試 HUD）與裝置版未量 |
| 手感 | **已驗收（2026-09-19 使用者）** | 使用者試玩後回報「人眼手感不錯」 |
| 實機 120Hz、Hitstop／震屏／閃白觀感、延遲注入下的手感 | **未驗證** | 只有人能判斷；需要 §4、§6 |

> **綠燈的涵蓋範圍**：70 個 EditMode 測試只覆蓋純邏輯層；8 個 PlayMode 測試覆蓋「場景載得起來、走得動、打得到、滑得出去、出不了界、斬殺反饋、Humanoid 載體、每幀零配置」。
> 材質與渲染結果、真實觸控、手感——**沒有任何自動化測試**，只能靠 §4~§6 的人工實測。請不要把「測試全綠」解讀成整體健康度。

重跑驗證：

- 突變檢查（不需要 Unity，約 2 分鐘）：`python Tools/DotnetCheck/mutation_check.py`，結束碼 0＝全部突變都被抓到
- 不需要 Unity：雙擊 `Tools/DotnetCheck/verify.bat`（需 .NET 8 SDK 與 `../vow-toolchain/refs` 參考組件）
- 需要 Unity（把 `<U>` 換成 `"C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"`，`<P>` 換成本專案路徑）：
  - 重建場景：`<U> -batchmode -quit -projectPath <P> -executeMethod Vow.EditorTools.VOWPhase1SceneBuilder.Build -logFile build.log`
  - EditMode：`<U> -batchmode -projectPath <P> -runTests -testPlatform EditMode -testResults edit.xml -logFile edit.log`
  - PlayMode：`<U> -batchmode -projectPath <P> -runTests -testPlatform PlayMode -testResults play.xml -logFile play.log`

---

## 0.1 網頁試玩版（手機開網址即可玩）

**https://9gf6p4448m-del.github.io/vow3d/** —— 右下角的版本列（`VOW v0.1.0` ＋ 建置時間 ＋ commit）用來判斷看到的是不是最新版；
遊戲內 HUD 第一行也有同一個版本號。看到舊版時先強制重新整理（手機：關掉分頁重開；桌機：Ctrl+Shift+R）。

- 更新流程：改 `Assets/Scripts/Core/VowVersion.cs` 的版本號 → commit → 雙擊 `Tools/deploy-webgl.bat`（建置約 5~15 分鐘，完成後自動推上 `gh-pages` 分支）。
- **網頁版只用於日常試玩與看畫面，不能當正式手感驗收**：瀏覽器的輸入延遲、幀率上限、觸控取樣都與原生 App 不同（WebGL 上幀率交給瀏覽器跟螢幕刷新率，
  不強制 120）；Unity 2022.3 對手機瀏覽器的 WebGL 屬有限支援。規格要求的「10 人在 6.7 吋手機盲測、鎖 120 FPS」以原生 Android／iOS 建置為準。

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
**驗收點**：EditMode 70 個、PlayMode 8 個全綠（EditMode 與 dotnet 下跑的是同一份原始碼：`Assets/Tests/EditMode/`）。

## 4. 手感驗收（按 Play）

左上角 HUD：`FPS`／`PANEL HZ`（螢幕實際刷新率）／`STATE`／`PIPS`（3 格充能）／`WINDOW`（目押窗口倒數條）／`MODE`／`RIG`（`HUMANOID` 或橘字 `PLACEHOLDER`），以及三顆按鈕 **Switch to A/B**、**Hitbox ON/OFF**、**NET delay: OFF／50 ms／80 ms**（模擬網路往返延遲：在最壞情況——沒有客戶端預測——下測 220ms 窗口還按不按得出來）。
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
   → **已裁定（2026-09-18，使用者）：維持此解讀，7.5mm 不設硬上界。** 拇指快速一彈是彈道式動作，實際位移很容易超過 7.5mm，
   設硬上界會把合法的微彈判成無效（玩家感受＝吃指令，正是 GDD Premortem 要消滅的體驗）；大幅拖曳已由 0.25 秒時限與 UI 區域分流擋住。
   盲測若有人回報「只想拖一下卻滑出去了」，再加可調上界參數。
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
- `ARCHITECTURE.md` §貳 的網路延遲注入與震覺疲勞管理已於 v0.2.0 補上；其中 **Android 原生與 iOS 原生的震動後端尚未在實機驗證**

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

## 11. Phase 2 批 1：地脈符印與石牆（v0.3.0～v0.3.4）

> 使用者 2026-09-19 裁定：`ARCHITECTURE.md` §貳「10 人實機盲測通過才解鎖 Phase 2」**延後**，盲測於 Phase 2 期間平行補做（原文不改）。
> Phase 2 分四批：批 1＝輸入路由＋符印按鈕＋`IRuneWall`（本節）；批 2＝0.5m 格點＋向量場繞牆；批 3＝破敵牆得護盾＋友軍穿透；批 4＝元素 Combo。
> 計畫與凍結的驗收條件：`docs/PHASE2_BATCH1_PLAN.md`。獨立審查三輪的摘要與逐條處置：repo 外 `vow-toolchain/REVIEW-p2b1-r1~r3.md`。

### 11.1 怎麼玩
- **輕點右下角符印鈕**＝極速石牆：英雄正前方 4m 立一面橫向石牆。任何狀態（前搖、收招、滑步中）都放得出來。
- **按住往外拖**＝自由封路：地面出現半透明虛影，拇指方向＝石牆相對英雄的方位，拉得越遠牆越遠（1.2m～8m；v0.3.4 起拖出後的前 1/3 行程是「貼身帶」，一律 1.2m）；鬆手成牆。
- **取消**：滑回按下的位置放手。手指回到那個範圍時，地上的虛影與符印鈕都會變紅＝「現在放手就是取消」。取消不吃冷卻。
  （v0.3.0／v0.3.1 另有一根紅色取消柱；使用者 2026-09-19 試玩後覺得怪，裁定拿掉，只留滑回原點。GDD §參-1「或上方取消區」的後半因此不實作；
  `docs/PHASE2_BATCH1_PLAN.md` §5 的 V2.d 同時作廢。）
- 石牆活 5 秒、全隊最多 2 面（第 3 面成形時最早的坍塌）、冷卻 8 秒（按鈕上有遮罩與秒數）。石牆會實體擋住英雄。

### 11.2 數值（`Assets/Scripts/Core/Logic/RuneTuning.cs`，經 `HeroTuningAsset.Rune` 可在 Inspector 調）
GDD 有給的：壽命 5s、極速距離 4m、上限 2 面、穿透 10 發（前 5 發不衰減）。
**GDD 沒給、使用者裁定先用暫定值【試玩必調】**：冷卻 8s、石牆 4m×0.6m×2m、血量 300、拖曳距離 1.2～8m（v0.3.4 起，原 2～8m）、貼身帶＝拉伸量前 1/3、拇指拉滿行程 14mm。
按鈕版面（`Assets/Scripts/Input/RuneButtonLayout.cs`）：直徑 16mm、離邊 15mm（v0.3.3 起；原為 4mm；下限 16px）。v0.3.2 起沒有取消區。

### 11.3 規格留白處的解讀（請確認或推翻）
1. **補了一個規格沒有的事件**：`IPlayerInputService` 只有符印的拖曳更新／極速施放／取消，沒有「鬆手成牆」。另立 `IRuneCastInput.OnRuneCastReleased`，ARCHITECTURE 逐字抄錄的契約不動。
2. **瞄準不進狀態機**：`PlayerState.CastingRune` 本批沒用到。現行轉移表下該狀態會吃掉移動／攻擊指令且只能從 Idle／Moving 進入，那樣極速石牆在前搖或收招中就放不出來（＝吃指令）。石牆在事件到達當下立即成形，英雄照常走 A。
3. **拖曳原點＝手指按下的位置**（浮動原點，與微輪盤一致）；「滑回按鈕中心」＝回到距按下位置 3.5mm 內，這是 v0.3.2 起唯一的取消方式，此時虛影與按鈕變紅（`PlayerInputService.IsRuneCancelArmed`）。往任何方向拉多遠都不會再被判成取消。
4. **沒拖出 3.5mm 就放手＝極速石牆，不論按多久。** 另外，0.2 秒內、拇指最多滾出 7.5mm、放手時已滾回 3.5mm 內的，也算輕點（短促點擊時拇指會滾動，不赦免的話會被判成取消＝按了沒反應）。放手時仍在 3.5mm 外的一律照拖曳方向成牆——那是快速方向施放，方向不得丟掉。
5. **拇指的兩個自由度**：角度＝方位，拉伸量＝距離；牆面永遠垂直於「英雄→落點」連線。極速石牆的「正前方」＝英雄當下朝向。
6. **延遲注入**：極速施放與鬆手成牆走延遲佇列；虛影與取消是本機回饋、不延遲。開 50／80ms 時虛影會先消失、實牆稍後才以當時的英雄位置出現。
7. **友軍穿透**（批 3 才有子彈，本批只有純邏輯與測試）：第 1～5 發 ×1.0、第 6～10 發固定 ×0.85，每發扣石牆 10% 最大生命與 0.5s 壽命，第 10 發後崩解。

### 11.4 已知限制（都已記錄，不是漏掉）
- **螢幕右方／下方立不了遠牆**：按鈕在右下角、拖曳原點浮動，往右、往下拖不了多遠手指就出螢幕了。
- **所有符印石牆都點不到、英雄打不了石牆**：石牆放在 Ignore Raycast 圖層，點擊射線會穿過它打到後面的地板（否則想走過去會變成原地砍自己的牆）。GDD §參-2 的「砸碎敵方／中立石牆得護盾」需要牆打得到——**批 3 必須換掉這個圖層做法**。
- **石牆的兩本血量帳**：近戰傷害記在 `CombatTargetBehaviour`，穿透損耗記在 `RuneWallLogic`，只在死亡時同步。本批場上沒有子彈所以摸不到；**批 3 接子彈前必修**。
- 落點不做場地邊界裁切：貼著場邊往外拉滿，牆會立在場外、冷卻照扣（排入批 2）。
- 繞牆尋路仍屬批 2：點牆後的地板，英雄會頂著牆走。另記一條批 2 風險：`HeroLocomotion.ApplyDisplacement` 對「起點已與 Collider 重疊」的命中會放行，批 2 若讓牆能貼身生成就會穿牆。
- 螢幕→世界方向的換算目前有三份（`HeroController`、`CadenceAimPreview`、`RuneCaster`），批 2 收斂。
- **WebGL 上的「毫米」不一定是實體毫米**：`Screen.dpi` 回 0 時退回 160dpi，而網頁範本把畫布像素放大到 CSS 像素的 2 倍——按鈕與手勢門檻的實體尺寸可能只有名目的一半左右。§11.5 有實測值。

### 11.5 驗證紀錄
自動化（2026-09-19，commit `1ecb258`／`719c181`；產物在 repo 外 `vow-toolchain/`）：
- `bash Tools/DotnetCheck/verify.sh`：純邏輯 104 個測試 0 失敗、compile-only 0 error、8 條紅線全過。
- `python Tools/DotnetCheck/mutation_check.py`：38／38（既有 18＋符印 20：U1～U12、W1～W8）。
- Unity batchmode：EditMode 104／104（`unity-p2b1-final-1-EditMode.xml`）、PlayMode 17／17（`unity-p2b1-final-2-PlayMode.xml`；既有 8＋符印 9）。
- 鑑別力實測（故意改壞→必須變紅→還原）：成牆路徑塞 `new byte[256]` → 零配置測試紅在「配置了 288 bytes」；虛影高度改回舊算法 → V4e 紅在「實際 y=0、預期 y=1」。
- 獨立審查三輪（fresh-context opus，任務是反駁「已完成」）：r1 CRITICAL 1／HIGH 5 → r2 九條中 7 真修＋新 HIGH 3 → r3 九條全部真修、CRITICAL 0／HIGH 0、部署前必修清單為空。

線上版瀏覽器實測（v0.3.0／v0.3.1，Playwright 手機模擬 844×390、dpr 3、CDP 觸控；截圖 `vow-toolchain/browser-screenshots/p2b1-01～10`，console 無 error）：
- 輕點符印鈕 → 英雄正前方立起石牆、按鈕出現冷卻遮罩與秒數。
- 往左拉滿 → 左方最遠處出現半透明虛影、右上出現紅色取消區；鬆手 → 牆立在虛影位置。
- 往上滑進取消區放手 → 沒有牆、按鈕沒有進冷卻；緊接著輕點仍可成牆。
- 立牆後點牆後方的地板 → 3 秒後英雄仍在牆的這一側（貼著牆面滑到牆端）；牆 5 秒到期後英雄走到目的地。
- v0.3.0 發現首頁版本字樣疊在符印鈕上（不吃觸控，但蓋住冷卻秒數）→ v0.3.1 移到底部中央。
- **實測 `Screen.dpi = 192`、畫布 1688×781**（CSS 844×390、dpr 3、網頁範本把畫布倍率上限設為 2）。192＝96×2，是瀏覽器的名目值而不是面板的實體 dpi：
  手機的 CSS 像素實體上約 160 dpi，乘上畫布倍率 2 約 320 dpi，所以**名目 1mm 在手機上約只有 0.6mm**——符印鈕 16mm 約 9.6mm、拖曳門檻 3.5mm 約 2.1mm、拉滿 14mm 約 8.4mm。
  同一把尺在 Phase 1 的微彈上已經過使用者真機驗收，所以本批不動；要修的話是整把尺一起校正（WebGL 上改用 160×畫布倍率 當 dpi），屬手感數值，先問使用者。

v0.3.2（2026-09-19，commit `e0b3d4d`；使用者試玩回饋「取消的紅柱有點怪」→ 裁定拿掉取消區、只留滑回原點）：
- `verify.sh` 純邏輯 102 個 0 失敗、ALL PASS；`mutation_check.py` 35／35（拿掉只屬於取消區的 U3／U9／U11／U12，新增 U13「滑回原點不亮取消旗標」）。
- Unity batchmode：EditMode 102／102（`unity-cancelfix-2-EditMode.xml`）、PlayMode 18／18（`unity-cancelfix-5-PlayMode.xml`；新增「放手會取消時虛影變色」，顏色由 Renderer 的 PropertyBlock 讀回）。鑑別力：把變色那行拿掉 → 該測試紅在「旗標為真時應顯示取消色」。
- 線上版瀏覽器實測（截圖 `browser-screenshots/v032-01～04`，console 無 error）：拖曳中畫面上沒有紅柱；拖出再滑回原點 → 虛影與符印鈕變紅，放手後無牆、未進冷卻；往正上方一路拉到螢幕頂放手 → 牆立在正前方最遠處、按鈕進冷卻。

v0.3.3（2026-09-19；使用者試玩回饋「按鈕太靠邊界，想把牆放到右邊容易觸發取消」）：
- 成因：拖曳原點＝手指按下的位置，按鈕離邊只有 4mm（按鈕中心離邊 12mm）＜拉滿行程 14mm；往右／往下拖的手指撞到邊框後觸控點不再移動，落回 3.5mm 取消半徑，放手＝取消。
- 修法：`RuneButtonLayout.EdgeMarginMillimeters` 4 → 15（＝拉滿行程 14mm＋1mm 餘裕）。新增不變量測試 `Layout_LeavesAFullSaturationStrokeBetweenTheButtonAndBothScreenEdges`；鑑別力：在 4mm 的舊幾何上紅在行為斷言（右側只剩 72.4px，需要 253.5px）。
- 兩個手算錨點跟著需求改值（460dpi 四個邊座標；下限分支的治具由 96dpi 改成 1px/mm，否則 15mm 邊距碰不到 16px 下限、那條分支不再被走到）。
- `verify.sh` 純邏輯 103 個 0 失敗；`mutation_check.py` 35／35；Unity batchmode EditMode 103／103、PlayMode 18／18（`unity-033-*.xml`）。

v0.3.4（2026-09-19；使用者試玩回饋「想把牆放得很靠近自己，容易觸發取消」→ 兩題裁定：加貼身帶、最近距離 1.2m）：
- 成因：拉伸量 0（最近距離）只對應「手指剛好停在 3.5mm 取消圈邊緣」那一點，手一抖就滑回圈內變取消。
- 修法：`RuneCastLogic.TryDragPlacement` 把拉伸量前 `RuneTuning.DragNearBand01`（1/3＝3.5～7mm）一律對應最近距離，之後線性到 8m；`DragMinDistance` 2 → 1.2。手勢判定（`RuneGestureTracker`）一行未動，取消規則不變。
- 測試：新增 `DragCast_NearBand_HoldsTheMinimumDistanceForTheFirstThirdOfTheStroke`；既有 `DragCast_MapsStretchToTwoThroughEightMetres` 的期望值跟著裁定改（近 2→1.2、中點 5→2.9；名稱沿用以免動到突變 W8 的比對）。舊邏輯上兩者都紅在行為斷言；新增突變 W9（貼身帶失效）被抓到。
- `verify.sh` 純邏輯 104 個 0 失敗；`mutation_check.py` 36／36；Unity batchmode EditMode 104／104、PlayMode 18／18（`unity-034-*.xml`）。

只有真人能判斷、尚未驗證的：符印鈕在真機上的大小與位置順不順手；0.2 秒／3.5mm／7.5mm 三個輕點判定值（症狀：「點了符印鈕卻沒反應」或「想在正前方立牆卻立歪在旁邊 2m」）；8 秒冷卻與 1.2～8m 距離、貼身帶寬度的手感；滑回原點取消順不順手、虛影變紅夠不夠明顯；開 NET delay 時虛影與實牆的時間差。

## 12. Phase 2 批 2：0.5m 格點阻擋網格＋向量場繞牆（v0.4.0）

> 計畫、凍結驗收條件與兩輪審查後的修訂：`docs/PHASE2_BATCH2_PLAN.md`（§5 原條件、§6 R1～R9 修訂）。審查報告：`vow-toolchain/REVIEW-p2b2-r1.md`、`REVIEW-p2b2-r2.md`。

**玩法上多了什麼**
- 英雄會**繞牆**：放一面牆擋在自己與目的地之間，點牆後方的地板，英雄會繞過牆端走過去；追擊被牆隔開的目標也一樣。
- **點牆腳／走不到的地方**：英雄走到「最近可達點」停下（不發呆、不頂牆）。牆的這一面與另一面都有可停的位置、而且離你點的地方差不多近（相差不到約 0.7m）時，優先停在英雄現在所在的這一面——例：英雄在牆的北邊、你點牆腳，就算牆南邊的空位近了 0.2m，他還是停在北邊；只有你明顯點在牆的另一面（相差超過約 0.7m）才會繞過去。
- **牆壓到英雄**（例：打爆測試牆後站在原地等它 6 秒後重生）：牆維持在原位不變，英雄被瞬間移到旁邊最近的空位。
- 沒有牆擋路時，移動與 v0.3.4 完全相同（到目的地有視線就走舊路徑，不碰格點）。
- HUD 多一顆 `GRID: OFF／ON`：疊出目前被擋住的格子，回報「繞得很怪」時請開著它截圖。

**規格解讀與取捨**（全部可推翻）
1. 格點 80×80、格寬 0.5m，涵蓋整個 40×40m 場地。進格點的只有符印石牆、`TestWall_A/B`，以及「英雄身體到不了」的最外一圈（316 格，由場景實際的邊界 Collider 算出）。木樁不進格點。
2. 牆的佔格＝牆體各向外擴英雄半徑 0.35m 後，與它相交的每一格；所以兩面牆之間的縫要比實際看起來寬一點才走得過（保守）。
3. 整合場（Dijkstra，直走 10／斜走 14、不切角）只在「到目的地沒有視線」時才建；路徑用視線前視拉直，避免 45° 鋸齒。
4. 微彈滑步不走格點，仍是撞牆裁切＋貼牆滑一次（Phase 1 手感核心零改動）。
5. 推出是瞬移、不補動畫；出招或滑步中也會被推【試玩必調】。
6. 牆消失或新牆出現時，只有還持有移動／追擊指令的英雄會重新找路；已經停下的不會自己再走。

**已知限制（排入 v0.4.1，r2 覆審的 MEDIUM／LOW）**
- 追擊一個「走不到」的目標（例：牆貼著木樁放，木樁落在牆的外擴區內）時，每 0.1 秒會重算一次替代點，桌機實測約 2.2ms／次；手機上可能感覺到微卡。追擊指令在英雄停到替代點之後也不會自己結束。
- 邊界圈找不到場景的邊界 Collider 時會無聲失效（目前靠測試擋）——失效後的症狀是 r1 的 H1 回來：牆貼著場地邊緣放時，英雄被導向最外圈那條身體到不了的格子，頂著看不見的邊界卡住直到牆消失（約 5 秒）；不會掉出場地；Unity 端的零配置量測窗口還沒涵蓋「點牆腳」這條路（dotnet 端量過是 0 bytes）。
- `HasLineOfSight` 對恰好壓在格界上的線段，比規格寫的 supercover 寬鬆一格；r2 以 16 萬條線段實測，放行的線段離真實牆面最小 0.351m ≥ 英雄半徑 0.35m，不會擦牆。

**驗證紀錄（`phase2-batch2` @ `3ab4801`，主對話親自重跑）**
- `verify.sh`：純邏輯 140 通過／1 略過、ALL PASS；`mutation_check.py`：55／55。
- Unity batchmode：EditMode 141（138 通過／3 略過——三個 `GC.GetAllocatedBytesForCurrentThread` 量測在 Unity Mono 上恆回 0，只在 dotnet 執行；另有一條 Unity 專用測試實證這個前提）、PlayMode 31／31（`vow-toolchain/unity-p2b2-main2-*.xml`）。
- r1 對抗審查 CRITICAL 1／HIGH 4 → 兩輪修復 → r2 三態覆審：18 條中 17 條真的修好、1 條（M3 效能）表面修好；新 finding 無 CRITICAL／HIGH。每條修復都有「修復前紅在行為斷言」的紀錄（`vow-toolchain/p2b2-fix1-red-*`、`p2b2-fix2-red-*`）。

- 送達與線上實機（V7）：`origin/gh-pages 7e4998c`（2026-09-19 14:23 +0800，deploy: VOW v0.4.0 from 80c84d6）、線上首頁版本列 v0.4.0。Playwright 手機模擬（844×390、`hasTouch`、CDP 觸控）：輕點符印鈕立牆 → 點牆後方木樁旁的地板 → 0.9s 截圖英雄正繞過牆右端（STATE Moving）、2.3s 截圖英雄已站在牆後（STATE Idle，牆仍存活）；另一輪先開 GRID 再放牆，截圖可見兩面測試牆與符印牆底下疊出 Blocked 格；兩輪 console 皆無 error。截圖 `vow-toolchain/browser-screenshots/v040-00～06`。

只有真人能判斷、尚未驗證的：繞牆路線看起來自不自然；點牆腳時停的位置順不順；被牆推開的瞬移會不會突兀；手機上放牆／追擊時有沒有卡頓。

### 12.x v0.4.1（2026-09-19）：r2 覆審遺留項
- 改了什麼（玩家看得到的只有「更不卡」）：追一個走不到的目標時，目標沒換格、牆沒變就沿用上一次的替代點（原本每 0.1s 重跑全場 Dijkstra＋三趟 6400 格掃描）；走得到的目標不快取、行為與 v0.4.0 逐值相同；替代點選點的三趟全場掃描縮成「一趟全場＋兩趟候選帶方塊」（點牆腳 19200→約 6600 格）；場地邊界圈建不出來時 `Debug.LogError`（原本無聲）；拔掉導航器時追擊指令也還原目的地。凍結驗收條件與 r3 覆審結果：`docs/PHASE2_BATCH2_PLAN.md` §6 R11／R12。
- 測試現況：純邏輯 143＋1 略過、突變 56/56、Unity EditMode 140＋4 略過、PlayMode 40/40（main `7c38042` 上重跑）。
- r3 覆審（`vow-toolchain/REVIEW-p2b2-r3.md`）：七條全部真的修好、無 CRITICAL／HIGH。**已知限制 R3-1**：替代點快取只看「目標格＋格點版本」，走不到的目標在同一格內移動時替代點可能不是當下最佳解；目前場上沒有會動的追擊目標所以踩不到，批 3 加入會動的敵人之前必修。
- 送達與線上實機：`origin/gh-pages a8085f0`（2026-09-19 18:47 +0800，deploy: VOW v0.4.1 from 7c38042）、線上首頁版本列 `v0.4.1 · build 2026-09-19 10:47 UTC · 7c38042`。Playwright 手機模擬（844×390、`hasTouch`、CDP 觸控）三輪 console 皆無 error：①輕點符印鈕立牆 ②點牆面（射線穿過石牆落在牆後地板）→ 1.5s 後英雄已繞到牆後木樁旁、STATE Idle ③開 GRID、點牆腳前緣的紅格（Blocked）→ 英雄停在牆的這一側、緊貼紅格外緣、STATE Idle。截圖 `vow-toolchain/browser-screenshots/v041-00～06`。

## 13. Phase 2 批 3：陣營校驗破牆得護盾＋友軍彈道穿透己方石牆（v0.5.0，2026-09-20）

計畫、使用者裁定、凍結驗收條件與三輪審查紀錄：`docs/PHASE2_BATCH3_PLAN.md`（§0 裁定、§5 V1～V8、§7 r1 修訂、§8 r2 結果）。審查報告：`vow-toolchain/REVIEW-p2b3-r1.md`（CRITICAL 1／HIGH 3，全修）、`REVIEW-p2b3-r2.md`（r1 四條全部真的修好、新 finding 無 CRITICAL／HIGH）。

### 怎麼玩（除錯 HUD 左下多兩顆鈕、多一列 SHIELD）
- **ENEMY WALL**：在英雄正前方 4m 生一面**紅色**敵方牆（壽命 5s、同時最多 2 面、第 3 面擠掉最舊；不佔己方 2 面上限）。點它＝鎖定去砸（血 300、每刀 60、5 刀）；**砸碎的那一刀**給英雄 150 點岩石護盾、持續 2.5s（頭上護盾條＋HUD `SHIELD` 數值，到期歸零；重複取得＝刷新不疊加）。兩面灰色測試牆視為中立牆，砸碎也給盾。
- **點己方（藍色）牆**＝手指穿過去、等於點到牆後的地板，英雄繞過去；己方牆不能用點的去砸。
- **不給盾的情況**：牆壽命到期、被友軍子彈穿到崩解、被第 3 面牆擠掉、子彈（遠程）打碎中立／敵方牆。
- **TURRET: OFF／ON**（預設 OFF）：左上那座藍色友軍砲台每 0.25s 朝木樁射一發（傷害 20、20 m/s）。把己方牆放在彈道上（木樁左邊那條橫向走廊；用拖曳施放往左上拉）：子彈**穿過**己方牆，前 5 發 ×1.0、第 6 發起 ×0.85，每發扣牆 10% 血＋0.5s 壽命，**第 7 發把牆打塌**；敵方／中立牆會**擋下**子彈並吃子彈傷害。符印牆與敵方牆現在頭上有血條。

### 暫定數值（全部【試玩必調】，單一來源 `Core/Logic/ProjectileTuning.cs`）
砲台射速 0.25s／發、子彈 20 傷害／20 m/s／射程 30m、護盾 150／2.5s、敵方牆同時存活上限 2（池 3 面）、敵方牆壽命 5s／正前方 4m。

### 規格解讀與已知限制
- `MaxPenetrations=10` 在任何射速下都不可達：每次穿透扣 0.5s 壽命，10 次＝整條 5s 壽命，壽命成本永遠先歸零（推導見計畫 §4-4）。規格數值不動，批 3 沒有任何以「第 10 發」為內容的驗收條文。
- 護盾吸收傷害只有純邏輯測試背書（`RockShieldLogic.Absorb`）：英雄還沒有血量、場上沒有東西會打英雄（使用者裁定 2）。
- 灰色測試牆每 6s 重生＝可重複取得護盾；灰盒沒有對手，正式地圖的中立牆不會重生。
- 牆在「子彈已進入牆體之後」才立起來時，那一發既不被擋也不記穿透（Unity 射線不回報起點在其內部的 Collider）；只影響除錯砲台觀感。
- 零配置量測的涵蓋缺口：動畫事件階段（近戰命中結算、真實路徑的護盾授予處理常式）與 `DebugHud.OnGUI`（IMGUI）結構上不在 Update／LateUpdate 夾區內；護盾授予本身已由夾區內的 Driver 行使並驗過注入配置會紅。
- 場邊按 ENEMY WALL 會把牆生到場地外（不進格點）；己方／敵方牆血條同色；R3-1 的另一半（替代點快取 key 不含英雄位置）仍在，批 3 沒有新增踩得到它的路徑（r1 覆審第 8 項）。

### 驗證紀錄
- main `1bff064` 出貨前主對話重跑：純邏輯 155＋1 略過（`verify.sh` ALL PASS）、Unity EditMode 152＋4 略過、PlayMode 66／66；突變 74／74（r2 覆審員實跑）。
- 送達與線上實機：`origin/gh-pages 766d7e5`（2026-09-20 01:53 +0800，deploy: VOW v0.5.0 from 1bff064）、線上首頁版本列 `v0.5.0 · build 2026-09-19 17:53 UTC · 1bff064`。Playwright 手機模擬（844×390、`hasTouch`、CDP 觸控）三輪 console 皆無 error：①按 ENEMY WALL 生紅牆→點它→英雄原地開砸→牆碎、HUD `SHIELD 150`、頭上護盾條；約 3s 後 `SHIELD 0` ②己方極速石牆→點牆面→英雄繞到牆後、STATE Idle、己方牆血條滿、`SHIELD 0` ③不開砲台時木樁血條不動；拖曳施放把己方牆放到彈道上→`TURRET: ON`→截圖可見子彈在牆兩側、牆血條下降、木樁血條下降；約 2s 後牆已崩解、`SHIELD 0`。截圖 `vow-toolchain/browser-screenshots/v050-00～17`。

## 14. Phase 2 批 4：三大元素反應——泥濘流沙／蒸氣迷霧／擴散火浪（v0.6.0，2026-09-20）

計畫、使用者裁定、凍結驗收條件與審查紀錄：`docs/PHASE2_BATCH4_PLAN.md`（§0 裁定、§4 假設、§5 V0～V8、§8 Checkpoint A、§9 r1 修訂與使用者裁定）。審查報告：`vow-toolchain/REVIEW-p2b4-r1.md`（CRITICAL 0／HIGH 3／MEDIUM 7／LOW 4）。

### 怎麼玩（除錯 HUD 左下再多兩列、四顆鈕）

第一列三顆技能鈕、第二列一顆陣營切換鈕。三顆技能鈕**各自獨立 5 秒冷卻**，冷卻中鈕面變灰、字變成 `WATER 5`…`WATER 1`（剩餘秒數），冷卻中按下＝什麼事都不會發生。

- **`ELEM: BLUE` / `ELEM: RED`**：決定**下一發**技能的陣營。無冷卻；已經成形的區域不會跟著變。
- **`WATER`**：在英雄**正前方 3m** 生一圈藍色水域（半徑 3m、壽命 6s）。水自己沒有任何效果，它是另外兩個反應的燃料。
- **`FIRE`**：在英雄正前方 3m 砸一發火。落在空地＝直接傷害 40 ＋ 留一圈橙色燃燒區（半徑 3m、壽命 4s、每秒 20）；落在水域／流沙上則觸發反應（見下）。
- **`WIND`**：以**英雄自己**為頂點、朝面向放一個 60°／6m 的扇形預警（不是前方 3m）。

### 三個反應各怎麼做出來

| 反應 | 操作 | 看得到什麼 |
|---|---|---|
| **泥濘流沙**（岩＋水） | `WATER` → **原地**再按 `ENEMY WALL`（或用符印牆把牆立在水域內） | 水域變成黃褐色流沙圓盤（半徑 4m、壽命 3.5s）＋一次 45ms 頓挫與震屏 |
| **蒸氣迷霧**（火＋水） | `WATER` → 原地再按 `FIRE` | 水域變成白色霧圈（半徑 4m、壽命 3s）＋一次頓挫震屏；**不留燃燒區** |
| **擴散火浪**（風＋火） | `FIRE` 打空地生燃燒區 → **原地**按 `WIND`（燃燒區圓心在正前方 3m，就在扇形內；也可以走位後從別的角度吹） | 扇形預警亮起、燃燒區消失、扇形內的目標吃 60 傷害＋一次頓挫震屏 |

**順序不能反**：先 `ENEMY WALL` 再 `WATER` 不會有任何反應（岩要「砸進」已經存在的水域）。

**施放距離 3m 是故意的**（r1 HIGH-1 使用者裁定，原為 4m）：敵方牆與極速石牆的落點是前方 4m、反應區半徑也是 4m，三個 4 疊在一起時原地連按會讓英雄**恰好站在流沙邊界上**，困不困得住由浮點捨入決定。改成 3m 之後英雄離邊界 1m，「原地連按就會被困」穩定成立。

### 被困住是什麼感覺（流沙）

站在**敵對**流沙裡：進入起算 **1.2 秒完全不能位移**（點地不動、微彈滑步也不會發生，而且不扣充能），1.2 秒後只要還在圈內就是**移速 ×0.65**，走出去或流沙消失即恢復。**可以照常普攻**。自家陣營的流沙對自己完全沒有效果。

### 陣營方向（GDD 的「友軍／敵方」是從被困者的角度講的）

- **紅流沙困住你（藍英雄）** → 按 `ELEM: BLUE` 再 `FIRE` 打在流沙上 ＝ **救援**：烘乾、當幀解除縛足、流沙消失、**不造成任何傷害**。
- **藍流沙罩住木樁（紅隊）** → `ELEM: BLUE` ＋ `FIRE` ＝ **爆沸**：對流沙圈內與藍隊敵對的目標造成 **80** 傷害、流沙消失（自家藍牆不吃這一下）。
- 同一個盤面按 `ELEM: RED` ＋ `FIRE` ＝ **救援木樁**：木樁一滴血都不掉，流沙照樣消失。
- 水域與石牆陣營不同時，**流沙算石牆（完成 Combo 那一方）的**。蒸氣不分陣營。

### 蒸氣迷霧遮蔽（要看效果請先把 `TURRET` 關掉）

霧裡的目標**點不到也鎖不到**；但① 站進**同一團**霧裡就打得到（與你共享任何一團霧都算）② 目標**受到任何傷害**後顯影 1.5 秒，期間可以鎖定，每挨一下就刷新。

### 暫定數值（全部【試玩必調】，單一來源 `Core/Logic/ElementTuning.cs`）

水域 3m／6s；火直傷 40、燃燒區 3m／4s／每秒 20；反應區 4m；流沙 3.5s、蒸氣 3.0s；縛足 1.2s、減速 ×0.65；受擊顯影 1.5s；霧內引導 ×0.5；火浪 60°／6m／×1.5（＝60）；爆沸 80；技能冷卻 5s；**施放距離 3m**；Combo 頓挫 45ms、震屏 0.5。同時存活上限 6 個區域（視覺池 8 個）。

### 規格解讀與假設（計畫 §4，可推翻）

- 反應優先序定死：**流沙 ＞ 水域 ＞ 空地**，一發只觸發一個；同種區域蓋到多個時取圓心最近者、平手取 id 小者。風只看燃燒區、岩只看水域。
- **反應區的圓心＝被消耗那個水域的圓心**（不是牆或火的落點）。火浪的「掃到」＝**燃燒區圓心**落在扇形內。
- 火落在既有燃燒區內＝把它刷新回 4s，不疊第二圈（否則同一塊地變成每秒 40）；火落在蒸氣內＝視同空地，蒸氣不消耗。
- 每個流沙對同一單位**只縛足一次**（走出去再走回來只有減速）；換一個流沙會重新縛足。
- 元素 AOE／DoT 一律跳過「擁有者＝施放陣營」的石牆；傷害型別是 `DamageType.Elemental`。
- 風沒掃到燃燒區時**不造成任何傷害、不生任何區域**，只有扇形預警。【待試玩推翻】
- 區域視覺是灰盒半透明**圓盤**（高 0.04m），沒有 Collider、不進格點、不擋點擊。

### 已知行為與限制

- **砲台開著時，霧裡的木樁會一直顯影、因此一直點得到**（r1 HIGH-3，使用者裁定**視為設計**）：`GDD.md:126` 的字面就是「受到傷害立即顯形」，而砲台 0.25s 一發 ≪ 顯影 1.5s。要看遮蔽效果請先 `TURRET: OFF`；開著時它是「受擊顯影」的示範。
- 蒸氣遮蔽只作用在**英雄的鎖定**上；砲台子彈不查蒸氣，霧裡照樣被打到（計畫 §6 R2）。
- 「霧內引導晶塔速度減半」只有純邏輯係數與測試，本批沒有生產呼叫點（裁定 4、§6 R3）。
- 木樁不會移動，所以縛足／減速實機上只有英雄一個受試者；「同一個流沙同時困住兩人」只有純邏輯測試（§6 R4）。
- 兩個**重疊**的敵對流沙之間來回走會鏈式重複縛足（上限＝流沙 3.5s 壽命，救援可解）；§4-4 的「只縛一次」是對**單一**流沙而言（§9 M2）。
- 縛足開始時**已經在進行中**的滑步會被吃掉：充能已扣、人沒動。窗口約一次滑步的時長（§9 M3）。
- HUD 面板已經在臨界：整塊面板需要 `Screen.height / scale ≥ 503.2`，WebGL 橫式約需 CSS 視窗高 ≥302px 才放得下最後一列（§9 M6）。真正的閘是下面那一節的線上實測。**懸崖在 DPR < 5/3**（`dpi < 160` 時縮放夾回 1）：DPR 1.5＋CSS 高 320 差 4.8px、DPR 1＋CSS 高 390 差 97px，`ELEM` 那一列會整個在畫面外；DPR 2／3 的手機橫式（CSS 高 320／360／390）都放得下（r2 M3）。
- 批 4 的 PlayMode 測試除 R1 外都用腳本化輸入（`ScriptedInput`）下指令，真實點擊路徑由 R1 與線上 Playwright 實測補（§9 M1）；V4-p「既有 HUD 座標不動」的期望值是手抄的 v0.5.0 算式，r1 審查員另對 `566a81f` 逐行核對過相同（§9 L4）。
- 縛足中仍然可以轉向（§9 L2）；`IsInsideSector` 的三個退化分支沒有測試（§9 L3）。
- 零配置量測的涵蓋缺口：`DebugHud.OnGUI`（四顆鈕的繪製）與動畫事件階段結構上不在 Update／LateUpdate 夾區內，量不到；四顆鈕的標籤重算、區域 Tick、AOE／DoT、視覺池借還、縛足與遮蔽判定都在夾區內量過，並實測注入一次配置會讓測試變紅。

### 驗證紀錄

- 步驟 B 與 r1 修訂出貨前重跑：`verify.sh` ALL PASS（純邏輯 193 通過＋1 略過、紅線 8 條期望數不變）、Unity EditMode 194 條 0 紅（4 略過）、PlayMode 89／89（v0.5.0 既有 66 條全在全綠）、突變 110／110。
- 鑑別力證據：零配置探針注入一次 `new float[1]` → PlayMode 由 85／85 變成 84／85（`Update 夾區在 1519 幀內配置了 54684 bytes`），用備份副本還原後回到全綠；`IsConcealedFrom` 的多團霧修復、施放距離 3m 的修復都各附了修復前的紅燈輸出。
- **送達與線上實機（2026-09-20）**：`origin/gh-pages ded1c4d`（2026-09-20 18:01:49 +0800，deploy: VOW v0.6.0 from 505ce5f）、線上首頁版本列 `v0.6.0 · build 2026-09-20 10:01 UTC · 505ce5f`。Playwright 手機模擬（844×390、`deviceScaleFactor: 2`、`hasTouch`、CDP 觸控；HUD 顯示 `192 dpi 1688x781`），五次載入 console 皆無 error：
  - **四顆鈕的 CSS 座標（橫式 844×390 與直式 390×844 相同，面板在左上、全部看得見、底緣約 CSS y=302）**：`WATER` x 9.5–53／`FIRE` x 58–101.5／`WIND` x 106.5–150（y 245.5–266.5）；`ELEM` x 9.5–150（y 271–292）。點擊中心：WATER (31,256)、FIRE (80,256)、WIND (128,256)、ELEM (80,282)；既有 ENEMY WALL (43,230)、TURRET (116,230) 不變。不與符印鈕（右下）、首頁版本列（底部中央）重疊。
  - ① **流沙**：`ELEM`→RED、`WATER`（標籤變 `WATER 5`）、**原地** `ENEMY WALL` → 黃褐流沙圓盤成形、紅牆立在圈內、英雄在圈內；0.6s 後點地 → 英雄只轉向、位置不變（**HUD 的 STATE 會顯示 `Moving`——狀態機接受了移動指令，位移被縛足擋掉**；計畫 V8 原寫「STATE 不變成 Moving」與實際不符，以此為準）；`ELEM`→BLUE、`FIRE` → 再點地 → 英雄正常走遠（截圖 `v060-00`～`06`）。
  - ② **蒸氣**（`TURRET: OFF`）：出生點原地 `WATER`→`FIRE` → 白霧圓盤罩住木樁；走到霧外後點霧裡的木樁 → 英雄不動、STATE `Idle`；等霧散（3s）後點**同一個螢幕位置** → 英雄過去開打（STATE `AttackWindup`、木樁血條下降）＝正向對照（截圖 `v060-10`～`14`）。
  - ③ **火浪**：往前走一小步、`FIRE` → 橙色燃燒圓盤、木樁閃白且血條下降；**原地** `WIND` → 橙色扇形預警、燃燒區消失、木樁血條再掉一段（截圖 `v060-20`～`23`；直式版面 `v060-30`）。
- **出生點的巧合（記錄）**：英雄出生在 (0,0,0)、木樁在 (0,0,6)，出生後不移動直接 `FIRE`，木樁恰在燃燒區圓周（距圓心 3.0＝`BurnRadius`）、`WIND` 時恰在火浪射程邊界（6.0＝`FirestormRangeMeters`）。座標全是整數所以判定穩定落在「界內」，但只要走過一步就不再是邊界情形；試玩想看火浪，先往木樁走一小步最穩。

## 15. v0.6.1：元素反應的回饋——反應名稱飄字＋受困狀態顯示（2026-09-21）

起因、使用者裁定與凍結驗收條件：`docs/V061_FEEDBACK_PLAN.md`（§0 起因、§1 要做的事、§2 不做什麼、§3 F1～F8）。這一版**不動任何玩法數值**，只加「畫面上告訴玩家剛才發生了哪個反應、現在是不是被困住」。

### 這一版在解決什麼

使用者手機試玩 v0.6.0 回報「流沙困住你」「爆沸」感覺不出效果——三個反應其實都有發生（線上 Playwright 四組重現過），但除了黃褐色圈本身，畫面上沒有任何東西標出「剛才是哪個反應」。舊版 STATE 列在縛足時顯示的是狀態機名稱（例如 `Moving`——玩家點地之後狀態機接受了指令，只是位移被縛足擋掉），完全看不出「被困住」這件事。

### 新增的兩件回饋

- **反應飄字**：五個反應（流沙／蒸氣／火浪／爆沸／救援）成立的那一幀，在**被消耗那個區域的圓心**跳一個大字（`characterSize 0.14`，比傷害飄字大一倍以上）、離地 2.6m、各反應不同色、停留 1.2s 後上飄消失。標籤：`QUICKSAND`／`STEAM`／`FIRESTORM`／`BOIL 80`／`RESCUE`。空地火與單放水**不跳字**（GDD 沒把這兩個算進「反應」）。
- **縛足開始的英雄頭上飄字**：縛足由 false→true 的那一幀，在**英雄本體**（不是左上角面板）跳一次 `ROOTED`——玩家的眼睛在角色身上，不在 HUD。同一個流沙重入不再縛足，所以也不再跳。
- **STATE 列**：縛足中顯示常數字串 `ROOTED`；縛足結束但仍在減速中顯示 `SLOWED`；其餘時候照舊顯示狀態機名稱（`Idle`／`Moving`／…）。`InfoRows` 沒有變、沒有加新的一列。

### 規格解讀與自決（可推翻）

- 六個標籤（含 `ROOTED`）預先建好一次的表，索引對齊 `Core/Logic/ElementCalloutLogic.LabelIndexFor`／`RootedLabelIndex`；`BOIL` 的傷害數字讀 `ElementTuning.BoilDamage`，不是寫死的 `"80"`——tuning 改了字面值會跟著換。
- 六種訊息各固定一顆專屬 TextMesh（共 6 顆），字串只在 `Initialize` 設一次、執行期不改字；同一種訊息重複觸發＝刷新該顆的位置與計時，不是新增一顆（因此沒有「淘汰最舊」邏輯）。
- 反應飄字位置一律讀 `TakeSnapshot()` 拍下的區域圓心（該區域這時多半已經被 `Resolve` 消耗掉了）——跟爆沸 AOE 圓心是同一份資料，不是重新算一次。
- `HeroController.OnRootedStarted` 只在 false→true 的邊緣觸發一次；`Phase1Bootstrap` 接到後才知道英雄在哪，飄字位置＝觸發當幀的英雄座標。
- `DebugHud` 的 STATE 值改成一個唯讀屬性（`StateLabel`），每次讀取都重新判斷（縛足／減速是每幀變動的狀態，不能只在狀態機事件時更新一次）；`Phase1Bootstrap.HudStateLabel` 轉交同一個值給 PlayMode 測試（測試 asmdef 看不到 `Vow.UI`，比照批 3／批 4 的 `IDebugHudPanel` 做法，但這裡直接開屬性、沒有動任何既有介面）。

### 已知限制

- 只加了「跳字」與「STATE 列改字」，沒有加音效、粒子、螢幕震動——這些不在使用者這次的裁定範圍內。
- `WaterPool` 這個 `ElementReaction` 列舉值目前沒有任何生產路徑會回傳它（`ElementReactionLogic.Resolve` 從不產生），`LabelIndexFor` 仍把它一併算進「不跳字」，純粹是收斂 switch 的預設分支，沒有新增行為。
- 「符印牆血條蓋住木樁血條」（v0.6.0 就有的既有問題）本輪沒有處理——不在計畫 §1 範圍內。

### 驗證紀錄

- `verify.sh` ALL PASS（純邏輯 193＋2＝195 通過＋1 略過、紅線 8 條期望數不變）；Unity EditMode 0 紅；PlayMode 既有 89 條全在全綠＋新增 10 條（F2-a～f、F3-a／b、F4、F5）全綠；突變 110＋4＝114／114 CAUGHT。
- F6 鑑別力：把 `ReactionCalloutDisplay.Show()` 的 `LastLabel = _labels[labelIndex];` 改成讀 `TextMesh.text` getter（該 getter 每次讀都配置字串，這正是開發中踩到的真因）→ 完整 PlayMode 99 條中 `Combat_UpdateAndLateUpdate_AllocateNothing` 紅（`Update 夾區在 1519 幀內配置了 178 bytes`）；還原後 99／99 全綠。注意：用 `-testFilter` 單獨跑該測試在基準版（v0.6.0）就會紅 96 bytes，單跑不是有效訊號，一律以完整套件為準。
- **送達與線上實機（2026-09-21）**：`origin/gh-pages 849cc87`（2026-09-21 02:25:26 +0800，deploy: VOW v0.6.1 from 2936174）、線上首頁版本列 `v0.6.1 · build 2026-09-20 18:25 UTC · 2936174`。Playwright 手機模擬（844×390、`deviceScaleFactor: 2`、`hasTouch`、CDP 觸控），四次載入 console 皆無 error；**按完鈕後 0.3s 截圖**（截圖 `vow-toolchain/browser-screenshots/v061-*`）：
  - **流沙**（`ELEM: RED`→`WATER`→原地 `ENEMY WALL`）：看得到 `QUICKSAND`（黃褐）、英雄頭上 `ROOTED`（黃）、HUD `STATE ROOTED`；成形後約 2.3s（人不動）HUD 變 `STATE SLOWED`，圈還在（`v061-01`／`03`）。
  - **爆沸**（走近木樁、`WATER`、符印鈕輕點、`FIRE`，`ELEM: BLUE`）：看得到 `BOIL 80`（紅）與頭上傷害數字 `80`，同時仍看得到流沙圈成形時的 `QUICKSAND` 尾巴（`v061-10`）。
  - **救援**（同盤面，`FIRE` 前把 `ELEM` 切成 RED）：看得到 `RESCUE`（綠）與**沒有**傷害數字（`v061-11b`；`v061-11` 是我腳本多按一次 `ELEM` 造成的爆沸，不是產品行為）。
  - **蒸氣**（原地 `WATER`→`FIRE`）：白霧圈＋`STEAM`（`v061-12`）。**限制：`STEAM` 的字色是近白色，疊在白霧圈上對比很低，勉強讀得出來**——待試玩回饋再決定要不要改深色（只改一個顏色常數）。
  - **火浪**（走一小步、`FIRE`、`WIND`）：橙色扇形＋`FIRESTORM`（橙）（`v061-13`）。
- **人手時序的教訓**：v0.6.0 的實測用「機器人式連點」不會發現「沒有回饋」；v0.6.1 起線上實測固定量「按完 0.3s 的畫面看不看得出發生什麼」。

## 16. v0.6.2：血條避讓與 STEAM 對比（2026-09-23）

- 來源 `cdb41d0`：符印牆血條高度修正在前一提交 `7bea574`；本提交將 STEAM 色改為深藍灰 `Color(0.18f, 0.27f, 0.34f)`，並同步升版。其餘反應飄字顏色及玩法數值未改。
- 驗證：`bash Tools/DotnetCheck/verify.sh` 為 195 通過／1 略過、編譯 0 error、紅線全過；Unity batchmode EditMode 192 通過／4 略過／0 失敗，PlayMode 100／100 通過（含木樁前立牆的血條投影回歸）。
- 送達：`origin/gh-pages fcaf70c`（2026-09-23 20:27:04 +0800，`deploy: VOW v0.6.2 from cdb41d0`）；線上首頁 `v0.6.2 · build 2026-09-23 12:27 UTC · cdb41d0`。Playwright 手機模擬 844×390、DPR 2、觸控，載入成功且 console 0 error。
- 線上畫面：原地 `WATER`→`FIRE` 後 0.3 秒可清楚讀到白霧上的深藍灰 `STEAM`（`../vow-toolchain/browser-screenshots/v062-01-steam-0_3s.png`）；英雄靠近木樁並按右下符印鈕後，木樁血條與牆血條上下分開（`v062-03-wall-bars.png`）。其他視角、落點及原生手機手感仍未驗證。

## 17. v0.7.0：灰盒對手攻防（2026-09-23）

玩法與已確認數值見 `docs/V070_GREYBOX_DUEL_PLAN.md`。紅色對手在木樁左後方 `(-4, 0, 8)`；待機時不追擊、不受傷，木樁與練習場仍可使用。點紅色對手**第一下只開局**，第二下才交給既有普攻。開局後對手以 4 m/s 追近，到 1.8m 停步，鎖住英雄當下位置，顯示半徑 1.5m 的紅色圓形預警 0.7 秒；命中扣 20 HP，有符印牆遮住或離開圈外即可避開。每次結算後隔 1 秒再起手。

英雄 100 HP、對手 300 HP；玩家可移動、普攻、微滑步與立符印牆。對局中 `WATER`／`FIRE`／`WIND`／`ELEM` 停用，開局前已有的元素區域與砲台飛彈會清除。任一方倒地後停 2.5 秒，雙方回出生點滿血，既有符印牆與格點清掉；要再點紅色對手才開下一局。停頓期間玩家指令不應留到下一局生效。

**試玩步驟**：① 點紅色對手一次，確認 HUD 顯示對局中且英雄沒有立刻揮刀；② 靠近看紅圈，先站圈內吃一次 20 傷，再用點地或微滑步躲一次；③ 對手與英雄隔牆時看它繞牆，並確認牆能擋住攻擊；④ 點對手普攻至一方倒地，等待重置，再點它開第二局；⑤ 回待機後確認元素鈕恢復。WebGL 只供日常試玩與畫面檢查，原生 Android／iOS 的 120Hz、震動、50／80ms 延遲及 10 人各 10 分鐘手感驗收仍須實機做。

**工程驗證**（WebGL 來源 `e18c0b4`）：
- `bash Tools/DotnetCheck/verify.sh`（2026-09-24 於 `e18c0b4` 實跑）：純邏輯 201 通過／1 略過、Unity 腳本編譯 0 錯、8 條紅線掃描全過。
- Unity EditMode：198 通過／4 略過／0 失敗（`../vow-toolchain/v070-edit-complete.xml`，00:22）；PlayMode：117／117 通過，含 `DuelPlayTests` 16 條與零配置測試（`v070-play-with-new-tests.xml`，09:00）。兩份結果晚於最後一次腳本變更（00:20）與 `DuelPlayTests.cs` 最後修改（00:39）；`e18c0b4` 只提交該測試檔，送達前未再重跑 Unity 測試。
- `python Tools/DotnetCheck/mutation_check.py`（於 `e18c0b4` 的獨立 worktree 實跑）：114／114 CAUGHT、還原後 202 個測試 0 失敗（`v070-mutation-e18c0b4.log`）。先前 `v070-mutation.log`（18／114）與 `v070-mutation-tail.log`（70／96）兩份 FAILED 是與 Unity 同時編譯的干擾結果，已由這次獨立重跑取代。
- WebGL：Unity batchmode `VOWWebGLBuilder.Build` 成功，10.5 MB、耗時 219 秒；首頁版本列 `v0.7.0 · build 2026-09-24 06:34 UTC · e18c0b4`。

**瀏覽器實測**（Playwright，844×390、DPR 2、觸控、swiftshader 軟體渲染約 6 FPS；截圖在 `../vow-toolchain/browser-screenshots/`）：
- 本機建置（`v070-local-a-*`、`-b-*`、`-c-*`）：第一下點紅色對手只開局，RED HP 維持 300；紅圈預警出現後英雄受擊 100→80→60→20；英雄被擊倒時 HUD 顯示 `RESETTING`、HERO HP 0，之後回到 `TAP RED TO START`、雙方滿血；英雄普攻使 RED HP 300→180→0，對手被擊倒後同樣重置；重置後再點紅色對手即開第二局。
- 躲招（`v070-dodge-*`）：開局後持續點地面移動 10.5 秒，紅圈仍在追擊，HERO HP 維持 100；站著不動時約 9 秒內會被擊倒。
- 線上 `https://9gf6p4448m-del.github.io/vow3d/`（`v070-online-*`）：版本列相同；開局→受擊至 40→`RESETTING`（HERO HP 0）→重置滿血。
- 全部場次 console 0 error。

**未驗證**：① KO 停頓期間的觸控是否被封住，瀏覽器沒有驗到：軟體渲染下每張截圖約 3 秒，比 2.5 秒停頓還長，無法確定點擊落在停頓內；這一項以 PlayMode「KO 停頓最後 80 ms 送出指令，重置後不得生效」的測試為準。② 符印牆擋招、繞牆追擊只有 PlayMode 證據，沒有做瀏覽器畫面實測。③ 原生 Android／iOS 的 120Hz、震動、50／80 ms 延遲與 10 人手感測試未做。舊版快取：PWA 若仍顯示 v0.6.2，關閉分頁重開或強制重新整理。

## 18. v0.8.0：七塊板塊佔領迴圈（2026-09-25）

規格與凍結驗收見 `docs/V080_CAPTURE_PLAN.md`（§1 規則與數值、§3 V 條文、§6 修訂紀錄）。7 塊平頂六角鋪在場地內：4 號（南、鏡頭近側）是藍方（英雄）基地、1 號（北）是紅方（對手）基地，其餘 5 塊開局為灰。站進塔心半徑 2.5m 的光圈內自動引導 3.5 秒即翻成己方顏色；受傷、離圈、倒地會讓進度歸零，雙方同在一圈時都凍結。開局後每 1.0 秒整點計分，每方加「己方塊數 × 2」，先到 1000 分者勝（同一次計分雙方都到 1000 時分高者勝、同分平手）。對手會依選點規則依序搶塊，英雄在 6m 內時轉為追打；雙方倒地後在基地（被搶時改在場邊）復活。結算停 3.0 秒後回佔領待機，雙方回 v0.7.0 出生點、結果與比分保留到下一局。佔領模式（待機／對局中／結算）時木樁整個停用，回單挑才恢復；進佔領模式時英雄原本的鎖定會清掉（r2 N1）。

**讀規則的兩個補充**（2026-09-25 fresh read-back 後補）：①比分不截在 1000——每 1.0 秒一次計分、一次加「己方塊數 × 2」，所以最後一次計分可能越過 1000（例如紅方 7 塊時一次 +14，實測停在 1004）；「同一次計分雙方都 ≥1000 才比高低、同分平手」只是這個例外情形的裁決。②紅方基地 1 號在場地正北、藍方基地 4 號的正對面；開局時鏡頭跟著英雄在南邊，844×390 畫面看不到 1 號，往北走就會看到。

**試玩步驟**：① 按右上 `CAPTURE`：按鈕變綠 `CAPTURE: ON`、右上顯示 `CAPTURE: TAP RED`、地板出現 7 塊灰色六角與光圈，木樁消失；② 點紅色對手開局：英雄移到 4 號、畫面顯示 `CAPTURE ACTIVE`、比分 0／0；③ 點左上 5 號塔附近的地面，走進光圈站 3.5 秒看它翻藍；④ 放著不動會被對手逐塊搶走並擊倒，約 95 秒遊戲時間紅方到 1000，顯示 `LAST: RED WINS` 後回待機；⑤ 再點紅色對手開第二局（比分、歸屬重置）；⑥ 在待機時再按 `CAPTURE` 回原本的單挑模式。

**工程驗證**（WebGL 來源 `96e143f`；N1 修正 `0bc2853`）：
- N1（打木樁中按 CAPTURE 對空揮刀）：`Phase1Bootstrap.cs:583-584` 進佔領模式時呼叫 `_hero.CancelCombatForDuel()`；新測試 `CaptureDummyPlayTests.N1_EnteringCaptureModeWhileAttackingTheDummy_ClearsTheHerosTargetAndStopsTheHits`。改壞驗紅：拿掉該行後紅在 `CaptureDummyPlayTests.cs:280`（Expected null，But was Dummy_Target），備份還原 sha256 一致（256684f8…）。
- `bash Tools/DotnetCheck/verify.sh`（`0bc2853` 與 `96e143f` 各跑一次）：純邏輯 233 通過／1 略過、Unity 腳本編譯 0 錯、紅線掃描全過，`RESULT: ALL PASS`。
- Unity EditMode：234 項 229 通過／5 略過／0 失敗（`0bc2853` 與 `96e143f` 各一次，`../vow-toolchain/v080-N1-edit.xml`、`v080-D-edit.xml`）；PlayMode（`0bc2853`，完整一次）：143／143 通過（`v080-N1-play.xml`）。
- `SeedCaptureScoresForTest` 與模擬手指 API 都在 `#if UNITY_EDITOR` 內（`Phase1Bootstrap.cs:167-173,187-193`）。
- WebGL：Unity batchmode `VOWWebGLBuilder.Build` 成功，10.6 MB、耗時 154 秒（`../vow-toolchain/v080-D-webgl-build.log`）；以 `SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 部署同一份產物 → `origin/gh-pages d232ee6`（2026-09-25 12:41:51 +0800）。

**瀏覽器實測**（Playwright；Chromium 844×390、DPR 2、觸控、swiftshader 約 5～12 FPS；截圖在 `../vow-toolchain/browser-screenshots/`；腳本 `../vow-toolchain/v080-online-check.py`、`v080-webkit-check.py`，座標推導 `v080_coords.py`：CAPTURE (786,90)、待機對手 (321,21)、5 號塔心地面 (162,49)）：
- 本機建置預演（`v080-local-*`）與線上（`v080-online-*`）結果一致，兩場 `pageerror`＝0、`console.error`＝0（`../vow-toolchain/v080-local-check.log`、`v080-online-check.log`）。
- 線上版本列：`v0.8.0 · build 2026-09-25 04:27 UTC · 96e143f`。
- `v080-online-01-lobby`：`CAPTURE: ON`（綠）、`CAPTURE: TAP RED`、灰色六角與光圈、木樁已隱藏。
- `v080-online-02-active`：`CAPTURE ACTIVE`、BLUE 0／RED 0、4 號藍。
- `v080-online-03-tower5`（點 5 號後 9.0 秒不輸入）：英雄站在 5 號、5 號為藍，BLUE 34。
- `v080-online-04-red-wins`（之後不輸入 300 秒）：`LAST: RED WINS`、RED 1004、BLUE 108，畫面內各塊全紅，英雄在中央出生點。
- `v080-online-05-second-match`：`CAPTURE ACTIVE`、比分 0／0、4 號藍。
- WebKit `iPhone 13 landscape`（`v080-webkit-loaded.png`、`../vow-toolchain/v080-webkit-check.log`）：7.8 秒內 `vowUnityInstance` 為真、`#vow-loading` 為 `display:none`、版本列同上、`pageerror`＝0、`console.error`＝0。

**已知限制**：
- WebGL 只供日常試玩與看畫面；瀏覽器的輸入延遲、幀率與觸控取樣與原生 App 不同。
- 844×390 畫面下，開局後鏡頭跟著英雄在 4 號，1 號（紅方基地）在畫面外，`02-active` 截圖看不到 1 號的紅色。
- r2 N2（LOW，記錄不修）：佔領待機時元素區仍會傷到隱藏的木樁，沒有錯誤顯示。r2 N4（LOW，記錄不修）：`verify.sh` 的 Unity 編譯檢查固定定義 `UNITY_EDITOR`，正式版路徑要靠 WebGL 建置把關。
- 既有 `ZeroAllocationTests.Combat_UpdateAndLateUpdate_AllocateNothing` 在高負載單跑時會紅在 164 bytes，v0.7.0 起點 `5687f66` 同樣 5/5 紅，與 v0.8.0 無關（`../vow-toolchain/v080-za-summary.txt`）；本次完整 PlayMode 這條是綠的。

**未驗證**：
1. 原生 Android／iOS 的 120Hz、震動、50／80 ms 延遲注入下的手感。
2. 倒地倒數（復活秒數）在線上畫面的可讀性：軟體渲染每張截圖約 3 秒，本輪沒有截到倒地畫面。
3. 對手第二目標在 0.5mm 不等距（h＝6.0625）下的實際表現，只有 PlayMode 證據。
4. 線上畫面中 1 號開局為紅（鏡頭外，見已知限制）。
5. WebKit 只驗載入，沒有在 WebKit 上跑佔領流程。
6. 舊版快取：PWA 若仍顯示 v0.7.0，關閉分頁重開或強制重新整理。

## 19. v0.9.0：19 塊棋盤＋包夾斷能＋劣勢狂怒（2026-09-26）

規格與凍結驗收見 `docs/V090_ENCIRCLE_PLAN.md`（§1 規則與數值、§3 V9 條文、§6 修-1～修-9）。

**規則摘要**：棋盤改為 19 塊平頂六角（中央 1＋中圈 6＋外圈 12，外接半徑 4.375m），晶塔光圈仍是 2.5m、引導 3.5 秒、爭奪凍結、每塊每秒 +2、1000 分勝、倒地 5 秒、結算 3 秒，全部沿用 v0.8.0。v0.8.0 的單一基地塊改成每隊 3 塊「母板塊」：藍方（英雄、鏡頭近側）13、12、14，紅方（對手）在北側對稱的 3 塊。每次翻塊後沿己方相鄰塊做 BFS，連不回任何一塊己方母板塊的己方塊當場中立化（斷能）；母板塊全失時該隊其餘塊全部斷能。斷能那一刻若該隊落後超過 15%（20·(領先分−自身分) > 3·領先分），該隊英雄進入狂怒：移速 +15%、12 秒（再觸發重設為 12 秒），身上出現狂怒光環，藍方狂怒時 HUD 顯示 `RAGE n`。雙方都適用。對手 AI 不看包夾，照舊去最近的非紅塊。開局雙方傳送到第一優先母板塊的復活點：英雄 (0,−16.65625)、對手 (0,16.65625)。

**試玩步驟**：① 按右上 `CAPTURE`：`CAPTURE: ON`、`CAPTURE: TAP RED`，地板出現 19 塊灰色六角（比 v0.8.0 小）；② 點紅色對手開局：`CAPTURE ACTIVE`、比分 0／0，英雄腳下與左右兩側 3 塊藍（13、12、14）；③ 往北點地走進 4 號光圈站 3.5 秒看它翻藍；④ 想看斷能：讓對手翻掉你和母板塊之間的那一塊，孤立的藍塊會當場變灰；落後超過 15% 時英雄身上會出現狂怒光環與 `RAGE n`；⑤ 放著不動約 60～90 秒遊戲時間紅方到 1000，`LAST: RED WINS` 後回待機；⑥ 再點紅色對手開第二局；⑦ 待機時再按 `CAPTURE` 回單挑模式。

**工程驗證**（WebGL 來源 `5b7bab3`，分支 `v090-encircle`）：
- 步驟 A～C 的完整回歸在 `e9a2554` 之前完成（計畫修-9）：verify `RESULT: ALL PASS`、EditMode 0 敗、PlayMode 167／167、突變 152／152。
- `5b7bab3`（只改版本字串 `VowVersion.cs:6` 與 `bundleVersion`）重跑：`UNITY_REFS_DIR=<絕對路徑> bash Tools/DotnetCheck/verify.sh` 純邏輯 251 通過／1 略過、`RESULT: ALL PASS`（`../vow-toolchain/v090d-verify.log`）；Unity EditMode 252 項 246 通過／6 略過／0 失敗（`v090d-edit.xml`）。PlayMode 沒有在版本 commit 上重跑（只改常數字串）。
- `grep -n "SeedCapture" Assets/Scripts`：`Phase1Bootstrap.cs:183,190` 在 `:181` 的 `#if UNITY_EDITOR`～`:194 #endif` 內；`:210` 與 `PlayerInputService.cs:133` 是註解，也都在 `#if UNITY_EDITOR` 區塊內。
- WebGL：batchmode `VOWWebGLBuilder.Build` 成功，10.6 MB、Unity 自報耗時 469 秒（含等待整體 843 秒，`../vow-toolchain/v090-D-webgl-build.log`）；本機 `python -m http.server` 預演（`v090-local-check.log`：`pageerror`＝0、`console.error`＝0）後，以 `SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 部署同一份產物 → `origin/gh-pages f22519c`（2026-09-26 11:10:32 +0800）。

**瀏覽器實測**（Playwright；Chromium 844×390、DPR 2、觸控、swiftshader；截圖在 `../vow-toolchain/browser-screenshots/`；腳本 `../vow-toolchain/v090-online-check.py`、`v090-webkit-check.py`、`v090-d10-rage-online.py`，座標推導 `v090_coords.py`：CAPTURE (786,90)、待機對手 (321,20)、英雄在出生點時 4 號引導點 (0,−9.578125) 地面 (422,55)）：
- 線上版本列：`v0.9.0 · build 2026-09-26 03:04 UTC · 5b7bab3`（sha7＝建置來源）。
- V9-D04～D07 截圖：`v090-online-01-lobby`、`02-active`、`03-tower4`、`04-red-wins`、`05-second-match`（本機預演同名 `v090-local-*`）。畫面判讀由主對話執行，結果：v0.9.0 線上 D05 紅（低幀率走過頭，計畫修-10），其餘見 §20 的 v0.9.1 重驗。
- V9-D08：線上 D02～D07 一場、D10 一場，`pageerror`＝0、`console.error`＝0（`v090-online-check.log`、`browser-screenshots/v090-d10-raw/d10-times.json`）。
- V9-D03 WebKit `iPhone 13 landscape`：8.5 秒內 `vowUnityInstance` 為真、`#vow-loading` 為 `display:none`、`pageerror`＝0、`console.error`＝0（`v090-webkit-check.log`、`v090-webkit-loaded.png`）。
- V9-D10 線上狂怒畫面：依 `v090-rage-script.json` 送出開局＋16 下點地，全部送出，每下比預定秒數晚 0.008～0.093 秒；中途不截圖。點地 CSS 座標取 V9-C07 dt＝1/6 那一跑的鏡頭姿態換成 844×390（1/6 與 1/60 兩組共 34 點都不在左側面板、右上面板、CAPTURE 鈕、符印鈕內，離畫面邊 ≥19.6px；`v090-coords-check.json`）。截圖 8 張（開局前 2 張＋t_r＝47.5 起的 6 張）在 `browser-screenshots/v090-d10-raw/`；每張截圖約 4 秒，所以 t_r 之後的 5 張實際在開局後 49.0、53.2、57.0、60.9、65.0 秒開始（t_r−3 那張在 44.5 秒）。判定結果：**作廢**——線上點擊落點隨鏡頭偏差放大，劇本無法重播（計畫修-10）；使用者 2026-09-26 同意 V9-D10 改為使用者手機實玩觸發狂怒並截圖（見 §20）。

**已知限制**：
- WebGL 只供日常試玩與看畫面；瀏覽器的輸入延遲、幀率與觸控取樣與原生 App 不同。
- 軟體渲染下截一張圖約 4 秒，截圖期間遊戲時間落後牆鐘（計畫 R12、修-5）。
- 既有 `ZeroAllocationTests.Combat` 單跑或排在前段跑恆紅 164 bytes（v0.7.0 起即如此，計畫修-9）。

**未驗證**：
1. 原生 Android／iOS 的 120Hz、震動、50／80 ms 延遲注入下的手感。
2. 線上包夾中立化瞬間的畫面（只截了狂怒時段，沒有對準中立化那一幀；狂怒畫面改由 V9-D10 驗）。
3. 對手在孤島上「翻塊→當場中立化→再翻」循環（R9）的實際頻率：只有 PlayMode 證據（V9-B20 只守不卡住）。
4. 0.3mm 不等距下對手第二個以後目標的實際表現：只有 PlayMode 證據。
5. WebKit 只驗載入，沒有在 WebKit 上跑佔領流程。
6. 舊版快取：PWA 若仍顯示 v0.8.0，關閉分頁重開或強制重新整理。

## 20. v0.9.1：低幀率點地走過頭修正（2026-09-26）

**這一版在解決什麼**：v0.9.0 線上 V9-D05 不通過。瀏覽器／手機每幀 0.2～0.33 秒時，`HeroLocomotion.Step` 一步走 1.4～1.8m、衝過目的地約 1m，走出 4 號光圈、引導歸零（計畫修-10，診斷 `../vow-toolchain/v090-d-diagnosis.md`）。修法：每幀位移夾在到目的地的水平直線距離內；速度、加速度等手感數值不變。英雄與紅方對手共用這段，一併修正。

**工程驗證**（WebGL 來源 `f90cb13`＝修復 `718f649`＋版本字串，分支 `v090-encircle`）：
- 新增 PlayMode `LowFrameRateArrivalPlayTests`（dt＝1/3、1/4，點地 7.81m）：修前紅「越過目的地 1.356417m／0.4397492m」，修後綠，還原修復再紅同數字（`../vow-toolchain/v091-red-prefix-kept.xml`、`v091-green.xml`、`v091-revert-red.xml`）。
- verify 純邏輯 251 通過／1 略過、`RESULT: ALL PASS`（`v091-verify.log`）；EditMode 252 項 246 通過／6 略過／0 失敗（`v091-edit.xml`）；PlayMode 149／149 同批（`v091-play-149.xml`）＋狂怒劇本 18／18（`v091-rage-b1～b5.xml`）。
- WebGL 本機預演 V9-D05：4 號藍、BLUE 86（`v090-diag/v091-local-d05-03-tower4.png`），0 error。
- `SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 部署同一份產物 → `origin/gh-pages 1b69ad1`（2026-09-26 17:23:52 +0800）；線上版本列讀回 `v0.9.1 · build 2026-09-26 07:05 UTC · f90cb13`。

**線上實測**（Chromium 844×390、DPR 2、觸控，`v090-online-check.py`，截圖 `browser-screenshots/v091-online-*`；主對話判讀）：
- V9-D04 過：待機 `CAPTURE: ON`、`CAPTURE: TAP RED`，灰色六角與塔影可見（對比偏淡）；開局 `CAPTURE ACTIVE`、0／0、腳下與左右 3 塊藍。
- V9-D05 過：4 號藍、BLUE 94 ≥ 8。
- V9-D06 過：`LAST: RED WINS`、RED 1002、英雄在畫面中央。
- V9-D07 過：第二局 `CAPTURE ACTIVE`、0／0、3 塊藍。
- V9-D08 過：`pageerror`＝0、`console.error`＝0（`v091-online-check.log`）。
- V9-D03 過：WebKit `iPhone 13 landscape` 載入 v0.9.1、`vowUnityInstance` 真、0 error（`v091-webkit-loaded.png`）；前兩次在 `Page.goto` 連線階段失敗（SSL connect error／逾時，同時段 curl 200），頁面未開始載入，第三次起成功。

**V9-D10（修-10 後）＝使用者手機實玩觸發狂怒並截圖**：狀態**未驗證（使用者豁免，計畫修-11）**：使用者 2026-09-26 手機試玩「通過，但沒看到狂怒」，選擇不重玩、直接併入 main。步驟：① 手機開網址、確認首頁版本列 `v0.9.1`；② 按 `CAPTURE`→點紅色對手開局；③ 先搶北邊幾塊讓藍方塊往前延伸，但**別站著顧**，讓紅方分數領先；④ 等紅方翻掉你和母板塊之間的某一塊——前方孤立的藍塊當場變灰；若此時藍方落後超過 15%，英雄身上出現狂怒光環、HUD 出現 `RAGE n`（12 秒）；⑤ 看到就截圖。

**未驗證**：同 §19 第 1～6 項；另加 7. 手機上走到點是否仍有「滑過頭」感（本版只在 swiftshader 低幀率驗過）。

