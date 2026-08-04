// ── شبكة أمان: ارفع ستارة الانتقال حتى لو تعذّر تحميل GSAP ───
(function () {
    setTimeout(function () {
        var pf = document.getElementById('pageFade');
        if (pf && typeof gsap === 'undefined') pf.style.display = 'none';
    }, 1200);
})();

// ── تبديل الشريط الجانبي على الشاشات الصغيرة ─────────────────
(function () {
    var toggle = document.getElementById('navToggle');
    var sidebar = document.getElementById('sidebar');
    if (!toggle || !sidebar) return;

    var scrim = null;

    function close() {
        sidebar.classList.remove('open');
        toggle.setAttribute('aria-expanded', 'false');
        if (scrim) { scrim.remove(); scrim = null; }
    }

    function open() {
        sidebar.classList.add('open');
        toggle.setAttribute('aria-expanded', 'true');
        scrim = document.createElement('div');
        scrim.className = 'scrim';
        scrim.addEventListener('click', close);
        document.body.appendChild(scrim);
    }

    toggle.addEventListener('click', function () {
        if (sidebar.classList.contains('open')) { close(); } else { open(); }
    });

    // إغلاق بمفتاح Escape — سلوك متوقع للوحة المفاتيح
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && sidebar.classList.contains('open')) {
            close();
            toggle.focus();
        }
    });

    // إغلاق عند العودة لعرض سطح المكتب
    window.addEventListener('resize', function () {
        if (window.innerWidth >= 992) close();
    });
})();

// ── إخفاء التنبيهات تلقائياً بعد 6 ثوانٍ ─────────────────────
(function () {
    var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    document.querySelectorAll('.alert-dismissible').forEach(function (el) {
        setTimeout(function () {
            if (!document.body.contains(el)) return;
            if (reduce) { el.remove(); return; }
            el.style.transition = 'opacity .3s ease';
            el.style.opacity = '0';
            setTimeout(function () { el.remove(); }, 320);
        }, 6000);
    });
})();
