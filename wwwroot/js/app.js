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