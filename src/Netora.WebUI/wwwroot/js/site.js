(function () {
  var root = document.documentElement;
  var buttons = document.querySelectorAll('[data-theme]');

  function sync() {
    var t = root.getAttribute('data-bs-theme');
    buttons.forEach(function (b) {
      var selected = b.dataset.theme === t;
      b.classList.toggle('selected', selected);
      b.setAttribute('aria-pressed', String(selected));
    });
  }

  buttons.forEach(function (b) {
    b.addEventListener('click', function () {
      root.setAttribute('data-bs-theme', b.dataset.theme);
      try { localStorage.setItem('netora-theme', b.dataset.theme); } catch (e) { }
      sync();
      // Signed in: also save it to the account, so it follows the user to other browsers.
      var form = document.getElementById('themeForm');
      if (form && window.fetch) {
        var data = new FormData(form);
        data.append('theme', b.dataset.theme);
        fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin' }).catch(function () { });
      }
    });
  });
  sync();

  var toggle = document.getElementById('sidebarToggle');
  var sidebar = document.getElementById('sidebar');
  if (toggle && sidebar) {
    var app = sidebar.closest('.app');
    var compact = window.matchMedia('(max-width: 800px)');

    function syncSidebar() {
      var expanded = compact.matches ? sidebar.classList.contains('open') : !app.classList.contains('sidebar-collapsed');
      toggle.setAttribute('aria-expanded', String(expanded));
    }

    function closeSidebar() {
      sidebar.classList.remove('open');
      syncSidebar();
      toggle.focus();
    }

    toggle.addEventListener('click', function () {
      if (compact.matches) {
        if (sidebar.classList.toggle('open')) sidebar.querySelector('.nav-item')?.focus();
      } else {
        app.classList.toggle('sidebar-collapsed');
      }
      syncSidebar();
    });
    app.querySelector('.sidebar-backdrop')?.addEventListener('click', closeSidebar);
    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && compact.matches && sidebar.classList.contains('open')) {
        closeSidebar();
      }
    });
    compact.addEventListener('change', syncSidebar);
    syncSidebar();
  }
  // <select data-autosubmit> submits its form on change (used for "Rows per page").
  document.querySelectorAll('select[data-autosubmit]').forEach(function (s) {
    s.addEventListener('change', function () { if (s.form) s.form.submit(); });
  });
})();

// Forms with data-confirm ask before submitting (used for deletes).
document.addEventListener('submit', function (e) {
  var form = e.target;
  if (form instanceof HTMLFormElement && form.dataset.confirm && !window.confirm(form.dataset.confirm)) e.preventDefault();
});
