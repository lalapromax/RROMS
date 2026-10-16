// Rewards page, phone layout: the tier cards swipe sideways. Open on the card for your own tier and show a row of dots.
(function () {
    'use strict';
    var grid = document.querySelector('[data-tier-grid]');
    if (!grid) return;
    var cards = Array.prototype.slice.call(grid.children);
    if (cards.length < 2) return;
    var mq = window.matchMedia('(max-width: 1000px)');

    var pager = document.createElement('div');
    pager.className = 'pager';
    pager.setAttribute('aria-hidden', 'true');
    cards.forEach(function () { pager.appendChild(document.createElement('i')); });
    grid.parentNode.insertBefore(pager, grid.nextSibling);

    function current() {
        var best = 0, bestDist = Infinity, mid = grid.scrollLeft + grid.clientWidth / 2;
        cards.forEach(function (c, i) {
            var d = Math.abs(c.offsetLeft + c.offsetWidth / 2 - mid);
            if (d < bestDist) { bestDist = d; best = i; }
        });
        return best;
    }
    function mark() {
        var at = current();
        Array.prototype.forEach.call(pager.children, function (dot, i) { dot.className = i === at ? 'on' : ''; });
    }
    grid.addEventListener('scroll', function () { window.requestAnimationFrame(mark); }, { passive: true });

    function openOnHere() {
        if (!mq.matches) return;
        var here = grid.querySelector('[data-here]');
        if (here) grid.scrollLeft = here.offsetLeft - (grid.clientWidth - here.offsetWidth) / 2;
        mark();
    }
    openOnHere();
    if (mq.addEventListener) mq.addEventListener('change', openOnHere);
})();
