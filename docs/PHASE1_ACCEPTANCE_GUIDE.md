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
