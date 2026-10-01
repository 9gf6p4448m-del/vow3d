# 第三人稱戰鬥操作試作計畫（普攻鈕／主動滑步／準星塑牆）

日期：2026-10-01。分支 `camera-lab-20261001`，已知良好狀態 `edbe752`（任何一步做壞就退回這裡）。
授權：使用者本回合裁定三個戰鬥操作設計「全部以建議為準」，只做成可手機試玩的試作；各自牆管理、武器區分、天賦等藍圖其他項目不做。
不 deploy、不推 gh-pages、不碰 `vow-camera-preview` 與主 vow 工作樹；GDD／ARCHITECTURE 正文不改（衝突只記在 §5）。

## 1. 行為規格

### 1.1 普攻鈕 ATK（只在 THIRD 顯示與生效）

- 準星＝螢幕中心＝鏡頭前方。鏡頭旋轉為 `Euler(pitch, yaw, 0)`，視線方向 `(sin yaw·cos pitch, −sin pitch, cos yaw·cos pitch)`；出手用其水平分量 `(sin yaw, cos yaw)`，以英雄位置為原點。
- 按下（觸控 Began 當下，不等放手）就在「準星錐」內挑目標：水平夾角 ≤ 30°、水平距離 ≤ 8m 的存活且本英雄可交戰（`HeroController.CanEngage`）目標中，取夾角最小者；夾角相同（cos 差 < 1e-4）取較近者。
- 挑到後走與「短點敵人」完全相同的出口：`PlayerInputService.OnCombatTargetSelected` → `DuelInputRouter`（開局／對局過濾）→ `HeroCombatBrain.CommandAttack`。射程、攻擊週期、前搖、追擊全部沿用，不新增傷害或命中規則。
- 解讀選擇（記錄）：「朝準星方向出手」實作為「準星錐輔助鎖定＋原普攻」，不是無目標的方向性揮擊——後者需要新的命中判定（武器區分項目），違反「不混入新規則」。錐內沒有目標時按鈕不出手（不對空揮）。30°／8m 為灰盒暫定值。
- 點敵人沿原攻擊（右側短 tap）的既有行為與測試不變。

### 1.2 主動滑步鈕 DASH（只在 THIRD 顯示與生效）

- GDD §貳 與現行實作一致：上限 3 格、每 2.5s 回 1 格、距上一次滑步起手 1.0s 內連段衰減 1.4→0.9→0.5m（`CombatTuning`、`CadenceSim`）。本處沒有 GDD／實作矛盾，不需停手。
- 選擇（最小做法）：主動滑步與命中連動**共用同一個 `CadenceSimState`**（同一組充能、回充計時、連段計數與 1.0s 窗口），經同一個 `MicroCadenceMover.TryExecuteCadenceDash` 起手；一次按壓最多扣 1 格。不新增充能取得來源、不改任何數值。
- 新純邏輯 `ActiveDashLogic`（Core/Logic，零 UnityEngine）依大腦狀態分流：
  - `AttackRelease`（220ms 目押窗口）→ 等同一次命中連動微彈 `CommandFlick`，進 `CadenceDashing`、保留目標與攻擊週期（舊路徑，不重複扣）。
  - `Idle`／`Moving` → 自由滑步：直接起手，大腦狀態不變（狀態機轉換表一格不改；`CadenceDashing` 仍只能由 `AttackRelease` 進入）。
  - `AttackWindup` → 比照既有「前搖可被移動指令打斷」：先 `CommandMove(原地)` 打斷前搖（不命中、不耗攻擊週期、清目標），再自由滑步。
  - `AttackRecovery`／`CadenceDashing`／`CastingRune` → 拒絕，不扣充能、不改狀態（已知限制：收招 0.15s 內按 DASH 不生效）。
  - 充能 0 → 拒絕，不改任何狀態（含 SinceLastDash、ChainCount）；縛足／噴口飛行／已在滑步中 → 拒絕不扣充能（沿用 `TryBeginCadenceDash` 的規則）。
- 方向：搖桿推量 ≥ 0.2（正規化）時＝搖桿方向依鏡頭 yaw 旋轉（與搖桿移動同一換算）；否則＝鏡頭水平前方。
- 自由滑步期間 `HeroController.Update` 暫停搖桿連續移動與導航步進（只套滑步位移），避免與步行位移疊加；命中連動的 `CadenceDashing` 原本就不步行，行為不變。

### 1.3 塑牆（符印鈕）在 THIRD

