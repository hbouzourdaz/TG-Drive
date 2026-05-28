document.addEventListener('DOMContentLoaded', () => {
    // ----------------------------------------------------
    // 1. Dynamic Theme Selection (Dark / Light)
    // ----------------------------------------------------
    const themeToggleBtn = document.getElementById('theme-toggle-btn');
    const body = document.body;

    // Retrieve cached theme or fallback to user system preferences
    const cachedTheme = localStorage.getItem('tg-theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;

    if (cachedTheme === 'light' || (!cachedTheme && !prefersDark)) {
        body.classList.remove('dark-theme');
        body.classList.add('light-theme');
    } else {
        body.classList.remove('light-theme');
        body.classList.add('dark-theme');
    }

    themeToggleBtn.addEventListener('click', () => {
        if (body.classList.contains('dark-theme')) {
            body.classList.remove('dark-theme');
            body.classList.add('light-theme');
            localStorage.setItem('tg-theme', 'light');
        } else {
            body.classList.remove('light-theme');
            body.classList.add('dark-theme');
            localStorage.setItem('tg-theme', 'dark');
        }
    });

    // ----------------------------------------------------
    // 2. Glassmorphic Navigation Bar Transition on Scroll
    // ----------------------------------------------------
    const navbar = document.getElementById('navbar');
    
    window.addEventListener('scroll', () => {
        if (window.scrollY > 30) {
            navbar.style.boxShadow = '0 8px 30px var(--shadow-color)';
            navbar.style.height = '68px';
        } else {
            navbar.style.boxShadow = 'none';
            navbar.style.height = '72px';
        }
    });

    // ----------------------------------------------------
    // 3. Interactive Installation Steps & C# Auth Mockup
    // ----------------------------------------------------
    const stepItems = document.querySelectorAll('.step-item');
    const authDialog = document.querySelector('.auth-dialog-card');
    
    // Translation Dictionary
    const translations = {
        en: {
            "nav-brand": "Telegram Drive",
            "nav-features": "Features",
            "nav-setup": "Setup",
            "nav-download": "Download",
            "hero-badge": "Windows Client v1.0.0",
            "hero-title": 'Unlimited Storage, <br><span class="highlight">Mapped Locally.</span>',
            "hero-desc": "Mount your secure Telegram cloud account directly as a Windows Local Disk. Backup, download, and manage files at lightning speed without using up your physical SSD space.",
            "hero-download-text": "Download for Windows (x64)",
            "hero-meta-size": "Size: ~53.7 MB",
            "hero-meta-admin": "No Admin Privileges Required",
            "exp-title": "File Explorer &rsaquo; This PC",
            "exp-this-pc": "This PC",
            "exp-downloads": "Downloads",
            "exp-backup": "Backup",
            "exp-devices-title": "Devices and drives (3)",
            "drive-c-name": "Local Disk (C:)",
            "drive-c-capacity": "12.4 GB free of 256 GB",
            "drive-d-name": "Archive (D:)",
            "drive-d-capacity": "341 GB free of 1 TB",
            "drive-t-name": "Telegram Cloud (T:)",
            "drive-t-capacity": "Unlimited Free Storage",
            "feat-badge": "Capabilities",
            "feat-title": "Why Telegram Drive?",
            "feat-desc": "A robust, single-file packaging of performance-first storage features built for Windows 10 & 11.",
            "feat-card-1-title": "Infinite Cloud Space",
            "feat-card-1-desc": "Mount infinite virtual folders using Telegram's secure servers, freeing up your local physical drives.",
            "feat-card-2-title": "Optimized Speed & Recovery",
            "feat-card-2-desc": "Powered by WTelegramClient with built-in cache-busting retries to gracefully handle network dropouts.",
            "feat-card-3-title": "Local & Encrypted",
            "feat-card-3-desc": "All database logs, configuration paths, and API keys are strictly saved in an encrypted local SQLite file.",
            "feat-card-4-title": "Single-Instance Protection",
            "feat-card-4-desc": "Built-in fail-safe multi-launch guards automatically clean up ghost zombie processes on launch.",
            "setup-badge": "Getting Started",
            "setup-title": "Set up in 3 simple steps",
            "setup-desc": "Getting your unlimited virtual drive up and running takes less than 60 seconds.",
            "step-1-title": "Download and Install",
            "step-1-desc": "Get the 53MB installer, which is self-contained and installs under your local user folder instantly.",
            "step-2-title": "Authenticate with Telegram",
            "step-2-desc": "Provide your safe API ID, Hash, and phone number to start your secure native WTelegram session.",
            "step-3-title": "Configure Folders & Sync",
            "step-3-desc": "Map your downloads and automatic background backup folders to enjoy infinite virtual storage.",
            "foot-github": "GitHub",
            "foot-download": "Direct Download",
            "foot-copy": "&copy; 2026 Telegram Drive Client. Formulated with performance first."
        },
        ar: {
            "nav-brand": "Telegram Drive",
            "nav-features": "الميزات",
            "nav-setup": "الإعداد",
            "nav-download": "تحميل",
            "hero-badge": "عميل ويندوز v1.0.0",
            "hero-title": 'مساحة تخزين غير محدودة، <br><span class="highlight">مربوطة محلياً.</span>',
            "hero-desc": "قم بربط حساب التخزين السحابي لـ Telegram كقرص محلي على نظام Windows. انسخ ملفاتك احتياطيًا، وحملها، وأدرها بسرعة فائقة دون استهلاك مساحة القرص الصلب (SSD) الخاصة بك.",
            "hero-download-text": "تحميل لنظام ويندوز (x64)",
            "hero-meta-size": "الحجم: ~53.7 ميجابايت",
            "hero-meta-admin": "لا يتطلب صلاحيات المسؤول (Admin)",
            "exp-title": "مستكشف الملفات &rsaquo; هذا الكمبيوتر",
            "exp-this-pc": "هذا الكمبيوتر",
            "exp-downloads": "التنزيلات",
            "exp-backup": "النسخ الاحتياطي",
            "exp-devices-title": "الأجهزة ومحركات الأقراص (3)",
            "drive-c-name": "القرص المحلي (C:)",
            "drive-c-capacity": "12.4 جيجابايت متبقية من 256 جيجابايت",
            "drive-d-name": "الأرشيف (D:)",
            "drive-d-capacity": "341 جيجابايت متبقية من 1 تيرابايت",
            "drive-t-name": "سحابة تليجرام (T:)",
            "drive-t-capacity": "مساحة مجانية غير محدودة",
            "feat-badge": "الميزات والقدرات",
            "feat-title": "لماذا Telegram Drive؟",
            "feat-desc": "حزمة برمجية متكاملة وقوية تركز على الأداء الفائق لميزات التخزين، مصممة خصيصاً لنظامي التشغيل Windows 10 و 11.",
            "feat-card-1-title": "مساحة سحابية لا نهائية",
            "feat-card-1-desc": "قم بإنشاء مجلدات افتراضية لا نهائية باستخدام خوادم Telegram الآمنة، مما يوفر مساحة محركات الأقراص الفعلية لديك.",
            "feat-card-2-title": "سرعة فائقة واسترداد ذكي",
            "feat-card-2-desc": "مدعوم بمكتبة WTelegramClient مع نظام تلقائي ذكي لإعادة المحاولة وتخطي التخزين المؤقت للتعامل بمرونة مع انقطاع الشبكة.",
            "feat-card-3-title": "تخزين محلي مشفر",
            "feat-card-3-desc": "تُحفظ جميع سجلات قاعدة البيانات، مسارات التهيئة، ومفاتيح API محلياً وبشكل آمن تماماً في قاعدة بيانات SQLite مشفرة.",
            "feat-card-4-title": "حماية من التشغيل المتعدد",
            "feat-card-4-desc": "حماية مدمجة تمنع التشغيل المتكرر وتقوم تلقائياً بإنهاء أي عمليات معلقة أو غير نشطة عند بدء التشغيل.",
            "setup-badge": "طريقة البدء",
            "setup-title": "الإعداد في 3 خطوات بسيطة",
            "setup-desc": "تشغيل قرصك الافتراضي غير المحدود يستغرق أقل من 60 ثانية.",
            "step-1-title": "التحميل والتثبيت",
            "step-1-desc": "احصل على ملف التثبيت بحجم 53 ميجابايت، وهو ملف متكامل يثبت في مجلد المستخدم الخاص بك على الفور دون الحاجة لصلاحيات مدير النظام.",
            "step-2-title": "المصادقة مع Telegram",
            "step-2-desc": "أدخل معرف API ID والهاش ورقم هاتفك لبدء جلسة اتصال آمنة ومباشرة مع خوادم Telegram.",
            "step-3-title": "تهيئة المجلدات والمزامنة",
            "step-3-desc": "قم بتعيين مجلدات التنزيل والنسخ الاحتياطي التلقائي للاستمتاع بمساحة تخزين افتراضية لا نهائية.",
            "foot-github": "جيت هاب",
            "foot-download": "تحميل مباشر",
            "foot-copy": "&copy; 2026 عميل Telegram Drive. تم تصميمه مع التركيز على الأداء الفائق."
        }
    };

    // Dialog Mockup States Content (English)
    const statesEn = {
        1: {
            title: "Installer Package Extraction",
            bodyHtml: `
                <div class="installer-step" style="display:flex; flex-direction:column; gap:14px; text-align:center; padding: 20px 10px;">
                    <div class="installer-icon" style="color:var(--primary-color); display:flex; justify-content:center;">
                        <svg viewBox="0 0 24 24" width="64" height="64" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>
                            <polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>
                            <line x1="12" y1="22.08" x2="12" y2="12"></line>
                        </svg>
                    </div>
                    <div style="font-weight:700; font-size:15px;">TelegramDrive Setup v1.0.0</div>
                    <div class="progress-bar infinite" style="margin-top:6px;"><div class="progress-fill" style="width:75%;"></div></div>
                    <span style="font-size:11px; color:var(--text-secondary);">Extracting files to LocalAppData/Programs...</span>
                </div>
            `
        },
        2: {
            title: "Telegram Storage Authentication",
            bodyHtml: `
                <div class="input-group">
                    <label>API ID</label>
                    <input type="text" placeholder="1234567" disabled value="2847952" style="border-color:var(--primary-color);">
                </div>
                <div class="input-group">
                    <label>API Hash</label>
                    <input type="password" placeholder="••••••••••••••••" disabled value="e3d9f0c29f98aaef28cfd8" style="border-color:var(--primary-color);">
                </div>
                <div class="input-group">
                    <label>Phone Number</label>
                    <input type="text" placeholder="+1 234 56789" disabled value="+213 671 ••• •••" style="border-color:var(--primary-color);">
                </div>
                <div class="input-group">
                    <label>Security Verification Code (OTP)</label>
                    <input type="text" placeholder="Enter OTP sent via Telegram" disabled style="background-color:var(--highlight-bg); border-color:var(--primary-color);">
                </div>
                <button class="dialog-btn" disabled style="background-color:var(--primary-color); opacity:1; cursor:pointer;">Connect Securely</button>
                <span class="dialog-note">All data is kept fully offline in local database.</span>
            `
        },
        3: {
            title: "Configure Folders & Mapping",
            bodyHtml: `
                <div class="settings-step" style="display:flex; flex-direction:column; gap:14px; padding: 5px 0;">
                    <div class="input-group">
                        <label>Virtual Disk Letter</label>
                        <select style="background-color:var(--bg-sidebar); border:1px solid var(--border-color); color:var(--text-primary); padding:10px 12px; border-radius:var(--border-radius-sm); font-size:13px; font-family:inherit; outline:none;" disabled>
                            <option>Telegram Cloud (T:)</option>
                        </select>
                    </div>
                    <div class="input-group">
                        <label>Local Download Cache Folder</label>
                        <input type="text" value="C:\\Users\\Hakim\\Downloads\\TelegramDrive" disabled style="border-color:var(--primary-color);">
                    </div>
                    <div class="input-group">
                        <label>Automatic Backup Directory</label>
                        <input type="text" value="C:\\Users\\Hakim\\Desktop\\SyncFolder" disabled style="border-color:var(--primary-color);">
                    </div>
                    <button class="dialog-btn" disabled style="background-color:var(--primary-color); opacity:1; cursor:pointer; display:flex; align-items:center; justify-content:center; gap:8px;">
                        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                            <polyline points="20 6 9 17 4 12"></polyline>
                        </svg>
                        Save Configuration
                    </button>
                </div>
            `
        }
    };

    // Dialog Mockup States Content (Arabic)
    const statesAr = {
        1: {
            title: "فك حزمة ملف التثبيت",
            bodyHtml: `
                <div class="installer-step" style="display:flex; flex-direction:column; gap:14px; text-align:center; padding: 20px 10px;">
                    <div class="installer-icon" style="color:var(--primary-color); display:flex; justify-content:center;">
                        <svg viewBox="0 0 24 24" width="64" height="64" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>
                            <polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>
                            <line x1="12" y1="22.08" x2="12" y2="12"></line>
                        </svg>
                    </div>
                    <div style="font-weight:700; font-size:15px;">تثبيت TelegramDrive إصدار 1.0.0</div>
                    <div class="progress-bar infinite" style="margin-top:6px;"><div class="progress-fill" style="width:75%;"></div></div>
                    <span style="font-size:11px; color:var(--text-secondary);">جاري استخراج الملفات إلى LocalAppData/Programs...</span>
                </div>
            `
        },
        2: {
            title: "المصادقة مع تخزين Telegram",
            bodyHtml: `
                <div class="input-group">
                    <label>معرف التطبيق (API ID)</label>
                    <input type="text" placeholder="1234567" disabled value="2847952" style="border-color:var(--primary-color);">
                </div>
                <div class="input-group">
                    <label>رمز التطبيق (API Hash)</label>
                    <input type="password" placeholder="••••••••••••••••" disabled value="e3d9f0c29f98aaef28cfd8" style="border-color:var(--primary-color);">
                </div>
                <div class="input-group">
                    <label>رقم الهاتف</label>
                    <input type="text" placeholder="+1 234 56789" disabled value="+213 671 ••• •••" style="border-color:var(--primary-color);">
                </div>
                <div class="input-group">
                    <label>رمز التحقق الأمني (OTP)</label>
                    <input type="text" placeholder="أدخل رمز التحقق المرسل عبر تليجرام" disabled style="background-color:var(--highlight-bg); border-color:var(--primary-color);">
                </div>
                <button class="dialog-btn" disabled style="background-color:var(--primary-color); opacity:1; cursor:pointer;">اتصال آمن</button>
                <span class="dialog-note">يتم الاحتفاظ بجميع البيانات محلياً وبشكل كامل خارج الإنترنت.</span>
            `
        },
        3: {
            title: "تهيئة المجلدات والربط",
            bodyHtml: `
                <div class="settings-step" style="display:flex; flex-direction:column; gap:14px; padding: 5px 0;">
                    <div class="input-group">
                        <label>حرف القرص الافتراضي</label>
                        <select style="background-color:var(--bg-sidebar); border:1px solid var(--border-color); color:var(--text-primary); padding:10px 12px; border-radius:var(--border-radius-sm); font-size:13px; font-family:inherit; outline:none;" disabled>
                            <option>سحابة تليجرام (T:)</option>
                        </select>
                    </div>
                    <div class="input-group">
                        <label>مجلد التخزين المؤقت المحلي للتنزيلات</label>
                        <input type="text" value="C:\\Users\\Hakim\\Downloads\\TelegramDrive" disabled style="border-color:var(--primary-color);">
                    </div>
                    <div class="input-group">
                        <label>مسار النسخ الاحتياطي التلقائي</label>
                        <input type="text" value="C:\\Users\\Hakim\\Desktop\\SyncFolder" disabled style="border-color:var(--primary-color);">
                    </div>
                    <button class="dialog-btn" disabled style="background-color:var(--primary-color); opacity:1; cursor:pointer; display:flex; align-items:center; justify-content:center; gap:8px;">
                        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                            <polyline points="20 6 9 17 4 12"></polyline>
                        </svg>
                        حفظ الإعدادات
                    </button>
                </div>
            `
        }
    };

    let currentLang = localStorage.getItem('tg-lang') || 'en';
    
    function getStates() {
        return currentLang === 'ar' ? statesAr : statesEn;
    }

    // Handle interactive dialog updates
    function updateMockupContent(stepNum) {
        const stateData = getStates()[stepNum];
        authDialog.querySelector('.dialog-header span').textContent = stateData.title;
        authDialog.querySelector('.dialog-body').innerHTML = stateData.bodyHtml;
    }

    // Language Toggler Elements
    const langToggleBtn = document.getElementById('lang-toggle-btn');
    const langLabel = langToggleBtn.querySelector('.lang-label');

    function setLanguage(lang) {
        currentLang = lang;
        localStorage.setItem('tg-lang', lang);

        // Update elements text
        const elements = document.querySelectorAll('[data-i18n]');
        elements.forEach(el => {
            const key = el.getAttribute('data-i18n');
            if (translations[lang] && translations[lang][key]) {
                el.innerHTML = translations[lang][key];
            }
        });

        // Toggle layout attributes
        if (lang === 'ar') {
            body.setAttribute('dir', 'rtl');
            body.setAttribute('lang', 'ar');
            langLabel.textContent = 'EN';
        } else {
            body.removeAttribute('dir');
            body.setAttribute('lang', 'en');
            langLabel.textContent = 'AR';
        }

        // Re-render active mockup state in current language
        const activeStep = document.querySelector('.step-item.active');
        if (activeStep) {
            const stepNum = parseInt(activeStep.getAttribute('data-step'));
            updateMockupContent(stepNum);
        }
    }

    // Initialize Language Selector
    setLanguage(currentLang);

    langToggleBtn.addEventListener('click', () => {
        setLanguage(currentLang === 'en' ? 'ar' : 'en');
    });

    stepItems.forEach(item => {
        item.addEventListener('click', () => {
            stepItems.forEach(s => s.classList.remove('active'));
            item.classList.add('active');
            
            const stepNum = parseInt(item.getAttribute('data-step'));

            // Render with beautiful fade transition
            authDialog.style.opacity = '0';
            authDialog.style.transform = 'translateY(8px)';
            
            setTimeout(() => {
                updateMockupContent(stepNum);
                authDialog.style.opacity = '1';
                authDialog.style.transform = 'translateY(0)';
            }, 180);
        });
    });

    // Simple smooth entry animation for cards
    const cards = document.querySelectorAll('.feature-card');
    cards.forEach((card, index) => {
        card.style.opacity = '0';
        card.style.transform = 'translateY(24px)';
        card.style.transition = `opacity 0.6s cubic-bezier(0.16, 1, 0.3, 1) ${index * 0.1}s, transform 0.6s cubic-bezier(0.16, 1, 0.3, 1) ${index * 0.1}s, border-color var(--transition-speed) ease`;
        
        setTimeout(() => {
            card.style.opacity = '1';
            card.style.transform = 'translateY(0)';
        }, 100);
    });
});

