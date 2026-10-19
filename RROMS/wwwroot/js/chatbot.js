// Floating chatbot widget wired to POST /api/chatbot/ask.
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        var toggle = document.getElementById('rroms-chat-toggle');
        var panel = document.getElementById('rroms-chat-panel');
        var messages = document.getElementById('rroms-chat-messages');
        var form = document.getElementById('rroms-chat-form');
        var input = document.getElementById('rroms-chat-input');

        if (!toggle || !panel || !form) return;

        toggle.addEventListener('click', function () {
            panel.classList.toggle('open');
            if (panel.classList.contains('open')) input.focus();
        });

        function addBubble(text, who) {
            var bubble = document.createElement('div');
            bubble.className = 'chat-bubble ' + who;
            bubble.textContent = text;
            messages.appendChild(bubble);
            messages.scrollTop = messages.scrollHeight;
        }

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var text = input.value.trim();
            if (!text) return;
            addBubble(text, 'user');
            input.value = '';

            fetch('/api/chatbot/ask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ message: text })
            })
                .then(function (res) { return res.json(); })
                .then(function (data) { addBubble(data.answer || "Sorry, I couldn't process that.", 'bot'); })
                .catch(function () { addBubble("I'm having trouble connecting right now. Please try again shortly.", 'bot'); });
        });
    });
})();
