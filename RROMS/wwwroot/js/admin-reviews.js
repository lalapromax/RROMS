/* Guest reviews (admin): the filter bar refreshes the list in place instead of loading a new page.
   Without this script the same bar still works: every control is a normal GET form. */
(function () {
  'use strict';
  var form = document.querySelector('form.filter');
  var box = document.getElementById('revResults');
  if (!form || !box || !window.fetch || !window.DOMParser || !window.URLSearchParams) return;

  var start = form.elements['StartDate'], end = form.elements['EndDate'];
  var firstDates = (start ? start.value : '') + '|' + (end ? end.value : '');
  var latest = 0;

  function lowOn() { var b = document.getElementById('revLow'); return !!b && b.getAttribute('aria-pressed') === 'true'; }
  function datesChanged() { return ((start ? start.value : '') + '|' + (end ? end.value : '')) !== firstDates; }

  function queryFrom(overrides) {
    var p = new URLSearchParams();
    new FormData(form).forEach(function (v, k) { if (typeof v === 'string' && v !== '' && k !== 'Low') p.set(k, v); });
    if (lowOn() && !(overrides && overrides.Low === null)) { p.set('Low', 'true'); p.delete('Stars'); }   // "Below 3 stars" stays on while other filters change
    Object.keys(overrides || {}).forEach(function (k) { if (overrides[k] === null) p.delete(k); else p.set(k, overrides[k]); });
    return p.toString();
  }

  function show(query) {
    var url = location.pathname + (query ? '?' + query : '');
    var mine = ++latest;
    box.setAttribute('aria-busy', 'true');
    fetch(url, { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error('bad'); return r.text(); })
      .then(function (html) {
        if (mine !== latest) return;
        var doc = new DOMParser().parseFromString(html, 'text/html');
        var fresh = doc.getElementById('revResults');
        var freshBtn = doc.getElementById('revLow');
        if (!fresh) throw new Error('missing');
        box.innerHTML = fresh.innerHTML;
        var btn = document.getElementById('revLow');
        if (btn && freshBtn) {
          btn.className = freshBtn.className;
          btn.value = freshBtn.value;
          btn.setAttribute('aria-pressed', freshBtn.getAttribute('aria-pressed'));
        }
        history.replaceState(null, '', url);
        box.removeAttribute('aria-busy');
      })
      .catch(function () { window.location.href = url; });   // anything unexpected: do it the plain way
  }

  // "Below 3 stars" switches on and off
  var low = document.getElementById('revLow');
  if (low) low.addEventListener('click', function (e) {
    if (datesChanged()) return;
    e.preventDefault();
    var turnOn = low.getAttribute('aria-pressed') !== 'true';
    show(queryFrom(turnOn ? { Low: 'true', Stars: null, PageNo: null } : { Low: null, PageNo: null }));
  });

  // Choosing a star count, issue, reply state or "only with comments" refreshes the list. A single star choice replaces "Below 3 stars".
  form.addEventListener('change', function (e) {
    var el = e.target;
    if (!el || !el.hasAttribute || !el.hasAttribute('data-autosubmit')) return;
    e.stopPropagation();                       // keeps the site-wide auto-submit from reloading the page
    if (datesChanged()) { form.submit(); return; }
    // picking a star count means "exactly that many", so it switches "Below 3 stars" off
    show(queryFrom(el.name === 'Stars' && el.value !== '0' ? { Low: null, PageNo: null } : { PageNo: null }));
  });

  // Apply (dates) is a normal submit
  form.addEventListener('submit', function (e) {
    if (datesChanged()) return;
    e.preventDefault();
    show(queryFrom({ PageNo: null }));
  });

  // "Show all reviews" in the note clears the button without leaving the page
  box.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('[data-rev-clear]');
    if (!a || e.metaKey || e.ctrlKey || e.shiftKey) return;
    e.preventDefault();
    var q = a.search ? a.search.slice(1) : '';
    show(q);
  });
})();
