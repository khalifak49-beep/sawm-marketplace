// ── نافذة تأكيد منبثقة أنيقة (بديل confirm الافتراضي) ──
// أي <form data-confirm="الرسالة"> يعترضه المعالج ويعرض النافذة، ويُرسل عند التأكيد.
(function () {
    var overlay, msgEl, okBtn, cancelBtn, resolver, lastFocus;

    function build() {
        overlay = document.createElement('div');
        overlay.className = 'cx-overlay';
        overlay.innerHTML =
            '<div class="cx-modal" role="alertdialog" aria-modal="true" aria-labelledby="cxMsg">' +
            '  <div class="cx-head">' +
            '    <span class="cx-icon" aria-hidden="true">' +
            '      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '        <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4"/><path d="M12 17h.01"/>' +
            '      </svg>' +
            '    </span>' +
            '    <div><div class="cx-title">تأكيد الإجراء</div><div class="cx-msg" id="cxMsg"></div></div>' +
            '  </div>' +
            '  <div class="cx-actions">' +
            '    <button type="button" class="btn btn-green cx-ok">تأكيد</button>' +
            '    <button type="button" class="btn btn-outline-secondary cx-cancel">إلغاء</button>' +
            '  </div>' +
            '</div>';
        document.body.appendChild(overlay);
        msgEl = overlay.querySelector('.cx-msg');
        okBtn = overlay.querySelector('.cx-ok');
        cancelBtn = overlay.querySelector('.cx-cancel');
        okBtn.addEventListener('click', function () { close(true); });
        cancelBtn.addEventListener('click', function () { close(false); });
        overlay.addEventListener('click', function (e) { if (e.target === overlay) close(false); });
        document.addEventListener('keydown', function (e) {
            if (!overlay.classList.contains('show')) return;
            if (e.key === 'Escape') { e.preventDefault(); close(false); }
            else if (e.key === 'Enter') { e.preventDefault(); close(true); }
        });
    }

    function show(msg) {
        if (!overlay) build();
        msgEl.textContent = msg;
        lastFocus = document.activeElement;
        overlay.classList.add('show');
        setTimeout(function () { okBtn.focus(); }, 30);
        return new Promise(function (res) { resolver = res; });
    }

    function close(val) {
        overlay.classList.remove('show');
        if (lastFocus && lastFocus.focus) { try { lastFocus.focus(); } catch (e) { } }
        if (resolver) { var r = resolver; resolver = null; r(val); }
    }

    window.uiConfirm = show;

    // اعتراض أي نموذج يحمل data-confirm
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        var msg = form.getAttribute('data-confirm');
        if (!msg || form.dataset.confirmed === '1') return;
        // امنع الإرسال الفعلي، ومنع المعالجات الأخرى (مثل ستارة الانتقال في motion.js)
        // من العمل قبل أن يؤكّد المستخدم — وإلا بقيت الستارة الخضراء ظاهرة عند الإلغاء.
        e.preventDefault();
        e.stopPropagation();
        if (e.stopImmediatePropagation) e.stopImmediatePropagation();
        var submitter = e.submitter;
        show(msg).then(function (ok) {
            if (!ok) return;
            form.dataset.confirmed = '1';
            // إرسال حقيقي الآن — يمرّ لمعالج motion.js فترتفع الستارة للتنقّل (السلوك المقصود)
            if (form.requestSubmit) form.requestSubmit(submitter); else form.submit();
        });
    }, true);
})();
