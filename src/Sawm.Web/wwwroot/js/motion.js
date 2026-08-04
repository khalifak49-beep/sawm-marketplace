/* ============================================================
   منصة ساوم — محرك الحركة
   GSAP + ScrollTrigger + jQuery
   كل الحركات تحترم prefers-reduced-motion وتُلغى بالكامل عنده.
   ============================================================ */
(function ($) {
    'use strict';

    var REDUCED = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (typeof gsap !== 'undefined' && gsap.registerPlugin && typeof ScrollTrigger !== 'undefined') {
        gsap.registerPlugin(ScrollTrigger);
    }

    // عند تفضيل تقليل الحركة: أظهر كل شيء فوراً واخرج
    if (REDUCED || typeof gsap === 'undefined') {
        $('.reveal, .stat-card, .card, .pillar, .nav-link-s').css({ opacity: 1, transform: 'none' });
        $('#pageFade').remove();
        return;
    }

    /* ── 1) دخول الصفحة ─────────────────────────────────────
       الدخول أبطأ قليلاً من الخروج (توقيت غير متماثل) ليبدو التنقل رشيقاً */
    var enter = gsap.timeline();

    enter.to('#pageFade', { autoAlpha: 0, duration: 0.28, ease: 'power1.out' }, 0);

    // الشريط الجانبي: انزلاق + تتابع الروابط
    if ($('.sidebar').length) {
        enter.from('.sidebar', { xPercent: 12, autoAlpha: 0, duration: 0.4, ease: 'power2.out' }, 0)
             .from('.brand-mark', { scale: 0.6, rotate: -25, autoAlpha: 0, duration: 0.45, ease: 'back.out(2)' }, 0.08)
             .from('.nav-link-s', { x: 18, autoAlpha: 0, duration: 0.3, stagger: 0.035, ease: 'power2.out' }, 0.12);
    }

    enter.from('.topbar', { y: -14, autoAlpha: 0, duration: 0.32, ease: 'power2.out' }, 0.05);

    // المحتوى: تتابع خفيف (0.03s) حسب توصية القوائم الطويلة.
    // نستثني عناصر .reveal لأن لها حركتها الخاصة عند التمرير —
    // تحريك العنصر نفسه بتَينَين متنافسين يتركه أحياناً عند opacity:0
    enter.from('.content > *:not(.reveal), main.container > *:not(.reveal)', {
        y: 14, autoAlpha: 0, duration: 0.34, stagger: 0.045, ease: 'power2.out', clearProps: 'all'
    }, 0.1);

    /* ── 2) بطاقات المؤشرات: ظهور + عدّاد تصاعدي ───────────── */
    gsap.utils.toArray('.stat-card').forEach(function (card, i) {
        gsap.from(card, {
            y: 18, autoAlpha: 0, duration: 0.42, delay: 0.15 + i * 0.06,
            ease: 'power2.out', clearProps: 'all'
        });

        var $v = $(card).find('.stat-value');
        var raw = $v.text().trim();
        var num = parseFloat(raw.replace(/,/g, ''));
        if (!$v.length || isNaN(num) || num === 0) return;

        var decimals = (raw.split('.')[1] || '').length;
        var suffix = raw.replace(/[\d.,]/g, '');
        var obj = { n: 0 };
        gsap.to(obj, {
            n: num, duration: 1.1, delay: 0.25 + i * 0.06, ease: 'power2.out',
            onUpdate: function () {
                $v.text(obj.n.toLocaleString('en-US', {
                    minimumFractionDigits: decimals, maximumFractionDigits: decimals
                }) + suffix);
            }
        });
    });

    /* ── 3) كشف عند التمرير ────────────────────────────────── */
    gsap.utils.toArray('.reveal').forEach(function (el) {
        gsap.from(el, {
            y: 26, autoAlpha: 0, duration: 0.55, ease: 'power2.out',
            clearProps: 'all',
            scrollTrigger: { trigger: el, start: 'top 92%', once: true }
        });
    });

    // بعد استقرار حركة الدخول تتغيّر المواضع — أعِد حساب نقاط التمرير
    // وإلا بقيت عناصر خارج نطاق الحساب مخفية عند opacity:0
    enter.eventCallback('onComplete', function () { ScrollTrigger.refresh(); });
    window.addEventListener('load', function () { ScrollTrigger.refresh(); });

    // صفوف الجداول: انزلاق خفيف فقط — بلا إخفاء.
    //
    // الجداول بيانات لا زينة. استخدام autoAlpha:0 هنا كان يترك صفوفاً
    // مكتوبة بالكامل غير مرئية إلى أن يمرّر المستخدم إليها (ظهر ذلك على
    // شاشة الجوال حيث تقع الجداول تحت الطية) — أي بيانات مخفية لا خلل بصري.
    // لذلك نحرّك الموضع فقط، وفقط للجداول الظاهرة وقت التحميل.
    gsap.utils.toArray('table tbody').forEach(function (tb) {
        var rows = tb.querySelectorAll('tr');
        if (!rows.length) return;
        if (tb.getBoundingClientRect().top >= window.innerHeight) return;

        gsap.from(rows, {
            y: 10, duration: 0.32, stagger: 0.03, delay: 0.25,
            ease: 'power1.out', clearProps: 'transform'
        });
    });

    /* ── 4) رسم أيقونات SVG عند الظهور ─────────────────────── */
    gsap.utils.toArray('.pillar .ico svg, .stat-ico svg, .trust-item .ico svg').forEach(function (svg) {
        var shapes = svg.querySelectorAll('path, circle, rect, line');
        gsap.fromTo(shapes,
            { strokeDasharray: 120, strokeDashoffset: 120, opacity: 0 },
            {
                strokeDashoffset: 0, opacity: 1, duration: 0.8, stagger: 0.06, ease: 'power2.out',
                scrollTrigger: { trigger: svg, start: 'top 92%', once: true },
                onComplete: function () { gsap.set(shapes, { clearProps: 'strokeDasharray,strokeDashoffset' }); }
            });
    });

    /* ── 5) الخط الزمني ينمو تدريجياً ──────────────────────── */
    gsap.utils.toArray('.timeline').forEach(function (tl) {
        gsap.from($(tl).find('li').toArray(), {
            x: 16, autoAlpha: 0, duration: 0.4, stagger: 0.07, ease: 'power2.out',
            scrollTrigger: { trigger: tl, start: 'top 85%', once: true }
        });
    });

    /* ── 6) أشرطة المطابقة تمتد لنسبتها ────────────────────── */
    gsap.utils.toArray('.match-bar > span').forEach(function (bar) {
        var target = bar.style.width;
        gsap.fromTo(bar, { width: '0%' }, {
            width: target, duration: 0.9, ease: 'power2.out',
            scrollTrigger: { trigger: bar, start: 'top 95%', once: true }
        });
    });

    /* ── 7) الخروج من الصفحة عند التنقل ────────────────────── */
    var leaving = false;
    $(document).on('click', 'a[href]', function (e) {
        var $a = $(this);
        var href = $a.attr('href');
        if (!href || leaving) return;
        if (e.ctrlKey || e.metaKey || e.shiftKey || e.which === 2) return;      // فتح بتبويب جديد
        if ($a.attr('target') || $a.attr('download')) return;
        if (href.charAt(0) === '#' || /^(mailto|tel|javascript):/i.test(href)) return;
        if ($a.is('[data-bs-toggle]')) return;                                   // عناصر Bootstrap
        if (this.hostname && this.hostname !== window.location.hostname) return; // رابط خارجي

        e.preventDefault();
        leaving = true;
        gsap.timeline({ onComplete: function () { window.location.href = href; } })
            .to('.content, main.container', { y: -8, autoAlpha: 0, duration: 0.16, ease: 'power1.in' }, 0)
            .to('#pageFade', { autoAlpha: 1, duration: 0.18, ease: 'power1.in' }, 0.02);
    });

    // الإرسال أيضاً يعطي إحساساً بالانتقال
    $(document).on('submit', 'form', function () {
        if (leaving) return;
        leaving = true;
        gsap.to('#pageFade', { autoAlpha: 1, duration: 0.2, ease: 'power1.in' });
    });

    // العودة بالسهم الخلفي: الصفحة تُستعاد من الذاكرة المؤقتة فتبقى مخفية — أعِد إظهارها
    window.addEventListener('pageshow', function (ev) {
        if (ev.persisted) {
            leaving = false;
            gsap.set('.content, main.container', { autoAlpha: 1, y: 0 });
            gsap.set('#pageFade', { autoAlpha: 0 });
        }
    });

    /* ── 8) تموّج عند الضغط على الأزرار ────────────────────── */
    $(document).on('pointerdown', '.btn', function (e) {
        var $b = $(this);
        if ($b.css('position') === 'static') $b.css('position', 'relative');
        var rect = this.getBoundingClientRect();
        var size = Math.max(rect.width, rect.height) * 2;
        var $r = $('<span class="ripple"></span>').css({
            width: size, height: size,
            left: e.clientX - rect.left - size / 2,
            top: e.clientY - rect.top - size / 2
        });
        $b.append($r);
        gsap.fromTo($r[0], { scale: 0, opacity: 0.45 },
            { scale: 1, opacity: 0, duration: 0.55, ease: 'power2.out',
              onComplete: function () { $r.remove(); } });
    });

    /* ── 9) رفع البطاقات عند المرور ────────────────────────── */
    $(document).on('mouseenter', '.auction-card, .pillar, .stat-card', function () {
        gsap.to(this, { y: -3, duration: 0.22, ease: 'power2.out' });
    }).on('mouseleave', '.auction-card, .pillar, .stat-card', function () {
        gsap.to(this, { y: 0, duration: 0.28, ease: 'power2.out' });
    });

    /* ── 10) الشارات تنبض عند الظهور ───────────────────────── */
    gsap.from('.badge', {
        scale: 0.82, autoAlpha: 0, duration: 0.32, stagger: 0.02,
        delay: 0.3, ease: 'back.out(1.7)', clearProps: 'all'
    });

    /* ── 11) شبكة أمان: لا يجوز أن يبقى محتوى مخفياً ────────
       إن أخفق أي مشغّل تمرير في الإطلاق، نُظهر العنصر قسراً.
       المحتوى غير المرئي عطل وظيفي، لا مجرد خلل بصري. */
    setTimeout(function () {
        $('.reveal, .stat-card, .pillar, .card, .content > *, main.container > *').each(function () {
            if (parseFloat($(this).css('opacity')) < 0.9 && $(this).is(':visible')) {
                // نُعيد خصائص الظهور فقط — لا clearProps:'all' لأنها تمسح
                // سمة style بالكامل وقد تُلغي أنماطاً أصلية للعنصر
                gsap.set(this, { opacity: 1, visibility: 'visible', y: 0, x: 0 });
            }
        });
    }, 2500);

})(jQuery);
