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

// طباعة HTML في نافذة جديدة
window.printHtml = function (html) {
    var printWindow = window.open('', '_blank', 'width=900,height=700');
    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.focus();
    setTimeout(function () {
        printWindow.print();
    }, 500);
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

// ===== APEX CHARTS - DASHBOARD LUXURY GOLD =====
window.dashboardCharts = {
    monthlyChart: null,
    balanceChart: null,
    alertChart: null,

    fmtNum: function (v) { return Number(v).toLocaleString('en-US'); },

    renderMonthly: function (categories, amounts) {
        var el = document.querySelector("#chartMonthly");
        if (!el) return;
        if (this.monthlyChart) { this.monthlyChart.destroy(); }
        var fmt = this.fmtNum;
        var options = {
            series: [{ name: 'المشتريات', data: amounts }],
            chart: { type: 'area', height: 320, fontFamily: 'Tajawal, sans-serif', toolbar: { show: false }, background: 'transparent', animations: { enabled: true, easing: 'easeinout', speed: 800 } },
            dataLabels: { enabled: false },
            stroke: { curve: 'smooth', width: 2.5, colors: ['#D4AF37'] },
            fill: { type: 'gradient', gradient: { shade: 'dark', type: 'vertical', shadeIntensity: 0, opacityFrom: 0.35, opacityTo: 0.02, colorStops: [{ offset: 0, color: '#D4AF37', opacity: 0.4 }, { offset: 100, color: '#D4AF37', opacity: 0 }] } },
            colors: ['#D4AF37'],
            xaxis: { categories: categories, labels: { style: { colors: '#8B95A5', fontFamily: 'Tajawal', fontSize: '12px' } }, axisBorder: { show: false }, axisTicks: { show: false } },
            yaxis: { labels: { style: { colors: '#8B95A5', fontFamily: 'Tajawal' }, formatter: fmt } },
            grid: { borderColor: 'rgba(212,175,55,0.08)', strokeDashArray: 4, xaxis: { lines: { show: false } } },
            tooltip: { theme: 'dark', style: { fontFamily: 'Tajawal' }, y: { formatter: function (v) { return fmt(v) + ' ج.م'; } } },
            markers: { size: 0, colors: ['#D4AF37'], strokeColors: '#070B14', strokeWidth: 2, hover: { size: 6 } }
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