- 符印鈕仍是「按住拖曳、放手成牆」，手勢（門檻、取消圈、短點赦免、極速點按）完全沿用 `RuneGestureTracker`。
- THIRD 下拖曳**只取拉伸量** `distance01`，方向一律＝鏡頭水平前方；落點與朝向沿用 `RuneCastLogic.TryDragPlacement`（貼身帶前 1/3＝1.2m，之後線性到 8m；牆面法線＝英雄→落點＝鏡頭前方，即牆面正對鏡頭）。虛影走同一換算。
- TOP 下完全沿用舊換算（螢幕方向→世界）；THIRD 的極速點按（正前方 4m）不改。
- 牆的上限、存續、歸屬不改（全隊 2 面）。

### 1.4 按鈕版面與觸控路由

- 新純邏輯 `LabActionButtonLayout`（Input，零 UnityEngine）：兩顆圓鈕直徑沿用符印鈕 16mm、間距 3mm；ATK 在符印鈕左側、DASH 在符印鈕上方（DASH 放不下時退到 ATK 左側）。離右／下邊距 ≥ 符印鈕同一標準 `max(15mm, 8px+8px)`。
- `TouchGestureRouter`：只有 THIRD 且版面已設定時，起手落在 ATK／DASH 的手指在 Began 當下送一次按鈕事件，之後整段觸控被吃掉（不成為轉頭、點擊、微彈）。判定在 UI 區域與符印之後、搖桿／轉頭之前，所以既有優先序不變。事件經可選介面 `IActionButtonSink`，既有 `ITouchGestureSink` 不改。
- 按鈕 IMGUI 沿用 `RuneButtonView` 風格（`DrawTexture` 填色＋粗體白字 ATK／DASH），DASH 鈕顯示當前充能數。

## 2. 改動範圍（預計）

- 新增：`Core/Logic/CameraLabAim.cs`（準星換算、準星錐挑選、牆方向解析）、`Core/Logic/ActiveDashLogic.cs`、`Input/LabActionButtonLayout.cs`；測試 `Assets/Tests/EditMode/CameraLabCombatTests.cs`、`Assets/Tests/PlayMode/CameraLabCombatPlayTests.cs`（含 .meta）。
- 最小接線：`TouchGestureRouter`（按鈕路由）、`PlayerInputService`（轉發按鈕事件、送出準星目標）、`HeroController`（`TryActiveDash`、自由滑步期間暫停步行）、`RuneCaster`／`RuneGhostPreview`（可選方向覆寫，null 時與舊碼逐行同路徑）、`CameraComparisonLab`（版面、按鈕處理、覆寫開關、IMGUI、WebGL 驗證用 log）、`PureLogic.Tests.csproj`（納入新 Input 純檔）、`mutation_check.py`（新增突變）。
- asmdef 依賴方向不變：Core/Logic 純 C#；Input→Core；Combat→Core；Bootstrap→全部。不新增套件、不改 Unity 版本。

## 3. 凍結驗收條件（2026-10-01 訂定即凍結）

每條附「什麼實作會讓它變紅」。修不過改實作；條件本身錯要寫明「原標準錯在哪、為何現在才知道」回報，不自行降標。

### A 普攻鈕（純邏輯 EditMode＋dotnet）

- A1 `CameraLabAim` 準星換算：yaw∈{0,30,90,180,270}、pitch∈{10,25,50} 的視線向量等於 `(sin y·cos p, −sin p, cos y·cos p)`，水平方向等於 `(sin y, cos y)`，誤差 ≤1e-5。紅：sin/cos 對調、pitch 正負號錯、水平分量未正規化。
- A2 準星錐挑選：正前方 3m 被選；夾角 29° 選、31° 不選；距離 7.99m 選、8.01m 不選；正後方不選；錐內兩目標取夾角小者（即使較遠）；夾角相同取較近；無候選回 −1。紅：忽略錐角、忽略距離、改成取最近、用 3D 視線（含 pitch）判角。
- A3 既有測試原封不動：`git diff --name-status edbe752 -- Assets/Tests` 只有 `A`（新增）行，沒有任何 `M`／`D`／`R`。紅：改動、刪除或改名任何既有測試檔（含 `HeroCombatBrainTests`、`ThirdPersonTouch*`、`TouchGestureRouterTests`）。

### B 主動滑步（純邏輯 EditMode＋dotnet）

