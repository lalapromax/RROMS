// The menu page: the à la carte filters and the running total on the open set menu. Without JavaScript every dish is
// shown and the total is the price of the choices ticked when the page loaded.
(function () {
    'use strict';

    // ---------------------------------------------------------------- à la carte filters
    var chips = document.querySelector('[data-chips]');
    if (chips) {
        var dishes = Array.prototype.slice.call(document.querySelectorAll('[data-dish]'));
        var empty = document.querySelector('[data-carte-empty]');
        var count = document.querySelector('[data-carte-count]');
        var note = document.querySelector('[data-suits-note]');
        var state = { cat: null, veg: false, suits: false };

        var pressed = function (el) { return el && el.getAttribute('aria-pressed') === 'true'; };
        var suitsChip = chips.querySelector('[data-filter="suits"]');
        state.suits = pressed(suitsChip);

        function apply() {
            var shown = 0;
            dishes.forEach(function (d) {
                var ok = true;
                if (state.cat && d.getAttribute('data-cat') !== state.cat) ok = false;
                if (state.veg && d.getAttribute('data-veg') !== '1') ok = false;
                if (state.suits && d.getAttribute('data-suits') !== '1') ok = false;
                d.hidden = !ok;
                if (ok) shown++;
            });
            // the chef's picks row disappears when none of its dishes are showing
            var picks = document.querySelector('.picks');
            if (picks) picks.hidden = !picks.querySelector('[data-dish]:not([hidden])');
            if (empty) empty.hidden = shown > 0;
            if (note) note.hidden = !state.suits;
            if (count) count.textContent = shown + (shown === 1 ? ' dish shown' : ' dishes shown');
        }

        chips.addEventListener('click', function (e) {
            var chip = e.target.closest('button[data-filter]');
            if (!chip) return;
            var kind = chip.getAttribute('data-filter');
            if (kind === 'all' || kind === 'cat') {
                state.cat = kind === 'cat' ? chip.getAttribute('data-cat') : null;
                Array.prototype.forEach.call(chips.querySelectorAll('[data-filter="all"],[data-filter="cat"]'), function (c) {
                    c.setAttribute('aria-pressed', c === chip ? 'true' : 'false');
                });
            } else if (kind === 'veg') {
                state.veg = !state.veg;
                chip.setAttribute('aria-pressed', state.veg ? 'true' : 'false');
            } else if (kind === 'suits') {
                state.suits = !state.suits;
                chip.setAttribute('aria-pressed', state.suits ? 'true' : 'false');
            }
            apply();
        });
        apply();
    }

    // ---------------------------------------------------------------- add to the cart without leaving the spot
    // A dish, a glass or bottle from the cellar, or a set menu goes into the cart in the background: the button says
    // "Added", the cart count in the bar goes up and a note with a link to the cart appears. The page does not reload,
    // so the guest is not thrown back to the top or down to an anchor. Without scripts the forms post as before.
    var toast = null, toastTimer = 0;
    function say(message, ok, cartUrl) {
        if (!toast) {
            toast = document.createElement('div');
            toast.className = 'add-toast';
            toast.setAttribute('role', 'status');
            toast.setAttribute('aria-live', 'polite');
            document.body.appendChild(toast);
        }
        toast.className = 'add-toast' + (ok ? '' : ' bad');
        toast.textContent = '';
        var text = document.createElement('span');
        text.textContent = message;
        toast.appendChild(text);
        if (ok && cartUrl) {
            var link = document.createElement('a');
            link.href = cartUrl;
            link.textContent = 'View cart';
            toast.appendChild(link);
        }
        toast.classList.add('show');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { toast.classList.remove('show'); }, ok ? 4000 : 6000);
    }
    function setCartCount(n) {
        document.querySelectorAll('.ib-cart').forEach(function (cart) {
            var badge = cart.querySelector('.ib-count');
            if (n > 0 && !badge) { badge = document.createElement('i'); badge.className = 'ib-count'; cart.appendChild(badge); }
            if (badge) { if (n > 0) badge.textContent = n > 9 ? '9+' : String(n); else badge.remove(); }
            cart.setAttribute('aria-label', n > 0 ? 'Your cart, ' + n + (n === 1 ? ' item' : ' items') : 'Your cart');
        });
    }
    function added(button) {
        if (!button) return;
        var label = button.getAttribute('data-label') || button.textContent;
        button.setAttribute('data-label', label);
        button.style.minWidth = button.offsetWidth + 'px';   // the word "Added" must not make the row jump
        button.classList.add('is-added');
        button.textContent = 'Added';
        clearTimeout(button._addedTimer);
        button._addedTimer = setTimeout(function () {
            button.classList.remove('is-added');
            button.textContent = button.getAttribute('data-label');
            button.style.minWidth = '';
        }, 1800);
    }

    document.addEventListener('submit', function (e) {
        var addForm = e.target.closest && e.target.closest('form[data-quick-add]');
        if (!addForm || !window.fetch || !window.FormData) return;
        e.preventDefault();
        var button = e.submitter && addForm.contains(e.submitter) ? e.submitter : addForm.querySelector('button[type=submit],button:not([type])');
        var data = new FormData(addForm);
        if (button && button.name) data.append(button.name, button.value);
        if (button) button.disabled = true;
        fetch(addForm.action, { method: 'POST', body: data, credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) {
                return r.json().catch(function () { throw new Error('not json'); });
            })
            .then(function (res) {
                say(res.message || (res.ok ? 'Added to your order.' : 'That could not be added.'), !!res.ok, res.cartUrl);
                if (res.ok) { setCartCount(res.cartCount || 0); added(button); }
            })
            .catch(function () { say('We could not add that just now. Please try again.', false); })
            .then(function () { if (button) button.disabled = false; });
    });

    // ---------------------------------------------------------------- set menu total
    var form = document.querySelector('[data-setform]');
    if (form) {
        var base = parseFloat(form.getAttribute('data-base')) || 0;
        var totalEl = form.querySelector('[data-total]');
        var noteEl = form.querySelector('[data-total-note]');
        var rm = function (n) {
            var whole = Math.abs(n - Math.round(n)) < 0.005;
            return 'RM ' + n.toLocaleString('en-US', { minimumFractionDigits: whole ? 0 : 2, maximumFractionDigits: whole ? 0 : 2 });
        };
        var list = function (names) { return names.length < 2 ? names.join('') : names.slice(0, -1).join(', ') + ' and ' + names[names.length - 1]; };
        var update = function () {
            var supp = 0, names = [];
            Array.prototype.forEach.call(form.querySelectorAll('input[data-supp]:checked'), function (i) {
                var s = parseFloat(i.getAttribute('data-supp')) || 0;
                if (s > 0) { supp += s; names.push(i.getAttribute('data-name')); }
            });
            totalEl.textContent = rm(base + supp);
            noteEl.textContent = supp > 0 ? 'per guest: set price ' + rm(base) + ', plus ' + rm(supp) + ' for ' + list(names) : 'per guest';
        };
        form.addEventListener('change', update);
    }
})();
