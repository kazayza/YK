window.navigateTo = function (url) {
    window.location.href = url;
};

window.downloadFile = function (fileName, contentType, base64) {
    var link = document.createElement('a');
    link.download = fileName;
    link.href = "data:" + contentType + ";base64," + base64;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

// هوية موحدة لمخرجات الطباعة، تغطي HTML المنفصل والطباعة المباشرة للصفحات.
window.ykPrintBranding = (function () {
    var fallbackLogo = '/images/yk-logo-gold.png';
    var companyProfile = { companyLogo: fallbackLogo };
    var brandAliases = [
        'yk coatings erp', 'yk coatings', 'yk coating',
        'مصنع واي كي كوتينج', 'واي كي كوتينج erp', 'واي كي كوتينج'
    ];

    function read(profile, key) {
        if (!profile) return '';
        var value = profile[key];
        if (value === undefined) {
            var pascal = key.charAt(0).toUpperCase() + key.slice(1);
            value = profile[pascal];
        }
        return value == null ? '' : String(value).trim();
    }

    function normalizeLogo(value) {
        var path = (value || '').trim();
        if (!path) return fallbackLogo;
        if (/^data:image\/(png|jpe?g|gif|webp|svg\+xml);base64,/i.test(path)) return path;
        if (/^(javascript|vbscript):/i.test(path) || path.indexOf('..') >= 0 || path.indexOf('\\') >= 0) return fallbackLogo;
        if (/^https?:\/\//i.test(path)) return path;
        if (path.indexOf(':') >= 0) return fallbackLogo;
        path = path.replace(/^wwwroot\//i, '').replace(/^\/+/, '');
        return '/' + path;
    }

    function normalizeText(value) {
        return (value || '').toLocaleLowerCase().replace(/[\u0640\s]+/g, ' ').trim();
    }

    function isLegacyBrandText(value) {
        var text = normalizeText(value);
        if (!text) return false;
        return brandAliases.some(function (alias) {
            var normalizedAlias = normalizeText(alias);
            return text === normalizedAlias || text.indexOf(normalizedAlias) >= 0;
        });
    }

    function isLegacyBrandOnly(value) {
        var text = normalizeText(value);
        if (!isLegacyBrandText(text)) return false;
        var remainder = text;
        brandAliases.slice().sort(function (a, b) { return b.length - a.length; }).forEach(function (alias) {
            remainder = remainder.replace(new RegExp(alias, 'ig'), ' ');
        });
        remainder = remainder
            .replace(/\berp\b|gold edition|v\s*\d+(?:\.\d+)*/ig, ' ')
            .replace(/نظام تخطيط موارد المؤسسات/g, ' ')
            .replace(/[—–|:;.\-]+/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
        return remainder.length === 0;
    }

    function isLegacyPrintMetaText(value) {
        var text = (value || '').replace(/\s+/g, ' ').trim();
        return /^(?:تاريخ الطباعة|تم الطباعة|طُبع في|طبع في)\s*[:：]?.{0,48}$/i.test(text);
    }

    function cleanLegacyFooter(value) {
        var text = (value || '').replace(/\s+/g, ' ').trim();
        text = text.replace(/(?:تم\s+الطباعة|طُبع\s+في|طبع\s+في)\s*[:：]?\s*[^—–|]*/i, ' ');
        text = text.replace(/تم\s+إنشاء\s+هذا\s+المستند\s+من\s+نظام[^.。]*/i, ' ');
        brandAliases.slice().sort(function (a, b) { return b.length - a.length; }).forEach(function (alias) {
            text = text.replace(new RegExp(alias, 'ig'), ' ');
        });
        text = text.replace(/(?:erp\s*)?(?:gold\s*edition|v\s*\d+(?:\.\d+)*)/ig, ' ');
        text = text.replace(/نظام تخطيط موارد المؤسسات/g, ' ');
        text = text.replace(/[|—–]+/g, ' ')
            .replace(/^[\s.,،؛:;|—–-]+|[\s.,،؛:;|—–-]+$/g, '')
            .replace(/\s+/g, ' ').trim();
        return text;
    }

    function addText(parent, tag, className, text) {
        if (!text) return null;
        var element = parent.ownerDocument.createElement(tag);
        if (className) element.className = className;
        element.textContent = text;
        parent.appendChild(element);
        return element;
    }

    function makeDetail(doc, label, value, dir) {
        if (!value) return null;
        var item = doc.createElement('span');
        item.className = dir ? 'yk-print-detail yk-print-detail-ltr' : 'yk-print-detail';
        var strong = doc.createElement('strong');
        strong.textContent = label + ': ';
        var content = doc.createElement('span');
        if (dir) content.dir = dir;
        content.textContent = value;
        item.appendChild(strong);
        item.appendChild(content);
        return item;
    }

    function addPrintStyles(doc) {
        if (doc.getElementById('yk-company-print-style')) return;
        var style = doc.createElement('style');
        style.id = 'yk-company-print-style';
        style.textContent = `
            .yk-print-header,.yk-print-footer{display:none;box-sizing:border-box;font-family:Arial,'Segoe UI',sans-serif}
            .yk-print-header{align-items:center;justify-content:space-between;gap:16px;border-bottom:2px solid #D4AF37;padding:8px 14px;background:#080B12;color:#F9FAFB;direction:ltr}
            .yk-print-logo{width:112px;height:62px;object-fit:contain;flex:0 0 auto}
            .yk-print-company{min-width:0;flex:1;display:flex;flex-direction:column;gap:2px;direction:rtl;text-align:right}
            .yk-print-company-ar{font-size:16px;font-weight:800;color:#fff}
            .yk-print-company-en{font-size:11px;font-weight:700;letter-spacing:.35px;color:#D4AF37;direction:ltr;text-align:right}
            .yk-print-footer{flex-direction:column;align-items:center;gap:4px;border-top:1px solid #D4AF37;padding-top:6px;background:#fff;font-size:8.5pt;line-height:1.35;color:#4b5563;text-align:center;direction:rtl}
            .yk-print-footer-details{display:flex;flex-wrap:wrap;justify-content:space-evenly;gap:4px 18px;align-items:center;width:100%;text-align:center}
            .yk-print-detail{display:inline-flex;flex:0 1 auto;justify-content:center;align-items:baseline;gap:3px;text-align:center;white-space:normal}
            .yk-print-detail-ltr{white-space:nowrap}
            .yk-print-detail strong{color:#273449;font-weight:700}
            .yk-print-footer-meta{display:flex;justify-content:center;gap:8px;width:100%;color:#6b7280;text-align:center}
            .yk-print-footer-note{width:100%;color:#374151;font-weight:700;text-align:center}
            .yk-print-actions{display:none;position:fixed;top:16px;right:16px;z-index:2147483000;gap:8px;direction:rtl;font-family:Arial,'Segoe UI',sans-serif}
            .yk-print-action{border:1px solid #D4AF37;border-radius:10px;padding:10px 16px;font:700 14px Arial,'Segoe UI',sans-serif;box-shadow:0 5px 16px rgba(0,0,0,.2);cursor:pointer}
            .yk-print-action-print{background:#D4AF37;color:#111827}
            .yk-print-action-close{background:#080B12;color:#F9FAFB}
            .yk-print-action:disabled{opacity:.6;cursor:wait}
            body.yk-print-popup .yk-print-actions{display:flex}
            body.yk-print-popup .yk-print-content .no-print,body.yk-print-popup .yk-print-content .print-toolbar,body.yk-print-popup .yk-print-content .toolbar{display:none!important}
            body.yk-print-popup .yk-print-header{display:flex;margin:64px 0 16px}
            body.yk-print-popup .yk-print-footer{display:flex;margin:18px 0 0}
            @page{margin:12mm}
            @media print{
                body.yk-print-ready{background:#fff!important;margin:0!important;padding:0!important;height:auto!important;min-height:0!important;overflow:visible!important}
                body.yk-print-ready .app-sidebar,body.yk-print-ready .app-topbar,body.yk-print-ready .app-footer,
                body.yk-print-ready .session-timeout-overlay,body.yk-print-ready .notif-bg,
                body.yk-print-ready .global-search-bg,body.yk-print-ready .notif-panel,
                body.yk-print-ready .tb-user-menu,body.yk-print-ready .global-search-dropdown,
                body.yk-print-ready .toast-container,body.yk-print-ready .toast{display:none!important}
                body.yk-print-ready .app-shell{display:block!important;min-height:0!important}
                body.yk-print-ready .app-main{display:block!important;width:100%!important;margin:0!important;min-height:0!important}
                body.yk-print-ready .app-body{display:block!important;min-height:0!important;margin:0!important;padding:0!important;overflow:visible!important}
                body.yk-print-ready .app-body-inner{max-width:none!important;width:100%!important;margin:0!important}
                body.yk-print-ready .yk-print-content{-webkit-box-decoration-break:clone;box-decoration-break:clone;padding:20mm 0!important}
                body.yk-print-ready .no-print,body.yk-print-ready .print-toolbar,
                body.yk-print-ready .yk-print-empty-legacy,.yk-print-actions{display:none!important}
                body.yk-print-ready .print-page,body.yk-print-ready .slip-page{padding:0!important}
                body.yk-print-ready .report-card,body.yk-print-ready .paper{box-shadow:none!important;max-width:none!important;margin:0!important}
                .yk-print-header{display:flex!important;position:fixed!important;top:0;left:0;right:0;z-index:2147483000;margin:0!important}
                .yk-print-footer{display:flex!important;position:fixed!important;bottom:0;left:0;right:0;z-index:2147483000;margin:0!important}
                .yk-print-logo,.yk-print-header{-webkit-print-color-adjust:exact;print-color-adjust:exact}
                .yk-print-header,.yk-print-footer{break-inside:avoid;page-break-inside:avoid}
            }
            @media screen{
                body:not(.yk-print-popup) .yk-print-header,body:not(.yk-print-popup) .yk-print-footer{display:none!important}
            }
        `;
        (doc.head || doc.documentElement).appendChild(style);
    }

    function prepare(doc, showPreview) {
        if (!doc || !doc.body) return function () { };
        if (doc.body.dataset.ykPrintPrepared === 'true') return doc.__ykPrintCleanup || function () { };

        var profile = companyProfile || {};
        var base = null;
        if (!doc.querySelector('base[href]') && doc.head) {
            base = doc.createElement('base');
            base.href = window.location.origin + '/';
            doc.head.insertBefore(base, doc.head.firstChild);
        }
        var retained = [];
        var notes = [];
        var legacyContainerCandidates = [];
        var emptyLegacyContainers = [];
        var selectors = '.company,.brand,.company-name,[data-company-brand],.footer,.foot';
        Array.prototype.slice.call(doc.querySelectorAll(selectors)).forEach(function (node) {
            if (node.closest('.yk-print-header,.yk-print-footer')) return;
            var classNames = node.classList || { contains: function () { return false; } };
            var isFooter = classNames.contains('footer') || classNames.contains('foot');
            if (isFooter) {
                var note = cleanLegacyFooter(node.textContent);
                if (note) notes.push(note);
            } else if (!node.hasAttribute('data-company-brand') && !isLegacyBrandText(node.textContent)) {
                return;
            }
            var marker = doc.createComment('yk-print-legacy');
            if (node.parentNode) {
                node.parentNode.replaceChild(marker, node);
                retained.push({ marker: marker, node: node });
            }
        });

        Array.prototype.slice.call(doc.querySelectorAll('.report-subtitle,h1,h2,p')).forEach(function (node) {
            if (node.closest('.yk-print-header,.yk-print-footer') || node.children.length !== 0) return;
            if (!isLegacyBrandOnly(node.textContent) && !isLegacyPrintMetaText(node.textContent)) return;
            if (node.parentElement) legacyContainerCandidates.push(node.parentElement);
            var marker = doc.createComment('yk-print-legacy-brand');
            if (node.parentNode) {
                node.parentNode.replaceChild(marker, node);
                retained.push({ marker: marker, node: node });
            }
        });

        Array.from(new Set(legacyContainerCandidates)).forEach(function (container) {
            if (container === doc.body) return;
            var hasVisibleContent = Array.prototype.some.call(container.childNodes, function (child) {
                if (child.nodeType === 3) return !!child.textContent.trim();
                return child.nodeType === 1;
            });
            if (!hasVisibleContent) {
                container.classList.add('yk-print-empty-legacy');
                emptyLegacyContainers.push(container);
            }
        });

        var header = doc.createElement('header');
        header.className = 'yk-print-header';
        header.setAttribute('aria-label', 'بيانات الشركة');
        var logo = doc.createElement('img');
        logo.className = 'yk-print-logo';
        logo.alt = read(profile, 'companyName') || read(profile, 'companyNameEn') || 'شعار الشركة';
        logo.src = normalizeLogo(read(profile, 'companyLogo'));
        logo.onerror = function () {
            if (logo.dataset.fallbackApplied !== 'true') {
                logo.dataset.fallbackApplied = 'true';
                logo.src = fallbackLogo;
            }
        };
        header.appendChild(logo);
        var company = doc.createElement('div');
        company.className = 'yk-print-company';
        addText(company, 'div', 'yk-print-company-ar', read(profile, 'companyName') || read(profile, 'companyNameEn'));
        var englishName = read(profile, 'companyNameEn');
        if (englishName && normalizeText(englishName) !== normalizeText(read(profile, 'companyName')))
            addText(company, 'div', 'yk-print-company-en', englishName);
        header.appendChild(company);

        var actions = null;
        var printButton = null;
        if (showPreview) {
            var printView = doc.defaultView;
            actions = doc.createElement('div');
            actions.className = 'yk-print-actions';
            actions.setAttribute('aria-label', 'إجراءات الطباعة');

            printButton = doc.createElement('button');
            printButton.type = 'button';
            printButton.className = 'yk-print-action yk-print-action-print';
            printButton.textContent = 'جارٍ تجهيز الطباعة…';
            printButton.disabled = true;
            printButton.addEventListener('click', function () {
                if (printButton.disabled || !printView) return;
                try { printView.focus(); printView.print(); } catch (e) { console.error('Print failed', e); }
            });

            var closeButton = doc.createElement('button');
            closeButton.type = 'button';
            closeButton.className = 'yk-print-action yk-print-action-close';
            closeButton.textContent = 'إغلاق';
            closeButton.addEventListener('click', function () {
                try { if (printView) printView.close(); } catch (e) { }
            });

            actions.appendChild(printButton);
            actions.appendChild(closeButton);
        }

        var footer = doc.createElement('footer');
        footer.className = 'yk-print-footer';
        footer.setAttribute('aria-label', 'تذييل بيانات الشركة');
        var details = doc.createElement('div');
        details.className = 'yk-print-footer-details';
        [
            makeDetail(doc, 'العنوان', read(profile, 'companyAddress')),
            makeDetail(doc, 'الهاتف', read(profile, 'companyPhone'), 'ltr'),
            makeDetail(doc, 'البريد', read(profile, 'companyEmail'), 'ltr'),
            makeDetail(doc, 'الرقم الضريبي', read(profile, 'taxNumber'), 'ltr'),
            makeDetail(doc, 'السجل التجاري', read(profile, 'commercialRegister'), 'ltr')
        ].forEach(function (item) { if (item) details.appendChild(item); });
        if (details.childNodes.length) footer.appendChild(details);
        var meta = doc.createElement('div');
        meta.className = 'yk-print-footer-meta';
        addText(meta, 'span', '', 'تم الطباعة: ' + new Date().toLocaleString('ar-EG'));
        footer.appendChild(meta);
        var uniqueNotes = Array.from(new Set(notes));
        if (uniqueNotes.length) addText(footer, 'div', 'yk-print-footer-note', uniqueNotes.join(' · '));

        addPrintStyles(doc);
        doc.body.classList.add('yk-print-ready');
        if (showPreview) doc.body.classList.add('yk-print-popup');
        var content = doc.createElement('div');
        content.className = 'yk-print-content';
        while (doc.body.firstChild) content.appendChild(doc.body.firstChild);
        if (actions) doc.body.appendChild(actions);
        doc.body.appendChild(header);
        doc.body.appendChild(content);
        doc.body.appendChild(footer);
        doc.body.dataset.ykPrintPrepared = 'true';

        var cleanup = function () {
            if (actions && actions.parentNode) actions.parentNode.removeChild(actions);
            if (header.parentNode) header.parentNode.removeChild(header);
            if (footer.parentNode) footer.parentNode.removeChild(footer);
            if (content.parentNode) {
                while (content.firstChild) doc.body.insertBefore(content.firstChild, content);
                content.parentNode.removeChild(content);
            }
            retained.forEach(function (item) {
                if (item.marker.parentNode) item.marker.parentNode.replaceChild(item.node, item.marker);
            });
            emptyLegacyContainers.forEach(function (container) {
                container.classList.remove('yk-print-empty-legacy');
            });
            var style = doc.getElementById('yk-company-print-style');
            if (style && style.parentNode) style.parentNode.removeChild(style);
            if (base && base.parentNode) base.parentNode.removeChild(base);
            doc.body.classList.remove('yk-print-ready', 'yk-print-popup');
            delete doc.body.dataset.ykPrintPrepared;
            delete doc.__ykPrintCleanup;
        };
        doc.__ykPrintCleanup = cleanup;
        return cleanup;
    }

    return {
        setProfile: function (profile) {
            companyProfile = Object.assign({ companyLogo: fallbackLogo }, profile || {});
            if (!read(companyProfile, 'companyLogo')) companyProfile.companyLogo = fallbackLogo;
        },
        clearProfile: function () {
            companyProfile = { companyLogo: fallbackLogo };
        },
        prepare: prepare
    };
})();

// توحيد الطباعة المباشرة من صفحات التطبيق دون التأثير على العرض العادي.
(function () {
    var nativePrint = window.print.bind(window);
    window.print = function () {
        var cleanup = window.ykPrintBranding.prepare(document, false);
        window.addEventListener('afterprint', cleanup, { once: true });
        return nativePrint();
    };
})();

// طباعة HTML في نافذة جديدة مع غلاف الشركة الموحد.
window.printHtml = function (html) {
    var printWindow = window.open('', '_blank', 'width=900,height=700');
    if (!printWindow) return false;

    printWindow.document.open();
    printWindow.document.write(html || '<!doctype html><html><body></body></html>');
    printWindow.document.close();
    window.ykPrintBranding.prepare(printWindow.document, true);
    printWindow.focus();

    var printButton = printWindow.document.querySelector('.yk-print-action-print');
    var images = Array.prototype.slice.call(printWindow.document.images || []);
    var imageLoads = images.map(function (image) {
        if (image.complete) return Promise.resolve();
        return new Promise(function (resolve) {
            image.addEventListener('load', resolve, { once: true });
            image.addEventListener('error', resolve, { once: true });
            setTimeout(resolve, 2000);
        });
    });
    var fontsReady = printWindow.document.fonts && printWindow.document.fonts.ready
        ? printWindow.document.fonts.ready.catch(function () { })
        : Promise.resolve();
    var readiness = Promise.all([Promise.all(imageLoads), fontsReady]);
    var readinessTimeout = new Promise(function (resolve) { setTimeout(resolve, 2500); });

    Promise.race([readiness, readinessTimeout]).then(function () {
        if (printWindow.closed || !printButton) return;
        printButton.disabled = false;
        printButton.textContent = 'طباعة مباشرة';
    });
    return true;
};
// تشغيل صوت الإشعار
//window.playNotificationSound = function () {
  //  try {
    //    var audio = new Audio('/sounds/notification.mp3');
      //  audio.volume = 1;
        //audio.play().catch(function() {});
    //} catch (e) {}
//};
// تشغيل صوت إشعار بدون ملف خارجي
window.playNotificationSound = function () {
    try {
        var ctx = new (window.AudioContext || window.webkitAudioContext)();
        
        // النغمة الأولى
        var osc1 = ctx.createOscillator();
        var gain1 = ctx.createGain();
        osc1.connect(gain1);
        gain1.connect(ctx.destination);
        osc1.frequency.value = 830;
        osc1.type = 'sine';
        gain1.gain.setValueAtTime(0.3, ctx.currentTime);
        gain1.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + 0.3);
        osc1.start(ctx.currentTime);
        osc1.stop(ctx.currentTime + 0.3);

        // النغمة الثانية
        var osc2 = ctx.createOscillator();
        var gain2 = ctx.createGain();
        osc2.connect(gain2);
        gain2.connect(ctx.destination);
        osc2.frequency.value = 1050;
        osc2.type = 'sine';
        gain2.gain.setValueAtTime(0.3, ctx.currentTime + 0.15);
        gain2.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + 0.5);
        osc2.start(ctx.currentTime + 0.15);
        osc2.stop(ctx.currentTime + 0.5);
    } catch (e) {
        console.log('Sound not supported');
    }
};
// جلب معلومات الجهاز
window.getClientInfo = function () {
    return {
        machineName: navigator.userAgent.substring(0, 100),
        screenSize: screen.width + 'x' + screen.height,
        language: navigator.language || 'unknown',
        platform: navigator.platform || 'unknown'
    };
};

// ==========================================
// 👇 أضف دالة التمرير الآمن هنا (بديل eval) 👇
// ==========================================
window.scrollToElement = function (element) {
    if (element) {
        element.scrollIntoView({ 
            behavior: 'smooth', 
            block: 'start',
            inline: 'nearest'
        });
    }
};

// ===== APEX CHARTS - DASHBOARD LUXURY GOLD =====
window.dashboardCharts = {
    monthlyChart: null,
    balanceChart: null,
    alertChart: null,

    fmtNum: function (v) { return Number(v).toLocaleString('en-US'); },

    renderMonthly: function (categories, series) {
        var el = document.querySelector("#chartMonthly");
        if (!el || !Array.isArray(series) || !series.length) return;
        if (this.monthlyChart) { this.monthlyChart.destroy(); }
        var fmt = this.fmtNum;
        var colors = series.map(function (item) {
            return item.name === 'المبيعات' ? '#10b981' : '#D4AF37';
        });
        var options = {
            series: series,
            chart: { type: 'area', height: 320, fontFamily: 'Tajawal, sans-serif', toolbar: { show: false }, background: 'transparent', animations: { enabled: true, easing: 'easeinout', speed: 800 } },
            dataLabels: { enabled: false },
            stroke: { curve: 'smooth', width: 2.5, colors: colors },
            fill: { type: 'gradient', gradient: { shade: 'dark', type: 'vertical', shadeIntensity: 0, opacityFrom: 0.30, opacityTo: 0.03, stops: [0, 100] } },
            colors: colors,
            legend: { show: series.length > 1, position: 'top', horizontalAlign: 'center', fontFamily: 'Tajawal', labels: { colors: '#8B95A5' }, markers: { width: 9, height: 9, radius: 9 } },
            xaxis: { categories: categories, labels: { style: { colors: '#8B95A5', fontFamily: 'Tajawal', fontSize: '12px' } }, axisBorder: { show: false }, axisTicks: { show: false } },
            yaxis: { labels: { style: { colors: '#8B95A5', fontFamily: 'Tajawal' }, formatter: fmt } },
            grid: { borderColor: 'rgba(212,175,55,0.08)', strokeDashArray: 4, xaxis: { lines: { show: false } } },
            tooltip: { theme: 'dark', style: { fontFamily: 'Tajawal' }, y: { formatter: function (v) { return fmt(v) + ' ج.م'; } } },
            markers: { size: 0, colors: colors, strokeColors: '#070B14', strokeWidth: 2, hover: { size: 6 } }
        };
        this.monthlyChart = new ApexCharts(el, options);
        this.monthlyChart.render();
    },

    renderBalances: function (suppliers, customers, stock) {
        var el = document.querySelector("#chartBalances");
        if (!el) return;
        if (this.balanceChart) { this.balanceChart.destroy(); }
        var fmt = this.fmtNum;
        var options = {
            series: [suppliers, customers, stock],
            chart: { type: 'donut', height: 280, fontFamily: 'Tajawal', background: 'transparent' },
            labels: ['الموردين', 'العملاء', 'المخزون'],
            colors: ['#0ea5e9', '#10b981', '#D4AF37'],
            dataLabels: { enabled: false },
            legend: { position: 'bottom', fontFamily: 'Tajawal', labels: { colors: '#8B95A5' } },
            plotOptions: { pie: { donut: { size: '68%', labels: { show: true, total: { show: true, label: 'الإجمالي', fontFamily: 'Tajawal', color: '#F5E6C8', formatter: function (w) { return fmt(w.globals.seriesTotals.reduce(function (a, b) { return a + b; }, 0)); } }, value: { color: '#F5E6C8', fontFamily: 'Tajawal', formatter: fmt } } } } },
            stroke: { width: 0 },
            tooltip: { theme: 'dark', y: { formatter: function (v) { return fmt(v) + ' ج.م'; } } }
        };
        this.balanceChart = new ApexCharts(el, options);
        this.balanceChart.render();
    },

    renderAlerts: function (outOfStock, belowMin, nearExpiry, expired) {
        var el = document.querySelector("#chartAlerts");
        if (!el) return;
        if (this.alertChart) { this.alertChart.destroy(); }
        var options = {
            series: [{ name: 'تنبيهات', data: [outOfStock, belowMin, nearExpiry, expired] }],
            chart: { type: 'bar', height: 260, fontFamily: 'Tajawal', toolbar: { show: false }, background: 'transparent' },
            colors: ['#ef4444', '#f59e0b', '#eab308', '#dc2626'],
            plotOptions: { bar: { borderRadius: 8, columnWidth: '45%', distributed: true, dataLabels: { position: 'top' } } },
            dataLabels: { enabled: true, formatter: function (val) { return val; }, offsetY: -20, style: { fontSize: '12px', colors: ['#F5E6C8'], fontFamily: 'Tajawal' } },
            xaxis: { categories: ['نفد', 'تحت الحد', 'قرب انتهاء', 'منتهي'], labels: { style: { colors: ['#8B95A5', '#8B95A5', '#8B95A5', '#8B95A5'], fontFamily: 'Tajawal', fontSize: '11px' } }, axisBorder: { show: false }, axisTicks: { show: false } },
            yaxis: { labels: { show: false } },
            grid: { show: false },
            legend: { show: false },
            tooltip: { theme: 'dark' }
        };
        this.alertChart = new ApexCharts(el, options);
        this.alertChart.render();
    },

    // Sparkline صغير داخل كروت الإحصائيات
    renderSpark: function (id, data, color) {
        var el = document.querySelector("#" + id);
        if (!el || !data || !data.length) return;
        if (el._sparkChart) { el._sparkChart.destroy(); }
        var options = {
            series: [{ data: data }],
            chart: { type: 'area', height: 34, width: '100%', sparkline: { enabled: true }, fontFamily: 'Tajawal', background: 'transparent', animations: { speed: 600 } },
            stroke: { curve: 'smooth', width: 1.8, colors: [color] },
            fill: { type: 'gradient', gradient: { shade: 'light', type: 'vertical', opacityFrom: 0.35, opacityTo: 0, colorStops: [{ offset: 0, color: color, opacity: 0.35 }, { offset: 100, color: color, opacity: 0 }] } },
            colors: [color],
            tooltip: { enabled: false }
        };
        el._sparkChart = new ApexCharts(el, options);
        el._sparkChart.render();
    }
};


window.sessionTimeoutManager = (() => {
    let dotNetRef = null;
    let warningTimer = null;
    let logoutTimer = null;
    let timeoutMinutes = 30;
    let warningMinutes = 2;
    let warningShown = false;

    const activityEvents = ["click", "keydown", "touchstart", "mousedown"];

    function clearTimers() {
        if (warningTimer) {
            clearTimeout(warningTimer);
            warningTimer = null;
        }

        if (logoutTimer) {
            clearTimeout(logoutTimer);
            logoutTimer = null;
        }
    }

    function removeListeners() {
        activityEvents.forEach(evt => {
            document.removeEventListener(evt, onActivity, true);
        });
    }

    function addListeners() {
        activityEvents.forEach(evt => {
            document.addEventListener(evt, onActivity, true);
        });
    }

    function startTimers() {
        clearTimers();

        const warningDelayMs = Math.max((timeoutMinutes - warningMinutes) * 60 * 1000, 1000);
        const logoutDelayMs = Math.max(timeoutMinutes * 60 * 1000, 1000);

        warningTimer = setTimeout(() => {
            warningShown = true;

            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("ShowSessionWarning").catch(() => { });
            }
        }, warningDelayMs);

        logoutTimer = setTimeout(() => {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("ForceLogoutFromIdle").catch(() => { });
            }
        }, logoutDelayMs);
    }

    function onActivity() {
        if (!dotNetRef) return;

        if (warningShown) {
            warningShown = false;
            dotNetRef.invokeMethodAsync("HideSessionWarning").catch(() => { });
        }

        startTimers();
    }

    return {
        init: function (ref, timeoutMins, warningMins) {
            dotNetRef = ref;

            timeoutMinutes = parseInt(timeoutMins || 30);
            warningMinutes = parseInt(warningMins || 2);

            if (isNaN(timeoutMinutes) || timeoutMinutes < 2)
                timeoutMinutes = 30;

            if (isNaN(warningMinutes) || warningMinutes < 1)
                warningMinutes = 2;

            if (warningMinutes >= timeoutMinutes)
                warningMinutes = 1;

            warningShown = false;

            removeListeners();
            addListeners();
            startTimers();
        },

        stayAlive: function () {
            onActivity();
        },

        stop: function () {
            clearTimers();
            warningShown = false;
            removeListeners();
            dotNetRef = null;
        }
    };
})();