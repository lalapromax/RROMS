// Small charts for the administrator's pages, drawn as SVG at the width they are shown at, so labels stay readable on a phone.
// No library. The page puts the numbers in <script type="application/json"> next to a <figure data-chart> and also prints them
// as a table ("View as table"), which is the version for screen readers and for anyone without scripts.
//
//   <div data-chart-wrap>
//     <figure class="chart" data-chart="line|columns|spark" data-src="revData"><div class="chart-plot"></div><div class="ch-tip" hidden></div></figure>
//     <script type="application/json" id="revData">{ "fmt":"money", "series":"Revenue", "label":"...", "points":[{"x":"19 Sep","t":"Sat 19 Sep","v":6120,"n":"14 orders"}] }</script>
//     <div class="chart-table">...table...</div>
//   </div>
(function () {
    'use strict';
    var NS = 'http://www.w3.org/2000/svg';
    var money = new Intl.NumberFormat('en-MY', { maximumFractionDigits: 0 });

    function el(name, attrs, parent) {
        var n = document.createElementNS(NS, name);
        for (var k in attrs) if (Object.prototype.hasOwnProperty.call(attrs, k)) n.setAttribute(k, attrs[k]);
        if (parent) parent.appendChild(n);
        return n;
    }

    function fmtValue(d, v) {
        if (d.fmt === 'money') return (d.cur || 'RM') + ' ' + money.format(v);
        if (d.fmt === 'rating') return v.toFixed(1);
        return money.format(v);
    }
    function fmtTick(d, v) {
        if (d.fmt === 'money' && Math.abs(v) >= 1000) return (v / 1000) + 'K';
        if (d.fmt === 'rating') return v.toFixed(1);
        return String(v);
    }

    // a tidy step (1, 2, 2.5, 5 times a power of ten) giving about four bands
    function niceScale(min, max, want) {
        if (max <= min) max = min + 1;
        var raw = (max - min) / (want || 4), pow = Math.pow(10, Math.floor(Math.log10(raw))), step = pow;
        [1, 2, 2.5, 5, 10].some(function (m) { step = m * pow; return step >= raw; });
        var lo = Math.floor(min / step) * step, hi = Math.ceil(max / step) * step;
        var ticks = [];
        for (var t = lo; t <= hi + step / 1000; t += step) ticks.push(Math.round(t * 1000) / 1000);
        return { lo: lo, hi: hi, ticks: ticks };
    }

    function draw(fig) {
        var data;
        try { data = JSON.parse(document.getElementById(fig.getAttribute('data-src')).textContent); } catch (e) { return; }
        var plot = fig.querySelector('.chart-plot');
        var tip = fig.querySelector('.ch-tip');
        var pts = data.points || [];
        var kind = fig.getAttribute('data-chart');
        var W = Math.max(200, Math.floor(plot.clientWidth || fig.clientWidth || 320));
        plot.textContent = '';
        if (!pts.length) { plot.innerHTML = '<p class="chart-empty">' + (data.empty || 'Nothing to show for this period.') + '</p>'; return; }

        var H = kind === 'spark' ? (data.h || 96) : (data.h || (W < 520 ? 230 : 300));
        var svg = el('svg', { width: W, height: H, viewBox: '0 0 ' + W + ' ' + H, role: 'img', 'aria-label': data.label || '' }, plot);
        svg.setAttribute('class', 'ch-svg ch-k-' + kind);
        var vals = pts.map(function (p) { return p.v; });

        if (kind === 'spark') {
            var mn = Math.min.apply(null, vals), mx = Math.max.apply(null, vals), span = (mx - mn) || 1;
            var padX = 8, padY = 10;
            var sx = function (i) { return padX + (pts.length === 1 ? (W - 2 * padX) / 2 : i * (W - 2 * padX) / (pts.length - 1)); };
            var sy = function (v) { return H - padY - (v - mn) / span * (H - 2 * padY); };
            var dPath = pts.map(function (p, i) { return (i ? 'L' : 'M') + sx(i).toFixed(1) + ' ' + sy(p.v).toFixed(1); }).join('');
            el('path', { d: dPath, class: 'ch-line', fill: 'none' }, svg);
            el('circle', { cx: sx(pts.length - 1), cy: sy(vals[vals.length - 1]), r: 4.5, class: 'ch-dot' }, svg);
            return;
        }

        var m = { l: data.fmt === 'money' ? 38 : 30, r: 14, t: kind === 'columns' ? 24 : 16, b: 30 };
        var iw = W - m.l - m.r, ih = H - m.t - m.b;
        var lo = data.yMin != null ? data.yMin : (kind === 'columns' ? 0 : Math.min(0, Math.min.apply(null, vals)));
        var hi = data.yMax != null ? data.yMax : Math.max.apply(null, vals);
        var sc = (data.yMin != null && data.yMax != null) ? (function () {
            var step = (hi - lo) / 4, ts = []; for (var i = 0; i <= 4; i++) ts.push(Math.round((lo + i * step) * 100) / 100);
            return { lo: lo, hi: hi, ticks: ts };
        })() : niceScale(lo, hi, 4);
        var y = function (v) { return m.t + ih - (v - sc.lo) / (sc.hi - sc.lo) * ih; };

        // grid and y labels
        sc.ticks.forEach(function (t) {
            el('line', { x1: m.l, x2: W - m.r, y1: y(t), y2: y(t), class: t === sc.lo ? 'ch-axis' : 'ch-grid' }, svg);
            var tx = el('text', { x: m.l - 8, y: y(t) + 4, 'text-anchor': 'end', class: 'ch-tick' }, svg);
            tx.textContent = fmtTick(data, t);
        });

        var n = pts.length, band = iw / n, xs = [];
        if (kind === 'columns') {
            var bw = Math.min(36, band * 0.62);
            pts.forEach(function (p, i) {
                var cx = m.l + band * (i + 0.5); xs.push(cx);
                var top = y(p.v), base = y(sc.lo), r = Math.min(4, bw / 2, base - top);
                if (p.v > 0) el('path', { d: 'M' + (cx - bw / 2) + ' ' + base + 'V' + (top + r) + 'Q' + (cx - bw / 2) + ' ' + top + ' ' + (cx - bw / 2 + r) + ' ' + top + 'H' + (cx + bw / 2 - r) + 'Q' + (cx + bw / 2) + ' ' + top + ' ' + (cx + bw / 2) + ' ' + (top + r) + 'V' + base + 'Z', class: 'ch-bar' }, svg);
            });
            if (data.markMax !== false) {
                var mi = vals.indexOf(Math.max.apply(null, vals));
                if (vals[mi] > 0) { var lt = el('text', { x: xs[mi], y: y(vals[mi]) - 7, 'text-anchor': 'middle', class: 'ch-val' }, svg); lt.textContent = fmtValue(data, vals[mi]).replace(/^RM /, ''); }
            }
        } else {
            pts.forEach(function (p, i) { xs.push(n === 1 ? m.l + iw / 2 : m.l + i * iw / (n - 1)); });
            var line = pts.map(function (p, i) { return (i ? 'L' : 'M') + xs[i].toFixed(1) + ' ' + y(p.v).toFixed(1); }).join('');
            el('path', { d: line + 'L' + xs[n - 1].toFixed(1) + ' ' + y(sc.lo) + 'L' + xs[0].toFixed(1) + ' ' + y(sc.lo) + 'Z', class: 'ch-area' }, svg);
            el('path', { d: line, class: 'ch-line', fill: 'none' }, svg);
            if (data.markLast !== false) el('circle', { cx: xs[n - 1], cy: y(vals[n - 1]), r: 4.5, class: 'ch-dot' }, svg);
        }

        // x labels: as many as fit
        var maxLabels = Math.max(2, Math.floor(iw / (kind === 'columns' ? 46 : 74)));
        var every = Math.max(1, Math.ceil(n / maxLabels));
        pts.forEach(function (p, i) {
            if (i % every !== 0) return;
            var t = el('text', { x: xs[i], y: H - 8, 'text-anchor': kind === 'line' && i === 0 ? 'start' : 'middle', class: 'ch-tick' }, svg);
            t.textContent = p.x;
        });

        // hover / touch: crosshair, dot and a small card with the exact figure
        var cross = el('line', { y1: m.t, y2: m.t + ih, class: 'ch-cross', visibility: 'hidden' }, svg);
        var hot = el('circle', { r: 5, class: 'ch-dot', visibility: 'hidden' }, svg);
        var hit = el('rect', { x: m.l, y: 0, width: iw, height: H, fill: 'transparent', class: 'ch-hit' }, svg);
        function show(evt) {
            var box = svg.getBoundingClientRect();
            var px = (evt.clientX - box.left) * (W / box.width);
            var i = kind === 'columns' ? Math.floor((px - m.l) / band) : Math.round((px - m.l) / (n === 1 ? 1 : iw / (n - 1)));
            i = Math.max(0, Math.min(n - 1, i));
            var p = pts[i], cx = xs[i], cy = y(p.v);
            cross.setAttribute('x1', cx); cross.setAttribute('x2', cx); cross.setAttribute('visibility', kind === 'line' ? 'visible' : 'hidden');
            hot.setAttribute('cx', cx); hot.setAttribute('cy', cy); hot.setAttribute('visibility', kind === 'line' ? 'visible' : 'hidden');
            tip.innerHTML = '';
            var b = document.createElement('b'); b.textContent = p.t || p.x; tip.appendChild(b);
            var s = document.createElement('span'); s.textContent = (data.series || '') + ' '; var v = document.createElement('strong'); v.textContent = fmtValue(data, p.v); s.appendChild(v); tip.appendChild(s);
            if (p.n) { var nn = document.createElement('span'); nn.textContent = p.n; tip.appendChild(nn); }
            tip.hidden = false;
            var fr = fig.getBoundingClientRect(), pr = plot.getBoundingClientRect();
            var left = (pr.left - fr.left) + cx * (box.width / W) - tip.offsetWidth / 2;
            left = Math.max(0, Math.min(fr.width - tip.offsetWidth, left));
            tip.style.left = left + 'px';
            tip.style.top = Math.max(0, (pr.top - fr.top) + cy * (box.height / H) - tip.offsetHeight - 12) + 'px';
        }
        function hide() { tip.hidden = true; cross.setAttribute('visibility', 'hidden'); hot.setAttribute('visibility', 'hidden'); }
        hit.addEventListener('pointermove', show);
        hit.addEventListener('pointerdown', show);
        hit.addEventListener('pointerleave', hide);
    }

    function init() {
        var figs = Array.prototype.slice.call(document.querySelectorAll('figure[data-chart]'));
        figs.forEach(function (f) {
            draw(f);
            if (window.ResizeObserver) {
                var last = f.clientWidth, raf = 0;
                new ResizeObserver(function () {
                    if (f.clientWidth === last) return; last = f.clientWidth;
                    cancelAnimationFrame(raf); raf = requestAnimationFrame(function () { draw(f); });
                }).observe(f);
            }
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();

    // "View as table" / "View as chart"
    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-table-toggle]');
        if (!b) return;
        var wrap = b.closest('[data-chart-wrap]') || document.getElementById(b.getAttribute('data-table-toggle'));
        if (!wrap) return;
        var on = wrap.classList.toggle('show-table');
        b.setAttribute('aria-expanded', on ? 'true' : 'false');
        b.textContent = on ? 'View as chart' : 'View as table';
        if (!on) Array.prototype.forEach.call(wrap.querySelectorAll('figure[data-chart]'), draw);
    });
})();
