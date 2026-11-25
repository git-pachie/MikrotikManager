// Runs in <head> so the theme is applied before first paint.
// The signed-in user's saved preference (data-user-theme, set by the server) wins; otherwise the browser's last choice.
(function () {
  var root = document.documentElement;
  var saved = root.getAttribute('data-user-theme');
  var t = 'light';
  if (saved === 'dark' || saved === 'light') {
    t = saved;
    try { localStorage.setItem('netora-theme', t); } catch (e) { }
  } else {
    try { if (localStorage.getItem('netora-theme') === 'dark') t = 'dark'; } catch (e) { }
  }
  root.setAttribute('data-bs-theme', t);
})();
