# VOW 誓約 — Codex 接手紀錄（2026-09-29 更新）

## 目前狀態：v0.12.0 地脈共振迷霧已部署，線上互動已核對

- 工作分支 `v0120-tectonic-fog`，已知良好起點 `713166edebb595d2fa345364fc2941bd014fbe31`；凍結規格在 `docs/V0120_TECTONIC_FOG_PLAN.md`。使用者裁定：暗區保留地形／晶塔輪廓並可點地探索、6m 局部視野、雙方板塊真視野、紅方 AI 對稱遵守視野。來源提交 `a0c4e99ad9b6fc38e55a9246e533bec1874be21d`（2026-09-29 13:06:52 +08:00）；工作分支已推到 `origin/v0120-tectonic-fog` 並讀回同 SHA。
- 迷霧只在正式 19 塊佔領局 Active 生效。敵方英雄／牆的外觀、點選與持續鎖定共用視野判定；己方板塊真視野可揭露蒸氣目標，6m 局部視野仍受蒸氣限制；紅方失去視野即停止追擊。BFS 斷能當 tick 失去真視野，第二局歸屬與視野重置。舊 V9 測試以 Editor-only、每場獨立的迷霧關閉入口保留原測試前提，正式版預設開啟。
- `UNITY_REFS_DIR=<vow-toolchain/refs 絕對路徑> bash Tools/DotnetCheck/verify.sh`：純邏輯 296 通過／1 略過，Unity 腳本編譯 0 錯，靜態掃描全 PASS。Unity `v0120-full-edit-r2.xml`：290 通過／7 略過／0 失敗；`v0120-full-play-r2.xml`：204／204 通過；後補定向 `v0120-fog-play-r5.xml`：5／5 通過（紅方 AI 追擊進出）；完整 EditMode 已包含 BFS 斷能與第二局測試。完整 PlayMode 後只新增兩項測試與版號，未改遊戲邏輯。獨立 code review 最終 APPROVE。
- 正確的 WebGL Unity 安裝是 `C:\Users\shung\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe`（含 WebGLSupport）。`Builds/v0120-release-build-r2.log`：WebGL 建置成功，10.6 MB／400 秒；首頁 `v0.12.0 · build 2026-09-29 05:16 UTC · a0c4e99`。本機 Chromium 844×390、640×360、1280×720 截圖已目視暗區、地形與塔輪廓、HUD；頁面／console error 各 0。線上 [試玩](https://9gf6p4448m-del.github.io/vow3d/) 部署 `origin/gh-pages 8305ba8cdccfd6006e7cd5c15c45fd26e96ffca1`（2026-09-29 13:19:23 +08:00），無快取 HTTP 及新瀏覽器實際點選 CAPTURE→開局讀回同一版號、迷霧畫面、0 error。細節在驗收指南 §23；PWA 若顯示舊版，關閉分頁重開或強制重新整理並核對底部 `a0c4e99`。
- 使用者 2026-09-29 回報 v0.12.0「試玩通過」，採記為本版線上試玩的人工驗收；未指明試玩裝置或逐項場景。原生 Android／iOS 的幀率、震動、延遲及觸控手感仍未驗證，本機 WebGL FPS 偏低不可代替。按 `docs/V090_ENCIRCLE_PLAN.md` 裁定 1，下一批是深淵先鋒，再來是立體地貌；改玩法前先凍結下一批規格。

## v0.11.0 誓約天賦歷史驗收

- 工作分支 `v0110-pact-talents`，已知良好起點 `9341813601c38a8e59541557b3f46023c0965757`；規格與分步驗收在 `docs/V0110_PACT_TALENTS_PLAN.md`。使用者已裁定佔領 Active 開放元素、任一方先達 250／500／750 分則雙方同時開放該階。
- 使用者已以「按照建議」裁定 B2／B3／AI 的玩法邊界，逐項凍結條件見計畫。A／B0／B1／B2／B3／C 與正常佔領局紅方石牆已接線；紅方目前沒有第一、二階真正可用的天賦，因此不假選、不跳到第三階。2026-09-29 `UNITY_REFS_DIR=<vow-toolchain/refs> bash Tools/DotnetCheck/verify.sh`：純邏輯 289 通過／1 略過、Unity 編譯 0 error、靜態掃描全 PASS。Unity EditMode `v0110-release-edit.xml`：283 通過／7 略過；完整 PlayMode `v0110-engaged-wall-all-play.xml`：200／200 通過，含舊 C07、裂風矢同線敵／己牆、碎岩震、AI 紅牆繞行／近戰破牆 220 盾／恢復佔點，以及三種 Combo、地脈施法後移位、紅 Combo 命中藍英雄先耗盾、敵／己／中立牆與中立木樁。第三輪獨立 code review 為 APPROVE，前兩輪的三個 finding 已修並補測。
- 全套 PlayMode `v0110-final-play-r2.xml` 曾為 190／200，10 個舊 C07 狂怒劇本失敗；已隔離並修正 640×480 HUD 元素列遮住舊世界點擊，以及紅方 AI 放牆改變舊移動軌跡兩個原因。修正後 `v0110-engaged-wall-all-play.xml` **200／200 通過**（含舊 C07 與新版 AI）；`v0110-release-edit.xml` EditMode 283 通過／7 略過；`UNITY_REFS_DIR=<vow-toolchain/refs> bash Tools/DotnetCheck/verify.sh` 純邏輯 289 通過／1 略過、Unity 編譯 0 error、靜態紅線全 PASS。
- 建置來源 commit `6126f60`（2026-09-29 10:15:59 +08:00）含 Unity bundleVersion 0.11.0；工作分支後續另補驗收文件。`Builds/v0110-release-build-r2.log` 記錄 WebGL 建置成功，10.6 MB／181 秒，首頁 `v0.11.0 · build 2026-09-29 02:19 UTC · 6126f60`。本機 `v0110-local-6126f60-r2` 的 844×390、640×360、1280×720 截圖已目視：天賦盤三選項與倒數、比分、元素鈕均可讀且不互蓋，頁面／console error 各 0；`v0110-local-interaction-6126f60` 真觸控選 SWIFT 後盤面消失，點 WATER 後出現藍色水域與冷卻。部署 `origin/gh-pages fed8887`（2026-09-29 10:30:08 +08:00）；無快取 HTTP 讀回與 `v0110-online-6126f60/capture-active.png` 均顯示 v0.11.0，同時可見 14:58、`SANCT 15%`、0／0 與元素列，線上頁面／console error 各 0。[線上試玩](https://9gf6p4448m-del.github.io/vow3d/)；PWA 若顯示舊版，關閉分頁重開或強制重新整理，核對底部 `6126f60`。
- 使用者 2026-09-29 回報「試玩通過」，採記為 v0.11.0 線上試玩的人工驗收；未逐項指明九個天賦搭配或原生裝置。原生 Android／iOS 的幀率、震動、延遲與觸控手感仍未驗證；本機 WebGL 的低 FPS 不能作原生手感驗收。依 `docs/V090_ENCIRCLE_PLAN.md` 裁定 1，後續順序為地脈共振迷霧 → 深淵先鋒 → 立體地貌；下一批迷霧尚待定義視野與可點選邊界。

## v0.10.0 已部署，線上 D02～D07 驗收通過

- Unity 主專案在分支 `v0100-sanctuary`；來源 `bb090ed`（2026-09-28 21:17:47 +08:00），部署 `origin/gh-pages 74a2c35`（2026-09-28 22:09:02 +08:00）。[線上試玩](https://9gf6p4448m-del.github.io/vow3d/) 已讀回 `v0.10.0 · build 2026-09-28 13:39 UTC · bb090ed`。凍結規格 `docs/V0100_SANCTUARY_PLAN.md`；詳細證據與試玩步驟在驗收指南 §21。
- 規則：母板塊聖所減傷 15%、奪回 1.8 秒、連續圍城 2 分鐘後聖所衰減、15 分鐘倒數、每塊每秒 1／7 分。A～C 已完成；`227ec63` 的 PlayMode 主組 162／162、C07 劇本 18／18，突變獨立重跑 172／172 CAUGHT、0 MISSED。`bb090ed` 上執行 `UNITY_REFS_DIR=<vow-toolchain/refs> bash Tools/DotnetCheck/verify.sh` 為 266 通過／1 略過、編譯 0 error、`RESULT: ALL PASS`；`run_unity.sh v0100-D-edit EditMode` 的 XML 為 260 通過／7 略過／0 失敗。WebGL 10.6 MB 建置成功。
- 線上 Chromium 844×390、DPR 2、觸控的 D04 截圖已目視通過：待機無倒數；開局 `15:00`、`SANCT 15%`、`CAPTURE ACTIVE`、0／0、3 塊藍色母板塊。WebKit `iPhone 13 landscape` 載入 12.996 秒、0 error。2026-09-29 再跑完整放置局：D05 連拍 39 張、後段 17 張，753.328 秒牆鐘內見 `SANCT 15%`＋HP 49、`RESPAWN`、`SIEGE 0%`、`LAST: RED WINS`／RED 1001；逐張目視計分均在凍結上限內。人工切段較遲，前段持續密集截圖到 11:53，沒有放置期間輸入。D06 第二局 14:59、0／0、3 塊藍，點 4 號後 9.275 秒靜置，4 號翻藍、BLUE 8、`SANCT` 消失。該段 `pageerror`／`console.error` 均 0；先前 D02～D04 與 WebKit D03 也均 0。證據在驗收指南 §21。
- 使用者手機試玩後補正：`SANCT 15%` 顯示時 HERO HP 看到 **83**（首刀 17 點）；第二局操作回報「這個有」，手機手感「可以」，並表示試玩項目都通過。完整 D05～D07 瀏覽器證據已於 2026-09-29 另行補齊，詳見驗收指南 §21。
- 使用者 2026-09-28 曾裁定「先部署試玩，明確標記 D05 未驗證」；當時未豁免判準，現已以無 OCR 的連續截圖補驗。真人對局是否約 12 分鐘、線上時間到、奪回 1.8 秒、紅方聖所與原生 App 手感仍未驗證。下一步按原藍圖進入 Pact Talents；未經裁定不要更動策略參數。

## 2026-09-26 舊狀態快照

### 當時狀態

- 專案：Unity 2022.3.62f1、C#、URP。工程約束見 `ARCHITECTURE.md`，詳細驗收見 `docs/PHASE1_ACCEPTANCE_GUIDE.md`。
- Claude Code 交接基線為 `e6e9d30`；Codex 入口與本紀錄於 `d652ce4` 加入。開始工作時以實際 `git status` 與 `git log` 為準；`e6e9d30` 記錄 v0.6.1 驗收與線上試玩結果。
- v0.6.1 新增五種元素反應飄字、英雄受困 `ROOTED` 飄字與 HUD `ROOTED`／`SLOWED`。規格與凍結驗收見 `docs/V061_FEEDBACK_PLAN.md`；結果見驗收指南 §15。
- 驗收指南記錄：`verify.sh` 195 通過、1 略過；Unity EditMode 0 紅、PlayMode 99 全綠；突變 114／114 CAUGHT。這是 2026-09-21 的既有紀錄；2026-09-23 的重新驗證見下節。
- `origin/gh-pages` 為 `1b69ad1`（2026-09-26 17:23:52 +0800），部署 v0.9.1（v0.9.0 的 19 塊棋盤＋包夾斷能＋劣勢狂怒，加低幀率點地走過頭修正），建置來源 `f90cb13`（分支 `v090-encircle`，2026-09-27 以 fast-forward 併入 main）；線上首頁顯示 `v0.9.1 · build 2026-09-26 07:05 UTC · f90cb13`。驗收指南 §19、§20 記有實測結果。可回退送達點：v0.9.0 的 `f22519c`（來源 `5b7bab3`）、v0.8.0 的 `d232ee6`（來源 `96e143f`）。

### 當時未決事項與下一步

1. 原生 Android／iOS 的 120Hz、震動與延遲注入下真人手感仍未驗證；WebGL 試玩不能代替原生裝置驗收。
2. v0.6.2 的血條避讓已驗證「英雄靠近木樁並極速立牆」的線上畫面；其他視角或落點尚未全面驗證。
3. 下一批玩法方向已選定為「灰盒對手攻防」：點紅色對手開局、追擊＋單招、無元素、英雄 100 HP／對手 300 HP／敵擊 20／預警 0.7 秒、任一方倒地後停 2.5 秒，雙方重置再點開局。完整規格與分步驗收見 `docs/V070_GREYBOX_DUEL_PLAN.md`；對手移速 4 m/s、停追距離 1.8 m、攻擊圈半徑 1.5 m、恢復 1 秒已獲使用者確認，實作分支為 `v070-greybox-duel`。

## 交接檢查

開始工作時重新執行 `git status --short --branch`、`git log -5 --oneline`、`git log origin/gh-pages -1`；以當下結果為準。若要宣告新版本完成，附改動檔案、實跑指令與輸出，以及部署後的送達證明。

## 2026-09-26 v0.9.1 送達（低幀率點地走過頭修正）

- 修復 `718f649`（`HeroLocomotion.Step` 每幀位移夾在剩餘距離內；新增 PlayMode `LowFrameRateArrivalPlayTests`，修前紅／修後綠／還原再紅）、版本字串 `f90cb13`（建置來源）。原因與使用者裁定見 `docs/V090_ENCIRCLE_PLAN.md` 修-10。
- 回歸：verify 251 通過／1 略過、`RESULT: ALL PASS`；EditMode 246 通過／6 略過／0 失敗；PlayMode 149／149＋狂怒劇本 18／18。
- `SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 部署本機預演過的同一份產物 → `origin/gh-pages 1b69ad1`（2026-09-26 17:23:52 +0800）。
- 線上 Chromium V9-D04～D08 主對話判讀全過（D05：4 號藍、BLUE 94；D06：RED 1002），WebKit iPhone 載入 0 error。細節見驗收指南 §20。
- 使用者手機試玩通過，但沒看到狂怒；使用者選擇 V9-D10 不驗、直接併入 main（計畫修-11）。**線上狂怒畫面仍未驗證**。
- 下一步：① 玩法方向對齊（Phase 3 後續）；② 原生裝置手感；③ 想補看狂怒可照驗收指南 §20 的步驟。

## 2026-09-26 v0.9.0 送達（19 塊棋盤＋包夾斷能＋劣勢狂怒）

- 分支 `v090-encircle`：步驟 A～C 到 `e9a2554`（計畫修-9：verify ALL PASS、EditMode 0 敗、PlayMode 167／167、突變 152／152），版本號 `5b7bab3`（建置來源）。規格與凍結驗收見 `docs/V090_ENCIRCLE_PLAN.md`。
- `5b7bab3` 重跑：verify 純邏輯 251 通過／1 略過、`RESULT: ALL PASS`；Unity EditMode 246 通過／6 略過／0 失敗。
- WebGL 10.6 MB（batchmode `VOWWebGLBuilder.Build`，Unity 自報 469 秒），本機 `python -m http.server` 預演 0 error 後以 `SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 部署同一份產物 → `origin/gh-pages f22519c`（2026-09-26 11:10:32 +0800）；線上版本列讀回 `v0.9.0 · build 2026-09-26 03:04 UTC · 5b7bab3`。
- 線上實測（Chromium 844×390、DPR 2、觸控）：CAPTURE→開局→翻 4 號→放置 300 秒→第二局，另一場依 V9-C07 劇本送 16 下點地並截狂怒時段 8 張；兩場 `pageerror`／`console.error` 皆 0。WebKit `iPhone 13 landscape` 載入成功、0 error。D04～D07 截圖判讀與 D10 盲判由主對話執行，結果待補（驗收指南 §19）。
- 下一步：① 主對話判讀 D04～D07、盲判 D10，通過後併入 main；② 手機試玩 v0.9.0（斷能與狂怒是否看得懂）；③ 原生裝置手感。

## 2026-09-25 v0.8.0 送達（七塊板塊佔領迴圈）

- 分支 `v080-capture`：步驟 A～C、r1 修補 `61a3168`、r2 N1 `0bc2853`（進佔領模式時清掉英雄鎖定）、版本號 `96e143f`（建置來源）。規格與凍結驗收見 `docs/V080_CAPTURE_PLAN.md`。
- `UNITY_REFS_DIR=../vow-toolchain/refs bash Tools/DotnetCheck/verify.sh`：233 通過／1 略過、編譯 0 錯、紅線全過，`RESULT: ALL PASS`；Unity EditMode 229 通過／5 略過／0 失敗；PlayMode 143／143（`0bc2853`）。
- WebGL 10.6 MB、154 秒（batchmode `VOWWebGLBuilder.Build`），本機 `python -m http.server` 預演通過後以 `SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 部署同一份產物 → `origin/gh-pages d232ee6`（2026-09-25 12:41:51 +0800）；線上版本列讀回 `v0.8.0 · build 2026-09-25 04:27 UTC · 96e143f`。
- 線上實測（Chromium 844×390、DPR 2、觸控、swiftshader）：CAPTURE→待機→開局→英雄翻 5 號→放置 300 秒紅勝（RED 1004）→第二局，`pageerror`／`console.error` 皆 0；WebKit `iPhone 13 landscape` 載入成功、0 error。細節、截圖與未驗證項見驗收指南 §18。
- 下一步：① 手機試玩 v0.8.0（倒地倒數可讀性、1 號開局紅色在畫面外）；② 原生裝置驗 120Hz、震動與延遲注入下的手感。

## 2026-09-24 v0.7.0 送達（灰盒對手攻防）

- 分支 `v070-greybox-duel`：A `3ea449f`、B `482f31b`、C `dcf8ba6`＋補測 `e18c0b4`（對手擊倒英雄後重置、對手腳下立牆推出、50 ms 延遲開局一次）。2026-09-24 以 fast-forward 併入 main（`origin/main` 3b10ad5→61ffcd3）。
- `bash Tools/DotnetCheck/verify.sh`：201 通過／1 略過、編譯 0 error、8 條紅線全過；Unity EditMode 198 通過／4 略過／0 失敗；PlayMode 117／117；`mutation_check.py` 114／114 CAUGHT（獨立 worktree 實跑）。
- WebGL 10.5 MB，以 `SKIP_BUILD=1 bash Tools/deploy-webgl.sh` 部署剛才實測過的那份產物 → `origin/gh-pages f9d0d12`；線上版本列讀回 `v0.7.0 · build 2026-09-24 06:34 UTC · e18c0b4`。
- 手機模擬實測（本機與線上，844×390、DPR 2、觸控）：開局只開不打、受擊 20、KO→2.5 秒→重置、再開第二局、英雄普攻擊倒對手、移動躲招，console 0 error。細節與未驗證項見驗收指南 §17。
- 下一步：① 在原生裝置驗 120Hz、震動與延遲注入下的手感；② KO 停頓期間的輸入封鎖目前只有 PlayMode 證據，要在實機或較快的瀏覽器環境補驗。

## 2026-09-23 v0.6.2 送達

- 使用者選定本版包含血條避讓與 STEAM 對比；來源提交 `cdb41d0` 將 STEAM 改為深藍灰，並同步更新程式版號與 Unity `bundleVersion`。
- `bash Tools/DotnetCheck/verify.sh`：195 通過／1 略過、Unity 腳本編譯 0 error、8 條紅線掃描全過；Unity batchmode EditMode：192 通過／4 略過／0 失敗；PlayMode：100／100 通過。
- `bash Tools/deploy-webgl.sh`：重新建置 11 MB WebGL，推送 `origin/gh-pages fcaf70c`；首頁版本列讀回 `v0.6.2 · build 2026-09-23 12:27 UTC · cdb41d0`。
- Playwright 線上手機模擬（844×390、DPR 2、觸控）：載入成功、console 0 error；`WATER`→`FIRE` 後 0.3 秒，深藍灰 `STEAM` 在白霧上清楚可讀；英雄靠近木樁並按符印鈕後，牆與木樁血條分開。畫面在 `../vow-toolchain/browser-screenshots/v062-01-steam-0_3s.png`、`v062-03-wall-bars.png`。

## 2026-09-23 Codex 接手進度

- VOW 已登錄為 Codex 本機專案，根目錄為本 Git 倉庫；Codex 專案清單讀回 ID `01a0ce1e-2395-7073-9ebe-c4db69666b26`。
- 符印牆血條的高度由牆心上方 1.4m 調為 1.9m；場景內 6 面池牆與場景建置器同步。PlayMode 新增木樁前 4m 極速立牆的畫面投影測試，修正前 0/1、修正後 1/1；完整 PlayMode 100/100。
- `Tools/DotnetCheck/verify.sh`：純邏輯 195 通過／1 略過、Unity 腳本編譯 0 error、8 條紅線掃描全過。本機 WebGL 建置成功（10.5 MB），本機瀏覽器試玩可見兩條血條分開、console 無 error。這是發布前的本機結果；v0.6.2 送達證明見上節。
- 當時 `STEAM` 飄字對比未改、線上版仍是 `origin/gh-pages 849cc87` 的 v0.6.1；v0.6.2 已處理並送達（見上節）。原生 Android／iOS 手感仍待實機驗收。
