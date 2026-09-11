// ── البث اللحظي (SignalR) — تحديث الشاشات فور وقوع أي حدث ──
(function () {
    if (!window.signalR) return;

    var conn = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/live")
        .withAutomaticReconnect()
        .build();

    function ensureToastHost() {
        var c = document.getElementById('liveToasts');
        if (!c) {
            c = document.createElement('div');
            c.id = 'liveToasts';
            c.style.cssText = 'position:fixed;top:16px;left:16px;z-index:3000;display:flex;flex-direction:column;gap:8px;max-width:340px';
            document.body.appendChild(c);
        }
        return c;
    }

    function toast(title, body) {
        var c = ensureToastHost();
        var t = document.createElement('div');
        t.style.cssText = 'background:linear-gradient(135deg,#166534,#15803D);color:#fff;padding:12px 16px;border-radius:12px;box-shadow:0 10px 28px rgba(0,0,0,.22);font-size:14px;transform:translateY(-8px);opacity:0;transition:opacity .3s,transform .3s';
        t.innerHTML = '<b>🔔 ' + (title || 'تحديث مباشر') + '</b>' + (body ? '<div style="opacity:.92;font-size:13px;margin-top:3px">' + body + '</div>' : '');
        c.appendChild(t);
        requestAnimationFrame(function () { t.style.opacity = '1'; t.style.transform = 'none'; });
        setTimeout(function () { t.style.opacity = '0'; setTimeout(function () { t.remove(); }, 400); }, 6000);
    }

    conn.on('notify', function (n) {
        var dot = document.getElementById('notifDot');
        if (dot) {
            var v = (parseInt(dot.getAttribute('data-count'), 10) || 0) + 1;
            dot.setAttribute('data-count', v);
            dot.textContent = v;
            dot.style.display = '';
        }
        toast(n && n.title, n && n.body);
    });

    // فئات هذه الشاشة تُشتق من مسارها (يمكن تجاوزها بضبط window.liveCategories)
    function pageCategories() {
        if (window.liveCategories) return window.liveCategories;
        var p = location.pathname.toLowerCase(), c = [];
        if (p.indexOf('/auctions') >= 0) c.push('auctions');
        if (p.indexOf('/tenders') >= 0) c.push('tenders');
        if (p.indexOf('/contracts') >= 0) c.push('contracts');
        if (p.indexOf('/logistics') >= 0 || p.indexOf('/shipping') >= 0) { c.push('logistics', 'contracts'); }
        if (p.indexOf('/company') >= 0) { c.push('auctions', 'contracts'); }
        if (p.indexOf('/admin') >= 0) { c.push('auctions', 'contracts', 'tenders', 'logistics', 'general'); }
        if (p === '/' || p.indexOf('/home') >= 0) { c.push('auctions', 'contracts', 'tenders', 'general'); }
        return c;
    }

    var reloadTimer = null;
    conn.on('dataChanged', function (category) {
        var cats = pageCategories();
        if (cats.indexOf(category) === -1 && cats.indexOf('*') === -1) return;
        // إعادة تحميل ناعمة (مؤجّلة قليلاً لدمج الأحداث المتتابعة)
        clearTimeout(reloadTimer);
        reloadTimer = setTimeout(function () { location.reload(); }, 700);
    });

    conn.start().catch(function () { /* يعيد المحاولة تلقائياً */ });
})();
