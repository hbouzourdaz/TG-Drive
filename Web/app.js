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
    
    // Dialog Mockup States Content
    const states = {
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

    // Make Step 2 active by default to look professional initially
    stepItems[1].classList.add('active');

    stepItems.forEach(item => {
        item.addEventListener('click', () => {
            // Remove active from all steps
            stepItems.forEach(s => s.classList.remove('active'));
            // Add active to current
            item.classList.add('active');
            
            const stepNum = parseInt(item.getAttribute('data-step'));
            const stateData = states[stepNum];

            // Render with beautiful fade transition
            authDialog.style.opacity = '0';
            authDialog.style.transform = 'translateY(8px)';
            
            setTimeout(() => {
                authDialog.querySelector('.dialog-header span').textContent = stateData.title;
                authDialog.querySelector('.dialog-body').innerHTML = stateData.bodyHtml;
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