- B1 數值字面值：`new CombatTuning()` 的 `MaxCharges==3`、`ChargeRecoverySeconds==2.5f`、`ChainWindowSeconds==1.0f`、`DashDistances=={1.4f,0.9f,0.5f}`、`CadenceWindowSeconds==0.22f`（精確相等）。紅：任何數值改動。
- B2 衰減經主動滑步實跑：Idle 下連按三次（每次等前一滑步結束，間隔 0.2s）起手距離依序 1.4／0.9／0.5（±1e-4）；距上一次起手 >1.0s 後再按回到 1.4。紅：主動滑步另開一套連段、窗口不是 1.0s、不衰減。
- B3 充能 0：結果為 `NoCharge`，大腦狀態、`CadenceSimState` 全欄位（Charges／RecoveryTimer／ChainCount／SinceLastDash／DashActive）與起手次數不變；在 Idle、Moving、AttackWindup、AttackRelease 各驗一次（AttackWindup 不得被打斷）。紅：0 充能仍滑、或先打斷前搖再失敗、或改動連段計數。
- B4 共用不重複扣：命中後在目押窗口按 DASH → 充能 3→2、進 `CadenceDashing`、起手次數 +1（不是 +2）、目標保留；接著 1.0s 內於 Idle 再按 → 0.9m、充能 1；命中連動 `CommandFlick` 與主動滑步交錯時共用同一連段（第二次為 0.9）。紅：兩套充能、一次按壓扣兩格、窗口內按 DASH 走自由滑步而清掉目標。
- B5 方向規則：yaw 0 搖桿 (1,0)→(1,0)；yaw 90 搖桿 (0,1)→(1,0)；yaw 180 搖桿 (0.5,0)→(−1,0)；搖桿長度 0.19 → 鏡頭前方 (sin yaw, cos yaw)；搖桿 (0,0) yaw 90 → (1,0)。誤差 ≤1e-5。紅：忽略搖桿、未依 yaw 旋轉、死區錯誤。
- B6 狀態分流：Idle／Moving→`FreeDash`（大腦狀態不變）；AttackWindup→`FreeDash` 且前搖被打斷（之後不會出現命中）；AttackRelease→`CadenceFlick`；AttackRecovery／CadenceDashing→`Rejected` 且充能不變。紅：分流表任一格不同。
- B7 命中連動 220ms 窗口既有測試原封不動並全綠（A3 機制＋F1）。

### C 塑牆（純邏輯 EditMode＋dotnet）

- C1 THIRD：英雄 (2,−1)、yaw 90、`distance01`=1 → 牆心 (10,−1)、法線 (1,0)；`distance01`=0.2 → 1.2m（貼身帶）；`distance01`=2/3 → 4.6m；兩個不同螢幕拖曳方向得到逐值相同的落點。誤差 ≤1e-4。紅：方向仍取螢幕拖曳、貼身帶失效、法線不是鏡頭前方。
- C2 TOP 回歸：`thirdPerson=false` 時解析出的方向對 {螢幕方向 × yaw} 格點與輸入**位元相同**，落點與直接呼叫 `TryDragPlacement` 逐值相同。紅：TOP 也被準星覆寫或方向被正規化／改寫。
- C3 取消半徑沿用：THIRD 路由下符印拖出門檻再滑回原點放手 → `Cancelled`；短促點按 → `QuickCast`。紅：THIRD 改了符印手勢判定。

### D Unity PlayMode（`CameraLabCombatPlayTests`）

- D1 THIRD、英雄 (0,0,3)、yaw 0、木樁 (0,0,6)：`SendScreenTap` 點 ATK 鈕中心 → 同幀 `CurrentTarget` 為木樁；1.0s 內木樁 Health 低於初值。另以「搖桿按住推動＋同時點 ATK」重做一次仍鎖定木樁。紅：按鈕未接線、方向錯（挑到對手或空）、與搖桿互搶。
- D2 THIRD、空曠處、無搖桿、yaw 0：點 DASH → 0.3s 後水平位移 z 方向 1.4±0.05m、|Δx|<0.05、充能 3→2；yaw 90 再做（等充能／窗口歸零後）位移 x 方向 1.4±0.05m。紅：距離錯、方向不跟鏡頭、與步行疊加。
- D3 THIRD 符印：按住符印鈕往任意方向拉滿後放手 → 有一面牆存活，牆心距「英雄 + 鏡頭前方×8m」水平 ≤0.1m，牆 forward 與鏡頭水平前方內積 ≥0.999；yaw 90 再做一次牆在 +x。拖曳中虛影位置與最終牆心水平差 ≤0.05m。紅：仍用螢幕方向、虛影與實牆不同換算。
- D4 零配置：新測試在 THIRD 以 `Update` 內的 driver 每隔數幀經真路由點 ATK／DASH（含實際起手與鎖定），180 幀窗口 `UpdateBytes==0` 且 `LateUpdateBytes==0`，且窗口內 ATK 鎖定 ≥1 次、DASH 起手 ≥1 次（否則 0 byte 無鑑別力）；既有 `ZeroAllocationTests` 全綠。紅：按鈕路徑每次配置（字串、閉包、LINQ、陣列）。
- D5 TOP 不變：TOP 下點 ATK／DASH 鈕所在位置＝原點地移動（移動事件 1 次、按鈕事件 0 次）；TOP 符印拖曳的牆心等於舊換算 `ScreenToWorldGroundDirection` 的結果（±0.01m）；既有 PlayMode 全套不新增失敗（見 F4）。紅：TOP 也吃到新按鈕或準星。

