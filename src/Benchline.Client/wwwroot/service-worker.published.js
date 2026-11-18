self.importScripts("./service-worker-assets.js");

const prefix = "benchline-shell-";
const name = prefix + self.assetsManifest.version;
const scope = self.registration.scope;
const urls = new Set(self.assetsManifest.assets
  .filter(entry => !/\.(map|pdb|br|gz)$/.test(entry.url) && !entry.url.startsWith("service-worker"))
  .map(entry => new URL(entry.url, scope).href));
const isDeferred = url => new URL(url).pathname.includes("/Benchline.Showcase.");
const index = new URL("index.html", scope).href;

self.addEventListener("install", event => {
  event.waitUntil(caches.open(name).then(cache => cache.addAll([...urls].filter(url => !isDeferred(url)))));
});

self.addEventListener("activate", event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys
    .filter(key => key.startsWith(prefix) && key !== name)
    .map(key => caches.delete(key)))).then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== "GET" || url.origin !== self.location.origin
      || /^\/(api|live|health|openapi)(\/|$)/.test(url.pathname)) return;

  if (request.mode === "navigate") {
    event.respondWith(fetch(request).catch(async () => (await caches.open(name)).match(index)));
  } else if (urls.has(url.href)) {
    event.respondWith(caches.open(name).then(async cache => {
      const cached = await cache.match(request);
      if (cached) return cached;
      const response = await fetch(request);
      if (response.ok && isDeferred(url.href)) await cache.put(request, response.clone());
      return response;
    }));
  }
});
