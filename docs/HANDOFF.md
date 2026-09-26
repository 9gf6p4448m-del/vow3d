# VOW 誓約 — Codex 接手紀錄（2026-09-26 更新）

## 目前狀態

- 專案：Unity 2022.3.62f1、C#、URP。工程約束見 `ARCHITECTURE.md`，詳細驗收見 `docs/PHASE1_ACCEPTANCE_GUIDE.md`。
- Claude Code 交接基線為 `e6e9d30`；Codex 入口與本紀錄於 `d652ce4` 加入。開始工作時以實際 `git status` 與 `git log` 為準；`e6e9d30` 記錄 v0.6.1 驗收與線上試玩結果。
- v0.6.1 新增五種元素反應飄字、英雄受困 `ROOTED` 飄字與 HUD `ROOTED`／`SLOWED`。規格與凍結驗收見 `docs/V061_FEEDBACK_PLAN.md`；結果見驗收指南 §15。
- 驗收指南記錄：`verify.sh` 195 通過、1 略過；Unity EditMode 0 紅、PlayMode 99 全綠；突變 114／114 CAUGHT。這是 2026-09-21 的既有紀錄；2026-09-23 的重新驗證見下節。
- `origin/gh-pages` 為 `1b69ad1`（2026-09-26 17:23:52 +0800），部署 v0.9.1（v0.9.0 的 19 塊棋盤＋包夾斷能＋劣勢狂怒，加低幀率點地走過頭修正），建置來源 `f90cb13`（分支 `v090-encircle`，2026-09-27 以 fast-forward 併入 main）；線上首頁顯示 `v0.9.1 · build 2026-09-26 07:05 UTC · f90cb13`。驗收指南 §19、§20 記有實測結果。可回退送達點：v0.9.0 的 `f22519c`（來源 `5b7bab3`）、v0.8.0 的 `d232ee6`（來源 `96e143f`）。

## 未決事項與下一步

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
