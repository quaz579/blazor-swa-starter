// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', event => {
    if (new URL(event.request.url).pathname === '/version.json') {
        event.respondWith(fetch(event.request, { cache: 'no-store' }));
    }
});
