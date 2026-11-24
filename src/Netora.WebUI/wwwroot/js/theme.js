// Runs in <head> so the saved theme is applied before first paint.
// Browser-local fallback; the per-user preference is added with authentication.
(function () {
  var t = 'light';
  try { if (localStorage.getItem('netora-theme') === 'dark') t = 'dark'; } catch (e) { }
  document.documentElement.setAttribute('data-bs-theme', t);
})();
