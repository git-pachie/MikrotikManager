// Tab switching for Customer Details. Keyboard: Left/Right/Home/End move between tabs.
(function () {
  var tabs = Array.prototype.slice.call(document.querySelectorAll('.detail-tab'));
  var panes = document.querySelectorAll('.detail-tab-pane');

  function select(tab, focus) {
    tabs.forEach(function (t) {
      var on = t === tab;
      t.classList.toggle('active', on);
      t.setAttribute('aria-selected', String(on));
      t.tabIndex = on ? 0 : -1;
    });
    panes.forEach(function (p) { p.hidden = p.dataset.pane !== tab.dataset.tab; });
    if (focus) tab.focus();
  }

  // Open the tab named in the URL hash (for example after an upload redirects to #attachments).
  var fromHash = tabs.filter(function (t) { return '#' + t.dataset.tab === location.hash; })[0];
  if (fromHash) select(fromHash, false);

  tabs.forEach(function (tab, i) {
    tab.addEventListener('click', function () { select(tab, false); });
    tab.addEventListener('keydown', function (e) {
      var next = null;
      if (e.key === 'ArrowRight') next = tabs[(i + 1) % tabs.length];
      else if (e.key === 'ArrowLeft') next = tabs[(i - 1 + tabs.length) % tabs.length];
      else if (e.key === 'Home') next = tabs[0];
      else if (e.key === 'End') next = tabs[tabs.length - 1];
      if (next) { e.preventDefault(); select(next, true); }
    });
  });
})();
