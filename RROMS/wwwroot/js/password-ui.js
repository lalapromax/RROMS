// Sign-up password helper: ticks off the four rules as the guest types and fills the strength bar.
// The server enforces the same rules (ASP.NET Identity options in Program.cs); this only shows progress.
(function () {
    'use strict';
    var input = document.querySelector('[data-password-check]');
    if (!input) return;
    var rules = document.querySelectorAll('[data-pw-rules] [data-rule]');
    var bars = document.querySelectorAll('[data-pw-meter] i');
    var tests = {
        len: function (v) { return v.length >= 8; },
        upper: function (v) { return /[A-Z]/.test(v); },
        digit: function (v) { return /\d/.test(v); },
        symbol: function (v) { return /[^A-Za-z0-9]/.test(v); }
    };
    function update() {
        var v = input.value, met = 0;
        Array.prototype.forEach.call(rules, function (r) {
            var ok = !!(tests[r.getAttribute('data-rule')] && tests[r.getAttribute('data-rule')](v));
            r.classList.toggle('ok', ok);
            if (ok) met++;
        });
        Array.prototype.forEach.call(bars, function (b, i) { b.classList.toggle('on', i < met); });
    }
    input.addEventListener('input', update);
    update();
})();
