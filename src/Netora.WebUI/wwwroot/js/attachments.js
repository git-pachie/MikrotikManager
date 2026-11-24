// Customer Details, Attachments tab: one-click upload and the image viewer.

// Upload: the "Upload File" button opens the hidden file picker; choosing a file uploads it straight away.
// The size check here only avoids sending a file the server would refuse; the server checks again.
(function () {
  var form = document.getElementById('attachmentForm');
  var input = document.getElementById('attachmentFile');
  var pick = document.getElementById('attachmentPick');
  if (!form || !input || !pick) return;

  pick.addEventListener('click', function () { input.click(); });

  input.addEventListener('change', function () {
    var file = input.files && input.files[0];
    if (!file) return;
    var max = Number(input.dataset.maxBytes);
    if (max && file.size > max) {
      window.alert('"' + file.name + '" is larger than the ' + input.dataset.maxLabel + ' limit and was not uploaded.');
      input.value = '';
      return;
    }
    pick.disabled = true;
    pick.querySelector('span').textContent = 'Uploading…';
    form.submit();
  });
})();

// Image viewer: clicking an image file opens it in a dialog with previous/next, thumbnails and keyboard control.
// PDFs keep their normal link (download).
(function () {
  var dialog = document.getElementById('imageViewer');
  var links = Array.prototype.slice.call(document.querySelectorAll('a[data-gallery]'));
  if (!dialog || links.length === 0) return;

  var img = document.getElementById('imageViewerImg');
  var title = document.getElementById('imageViewerTitle');
  var count = document.getElementById('imageViewerCount');
  var download = document.getElementById('imageViewerDownload');
  var error = document.getElementById('imageViewerError');
  var thumbs = document.getElementById('imageViewerThumbs');
  var prev = document.getElementById('imageViewerPrev');
  var next = document.getElementById('imageViewerNext');
  var items = links.map(function (a) { return { src: a.getAttribute('href'), name: a.dataset.name }; });
  var current = 0;
  var opener = null;

  // Thumbnails load only when the viewer first opens, not with the page.
  var thumbButtons = [];
  function buildThumbs() {
    if (thumbButtons.length) return;
    items.forEach(function (item, i) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'viewer-thumb';
      b.setAttribute('role', 'listitem');
      b.setAttribute('aria-label', 'Show ' + item.name);
      var t = document.createElement('img');
      t.src = item.src;
      t.alt = '';
      t.loading = 'lazy';
      b.appendChild(t);
      b.addEventListener('click', function () { show(i); });
      thumbs.appendChild(b);
      thumbButtons.push(b);
    });
  }

  function show(i) {
    current = (i + items.length) % items.length;
    var item = items[current];
    error.hidden = true;
    img.hidden = false;
    img.src = item.src;
    img.alt = item.name;
    title.textContent = item.name;
    count.textContent = items.length > 1 ? (current + 1) + ' of ' + items.length : '';
    download.href = item.src;
    thumbButtons.forEach(function (b, n) {
      b.classList.toggle('active', n === current);
      b.setAttribute('aria-current', n === current ? 'true' : 'false');
    });
    if (thumbButtons[current]) thumbButtons[current].scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }

  img.addEventListener('error', function () { img.hidden = true; error.hidden = false; });

  var single = items.length < 2;
  prev.hidden = next.hidden = thumbs.hidden = single;
  prev.addEventListener('click', function () { show(current - 1); });
  next.addEventListener('click', function () { show(current + 1); });
  document.getElementById('imageViewerClose').addEventListener('click', function () { dialog.close(); });

  dialog.addEventListener('keydown', function (e) {
    if (single) return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); show(current - 1); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); show(current + 1); }
  });
  // Clicking the dark backdrop (outside the panel) closes the viewer.
  dialog.addEventListener('click', function (e) { if (e.target === dialog) dialog.close(); });
  dialog.addEventListener('close', function () {
    img.removeAttribute('src');
    if (opener) opener.focus();
  });

  links.forEach(function (a, i) {
    a.addEventListener('click', function (e) {
      if (e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1) return;   // let "open in new tab" work
      e.preventDefault();
      opener = a;
      buildThumbs();
      show(i);
      dialog.showModal();
    });
  });
})();