### E 觸控路由與版面（純邏輯 EditMode＋dotnet）

- E1 搖桿手指按住推動中，第二指按 ATK → 按鈕事件 Attack 恰 1 次、`MoveX/MoveY` 不變且仍 `MoveHeld`；ATK 手指之後移動／放開不產生點擊、微彈、look。紅：按鈕被當轉頭或世界點擊、搶走搖桿。
- E2 轉頭手指拖曳中，第二指按 DASH → Dash 恰 1 次；DASH 手指移動不累加 `LookDelta`，轉頭手指持續累加。紅：互搶觸點。
- E3 按鈕只在 Began 觸發一次；符印區仍歸 Rune；`SetThirdPersonEnabled(false)` 時落在按鈕位置的手指照舊走世界路由（tap 出 `OnWorldTap`）、按鈕事件 0。紅：TOP 也觸發、Moved/Ended 重複觸發。
- E4 版面不變量，對 (844×390, 6.3px/mm)、(640×360, 6.3)、(1280×720, 7.56)、(1688×780, 7.56)、(1688×780, 12.6)、(2532×1170, 18.9) 每組：兩鈕完全在螢幕內且不在 8px 邊緣死區；離右邊與下邊距離 ≥ `max(15mm×ppmm, 16px)`（與符印鈕同一標準）；不與符印鈕重疊、彼此不重疊；`XMin ≥ 0.5×寬`；不與搖桿區 (0,0)-(0.42w,0.55h) 重疊；上緣 ≤ 高−8px。紅：邊距縮小、壓到符印或搖桿區、跑出螢幕。

### F 回歸與鑑別力

- F1 `Tools/DotnetCheck/verify.sh` exit 0、`RESULT: ALL PASS`；純邏輯測試「失敗 0、略過 1、通過 ≥ 333＋新增數」。紅：既有任一測試轉紅或被跳過。
- F2 既有測試斷言不刪不鬆：A3 的 diff 檢查；另 `git diff edbe752 -- Tools/DotnetCheck/mutation_check.py` 只有新增行（既有突變一條不改）。
- F3 突變檢查在獨立 worktree 執行：既有全部突變仍 caught；新增 ≥6 個針對新邏輯的突變（準星 sin/cos 對調、錐角失效、主動滑步 Idle 拒絕、方向忽略搖桿、按鈕路由不吃觸點、版面邊距縮小、TOP 牆方向被覆寫）全部 caught，還原後全綠。
- F4 Unity：定向 EditMode（新＋路由／大腦相關）與定向 PlayMode（新＋ThirdPerson／CameraComparisonLab／RuneWall／ZeroAllocation）全綠；完整 EditMode 與完整 PlayMode 的失敗集合 ⊆ `edbe752` 同環境基線的失敗集合（基線若非 0 敗則另跑一次 `edbe752` 取得）。紅：新增任何失敗。
- F5 WebGL 本機建置 exit 0；Playwright 844×390、DPR2、hasTouch，以 CDP `Input.dispatchTouchEvent` 連續操作（不靠截圖判斷時序）：THIRD → ATK 鎖定木樁、DASH 位移、符印拖曳成牆，皆由頁面 console 的 `[CAMERA LAB]` 紀錄行機械判定（ATK 目標名、DASH 位移 1.4±0.1m、牆心與準星前方落點誤差 ≤0.2m）；Chromium 與 WebKit 各載入一次無 `pageerror`。紅：建置失敗、任一操作 log 不符、頁面錯誤。

## 4. 未涵蓋（不宣稱）

真機手感、原生效能、正式 HUD 美術、線上送達（本輪不部署）、多人戰場、武器區分、各自牆管理、天賦互動調整。WebGL 與模擬觸控不能代替真機。

## 5. 與 GDD 衝突清單（只記錄，不改 GDD）

- GDD:49 普攻後搖綁定「微滑步嚴禁對空狂甩，僅在普攻命中切除後搖（220ms 窗口）觸發」：主動滑步鈕在 Idle／Moving／前搖可對空起手。
- GDD:66-67 高壓紅線「日常移動一律維持全螢幕點地」：THIRD 用搖桿日常移動（前一輪已存在，本輪沿用）。
- GDD:100 全隊石牆上限 2 面：本輪沿用，未改成藍圖的「各自管理」。
- GDD:88（§參-1）拖曳「拇指角度決定石牆朝向」：THIRD 改為只取拉伸量、朝向固定為鏡頭前方。
- GDD:56（§貳-3）智慧吸附 8 向：主動滑步方向取搖桿連續角度，不吸附。
