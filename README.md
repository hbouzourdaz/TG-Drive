# TG-Drive

TG-Drive is a sleek, premium, performance-first Windows desktop client that maps your secure Telegram cloud storage account directly as a native Windows Local Disk (Virtual Drive). Manage files, upload, download, and backup data at high speeds without using up your physical SSD space.

---

## ✨ Features

- 📂 **Virtual Local Disk:** Mounts directly in Windows File Explorer as a native drive letter (e.g. `T:`), making interaction as simple as dragging and dropping.
- ♾️ **Unlimited Cloud Space:** Store files using Telegram's API without consuming physical storage space on your PC.
- ⚡ **Optimized Speed & Recovery:** Powered by `WTelegramClient` with a robust cache-busting retry logic that automatically circumvents host connection dropouts.
- 🔒 **Encrypted Local Database:** All database mappings, configuration parameters, and API keys are stored strictly offline in an encrypted SQLite database.
- 🛠️ **Process-Based Multi-Launch Protection:** Fail-safe single-instance check in `App.xaml.cs` automatically terminates background zombie processes to keep startup clean.
- 📦 **Professional Self-Contained Installer:** Lightweight 53MB setup installer that runs without administrator privileges and configures local app directory mapping.
- 🌐 **Minimalist Web Landing Page:** Elegant fully responsive page designed using Telegram's visual system, featuring an interactive C# WinUI auth preview mockup.

---

## 🚀 Tech Stack

- **Client:** C# WinUI 3 (Windows App SDK v1.6.240829007)
- **Framework:** .NET 8.0 (Targeting Windows 10.0.19041+)
- **Library:** WTelegramClient v4.2.5
- **Database:** Microsoft.Data.Sqlite
- **Installer:** Inno Setup 6 (Solid LZMA2/Ultra64 compression)
- **Web Landing:** Vanilla HTML5, CSS3 (Telegram Design System), ES6 Javascript

---

## 🛠️ Getting Started

### Prerequisites
- Windows 10 or 11
- Telegram API Credentials (API ID and Hash from [my.telegram.org](https://my.telegram.org))

### Installing
1. Go to the `Installer` directory.
2. Run the [TelegramDriveSetup.exe](Installer/TelegramDriveSetup.exe) installer.
3. Choose to install under your local user directory (no Admin privileges needed).
4. Launch the application, input your secure Telegram API credentials, verify the OTP sent to your Telegram app, and start mapping!

---

## 📁 Repository Structure

- `TelegramDrive/` - Main C# WinUI 3 solution & project source code.
- `Installer/` - Ready-to-use self-contained setup installer executables.
- `Web/` - Minimalist web landing page code (HTML, CSS, JS).
- `TelegramDriveSetup.iss` - Inno Setup configuration compiler script.
- `telegram_drive_app.py` - Auxiliary integration python scripts.
- `README.md` - Repository overview and usage guidelines.
