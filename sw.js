// 自我解除用的 service worker（kill switch）。
//
// 這個網址以前部署過另一個 PWA，它的 service worker（workbox precache）還留在玩家的瀏覽器裡，
// 會一直拿快取裡的舊站回應，新部署的內容永遠顯示不出來。瀏覽器每次造訪都會重新抓 /sw.js 檢查更新：
// 抓到這一支之後，它會立刻接手、刪掉「本 scope」的快取、解除註冊，並讓已開啟的分頁重新載入。
// 只處理快取名稱含本 scope 的項目——同一個網域下其他遊戲（各自的 scope）的快取不受影響。
// 本站不需要 service worker；這支檔案必須一直留著，直到確定沒有人的瀏覽器還帶著舊的註冊為止。
self.addEventListener('install', function () {
  self.skipWaiting();
});

self.addEventListener('activate', function (event) {
  event.waitUntil((async function () {
    var scope = self.registration.scope;
    var keys = await caches.keys();
    await Promise.all(keys
      .filter(function (key) { return key.indexOf(scope) !== -1; })
      .map(function (key) { return caches.delete(key); }));

    await self.registration.unregister();

    var windows = await self.clients.matchAll({ type: 'window' });
    windows.forEach(function (client) { client.navigate(client.url); });
  })());
});
