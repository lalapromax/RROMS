// Google Sign-In with Google Identity Services (GIS).
// The callback hands us a signed ID token (a JWT). We never decode or trust it here - it is
// POSTed to a server page handler, which verifies Google's signature, audience, expiry and
// nonce before signing anyone in (Services/GoogleAuthService.cs).
(function () {
    'use strict';

    function showError(message) {
        var box = document.getElementById('googleSignInError');
        if (box) box.textContent = message || '';
    }

    function antiforgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function init() {
        var host = document.getElementById('googleSignIn');
        if (!host) return;

        function ready() {
            return window.google && window.google.accounts && window.google.accounts.id;
        }

        var attempts = 0;
        (function waitForGoogle() {
            if (ready()) return start();
            if (++attempts > 50) { showError('Google sign-in could not load. Check your connection and refresh.'); return; }
            setTimeout(waitForGoogle, 100);
        })();

        function start() {
            window.google.accounts.id.initialize({
                client_id: host.getAttribute('data-client-id'),
                nonce: host.getAttribute('data-nonce'),
                callback: onCredential,
                ux_mode: 'popup'
            });
            window.google.accounts.id.renderButton(host, {
                theme: 'outline',
                size: 'large',
                text: host.getAttribute('data-text') || 'continue_with',
                shape: 'rectangular',
                width: Math.min(host.parentElement.clientWidth || 320, 400)
            });
        }

        function onCredential(response) {
            showError('');
            var body = new URLSearchParams();
            body.append('credential', response.credential);
            body.append('returnUrl', host.getAttribute('data-return-url') || '');
            body.append('__RequestVerificationToken', antiforgeryToken());

            fetch(host.getAttribute('data-post-url'), {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: body
            })
                .then(function (r) { return r.json(); })
                .then(function (result) {
                    if (result.success) {
                        window.location.href = result.redirectUrl || '/';
                    } else {
                        showError(result.message || 'Google sign-in failed. Please try again.');
                    }
                })
                .catch(function () { showError('Could not reach the server. Please try again.'); });
        }
    }

    document.addEventListener('DOMContentLoaded', init);
})();
