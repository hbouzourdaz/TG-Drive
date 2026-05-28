import os
import sys
import sqlite3
import shutil
import asyncio
import threading
import webbrowser
from datetime import datetime
import tkinter as tk
from tkinter import filedialog, messagebox

# ==========================================
# PRE-INSTALLER / DEPENDENCY CONTROL
# ==========================================
try:
    import customtkinter as ctk
    from telethon import TelegramClient
    from telethon.errors import SessionPasswordNeededError
    HAS_DEPS = True
except ImportError:
    HAS_DEPS = False

if not HAS_DEPS:
    import tkinter as tk
    import subprocess
    
    def install_dependencies():
        try:
            status_label.config(text="Installing 'customtkinter' and 'telethon'... Please wait.")
            btn.config(state="disabled")
            window.update()
            subprocess.check_call([sys.executable, "-m", "pip", "install", "customtkinter", "telethon"])
            messagebox.showinfo("Success", "Dependencies installed successfully! Please restart the application.")
            window.destroy()
            sys.exit(0)
        except Exception as e:
            messagebox.showerror("Error", f"Failed to install dependencies: {str(e)}")
            btn.config(state="normal")
            status_label.config(text="Installation failed. Run 'pip install customtkinter telethon' manually.")

    window = tk.Tk()
    window.title("Setup Required - Telegram Cloud Storage")
    window.geometry("480x240")
    window.configure(bg="#1c1c1c")
    window.resizable(False, False)
    
    lbl = tk.Label(window, text="Dependencies Missing", fg="#0088cc", bg="#1c1c1c", font=("Segoe UI", 16, "bold"))
    lbl.pack(pady=15)
    
    msg = tk.Label(
        window, 
        text="This application requires 'customtkinter' and 'telethon' to run.\nClick the button below to install them automatically using pip.", 
        fg="white", bg="#1c1c1c", font=("Segoe UI", 10), justify="center"
    )
    msg.pack(pady=10)
    
    btn = tk.Button(
        window, text="Install Dependencies Automatically", command=install_dependencies, 
        fg="white", bg="#0088cc", activebackground="#006699", activeforeground="white", 
        font=("Segoe UI", 11, "bold"), padx=15, pady=8, border=0, cursor="hand2"
    )
    btn.pack(pady=10)
    
    status_label = tk.Label(window, text="", fg="gray", bg="#1c1c1c", font=("Segoe UI", 9, "italic"))
    status_label.pack(pady=5)
    
    window.mainloop()
    sys.exit(0)

# ==========================================
# CONFIGURATION PLACEHOLDERS
# ==========================================
API_ID = 0  # Replace with your Telegram API ID (integer)
API_HASH = ""  # Replace with your Telegram API HASH (string)
SESSION_NAME = "tg_storage_session"
DB_NAME = "telegram_drive.db"

# Local Storage root path definition (workspace absolute directory)
LOCAL_DRIVE_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "local_storage_drive"))
os.makedirs(LOCAL_DRIVE_ROOT, exist_ok=True)

# ==========================================
# SQLITE VIRTUAL FILE SYSTEM
# ==========================================
class DBHelper:
    """Manages the hierarchical SQL Database mapping files and folders to Telegram message IDs."""
    def __init__(self, db_path=DB_NAME):
        self.db_path = db_path
        self.init_db()

    def get_conn(self):
        conn = sqlite3.connect(self.db_path)
        conn.row_factory = sqlite3.Row
        return conn

    def init_db(self):
        with self.get_conn() as conn:
            # Virtual Folders table
            conn.execute("""
                CREATE TABLE IF NOT EXISTS folders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    parent_id INTEGER DEFAULT NULL,
                    FOREIGN KEY(parent_id) REFERENCES folders(id)
                )
            """)
            # Virtual Files table mapping to Telegram message ID
            conn.execute("""
                CREATE TABLE IF NOT EXISTS files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    filename TEXT NOT NULL,
                    file_size INTEGER NOT NULL,
                    telegram_message_id INTEGER NOT NULL,
                    upload_date TEXT NOT NULL,
                    folder_id INTEGER DEFAULT NULL,
                    FOREIGN KEY(folder_id) REFERENCES folders(id)
                )
            """)
            # Application Status & Key-Value settings table
            conn.execute("""
                CREATE TABLE IF NOT EXISTS status (
                    key TEXT PRIMARY KEY,
                    value TEXT
                )
            """)
            conn.commit()

        # Database migration: safely add folder_id column to files if it is missing
        try:
            with self.get_conn() as conn:
                conn.execute("ALTER TABLE files ADD COLUMN folder_id INTEGER DEFAULT NULL")
                conn.commit()
        except sqlite3.OperationalError:
            pass  # Column already exists

    def set_status_val(self, key, value):
        """Saves any application status or configuration state into the database."""
        with self.get_conn() as conn:
            conn.execute("INSERT OR REPLACE INTO status (key, value) VALUES (?, ?)", (key, str(value)))
            conn.commit()

    def get_status_val(self, key, default=None):
        """Retrieves saved status or configuration state from the database."""
        try:
            with self.get_conn() as conn:
                cursor = conn.cursor()
                cursor.execute("SELECT value FROM status WHERE key = ?", (key,))
                row = cursor.fetchone()
                return row[0] if row else default
        except sqlite3.OperationalError:
            return default

    def add_file(self, filename, file_size, telegram_message_id, upload_date, folder_id=None):
        with self.get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute(
                "INSERT INTO files (filename, file_size, telegram_message_id, upload_date, folder_id) VALUES (?, ?, ?, ?, ?)",
                (filename, file_size, telegram_message_id, upload_date, folder_id)
            )
            conn.commit()
            return cursor.lastrowid

    def delete_file(self, file_id):
        with self.get_conn() as conn:
            conn.execute("DELETE FROM files WHERE id = ?", (file_id,))
            conn.commit()

    def create_folder(self, name, parent_id=None):
        with self.get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute("INSERT INTO folders (name, parent_id) VALUES (?, ?)", (name, parent_id))
            conn.commit()
            return cursor.lastrowid

    def get_folders_in_folder(self, folder_id):
        """Fetches virtual folders nested in the target parent folder ID."""
        with self.get_conn() as conn:
            cursor = conn.cursor()
            if folder_id is None:
                cursor.execute("SELECT id, name, parent_id FROM folders WHERE parent_id IS NULL ORDER BY name ASC")
            else:
                cursor.execute("SELECT id, name, parent_id FROM folders WHERE parent_id = ? ORDER BY name ASC", (folder_id,))
            return [dict(row) for row in cursor.fetchall()]

    def get_files_in_folder(self, folder_id):
        """Fetches virtual files nested in the target parent folder ID."""
        with self.get_conn() as conn:
            cursor = conn.cursor()
            if folder_id is None:
                cursor.execute("SELECT id, filename, file_size, telegram_message_id, upload_date, folder_id FROM files WHERE folder_id IS NULL ORDER BY id DESC")
            else:
                cursor.execute("SELECT id, filename, file_size, telegram_message_id, upload_date, folder_id FROM files WHERE folder_id = ? ORDER BY id DESC", (folder_id,))
            return [dict(row) for row in cursor.fetchall()]

    def get_breadcrumb_path(self, folder_id):
        """Generates a hierarchical chain of folder names and IDs leading up to the current folder."""
        if folder_id is None:
            return [("Root", None)]
        
        path = []
        curr_id = folder_id
        with self.get_conn() as conn:
            while curr_id is not None:
                cursor = conn.cursor()
                cursor.execute("SELECT id, name, parent_id FROM folders WHERE id = ?", (curr_id,))
                row = cursor.fetchone()
                if row:
                    path.insert(0, (row["name"], row["id"]))
                    curr_id = row["parent_id"]
                else:
                    break
        path.insert(0, ("Root", None))
        return path

    def get_files_in_folder_recursive(self, folder_id):
        """Recursively gathers all files inside a folder and its subdirectories."""
        files = []
        with self.get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT id, telegram_message_id, filename FROM files WHERE folder_id = ?", (folder_id,))
            files.extend([dict(row) for row in cursor.fetchall()])
            
            cursor.execute("SELECT id FROM folders WHERE parent_id = ?", (folder_id,))
            subfolders = [row[0] for row in cursor.fetchall()]
            for sub_id in subfolders:
                files.extend(self.get_files_in_folder_recursive(sub_id))
        return files

    def delete_folder_recursive(self, folder_id):
        """Recursively clears database records for a folder, its subfolders, and associated files."""
        with self.get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT id FROM folders WHERE parent_id = ?", (folder_id,))
            subfolders = [row[0] for row in cursor.fetchall()]
            for sub_id in subfolders:
                self.delete_folder_recursive(sub_id)
            
            conn.execute("DELETE FROM files WHERE folder_id = ?", (folder_id,))
            conn.execute("DELETE FROM folders WHERE id = ?", (folder_id,))
            conn.commit()

    def get_storage_stats(self):
        """Fetches total files count and overall drive storage space consumed in Cloud."""
        with self.get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute("SELECT COUNT(id), SUM(file_size) FROM files")
            row = cursor.fetchone()
            count = row[0] or 0
            size = row[1] or 0
            return count, size

# ==========================================
# ASYNC LOOP THREAD (GUI BRIDGING MECHANISM)
# ==========================================
class AsyncLoopThread(threading.Thread):
    """A persistent background daemon thread executing an asyncio loop for Telethon client operations."""
    def __init__(self):
        super().__init__(daemon=True)
        self.loop = asyncio.new_event_loop()

    def run(self):
        asyncio.set_event_loop(self.loop)
        self.loop.run_forever()

# ==========================================
# CUSTOM WIDGETS: TELEGRAM CHAT ROW
# ==========================================
class ChatRow(ctk.CTkFrame):
    """A custom widget designed to look like a Telegram Desktop Chat List Row."""
    def __init__(self, parent, title, subtitle, icon_char, badge_color, active_bg, callback):
        super().__init__(parent, fg_color="transparent", height=60, corner_radius=8, cursor="hand2")
        self.callback = callback
        self.active_bg = active_bg
        self.normal_bg = "transparent"
        
        self.pack(fill="x", pady=2, padx=6)
        self.pack_propagate(False)
        
        # Left Circular Badge / Avatar
        self.badge = ctk.CTkLabel(
            self, text=icon_char, corner_radius=20, fg_color=badge_color,
            text_color="#ffffff", width=40, height=40,
            font=ctk.CTkFont(family="Segoe UI", size=16, weight="bold")
        )
        self.badge.pack(side="left", padx=(10, 12), pady=10)
        self.badge.pack_propagate(False)
        
        # Counter Badge on Right (Pill-shaped, classic Telegram style)
        self.counter_badge = ctk.CTkLabel(
            self, text="", corner_radius=10, fg_color="#2481cc",
            text_color="#ffffff", width=22, height=20,
            font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold")
        )
        self.counter_badge.pack_propagate(False)
        
        # Texts Frame
        self.txt_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.txt_frame.pack(side="left", fill="both", expand=True, pady=10)
        
        self.lbl_title = ctk.CTkLabel(
            self.txt_frame, text=title, text_color="#ffffff",
            font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"), anchor="w"
        )
        self.lbl_title.pack(fill="x")
        
        self.lbl_sub = ctk.CTkLabel(
            self.txt_frame, text=subtitle, text_color="#7f91a4",
            font=ctk.CTkFont(family="Segoe UI", size=11), anchor="w"
        )
        self.lbl_sub.pack(fill="x")
        
        # Bind events to simulate click across all child elements
        self.bind("<Button-1>", lambda e: self.callback())
        self.badge.bind("<Button-1>", lambda e: self.callback())
        self.counter_badge.bind("<Button-1>", lambda e: self.callback())
        self.lbl_title.bind("<Button-1>", lambda e: self.callback())
        self.lbl_sub.bind("<Button-1>", lambda e: self.callback())
        self.txt_frame.bind("<Button-1>", lambda e: self.callback())
        
        # Bind hover effects
        self.bind("<Enter>", self.on_enter)
        self.bind("<Leave>", self.on_leave)
        
    def on_enter(self, event):
        if self.cget("fg_color") != self.active_bg:
            self.configure(fg_color="#202b36")  # Subtle Telegram dark hover
            
    def on_leave(self, event):
        if self.cget("fg_color") != self.active_bg:
            self.configure(fg_color=self.normal_bg)

    def set_active(self, is_active):
        self.configure(fg_color=self.active_bg if is_active else self.normal_bg)
        # Use contrasting background for counter badge when active
        if is_active:
            self.counter_badge.configure(fg_color="#5288c1")
        else:
            self.counter_badge.configure(fg_color="#2481cc")

    def set_counter(self, count):
        """Displays number of items dynamically in a custom Telegram-style badge."""
        if count is not None and count > 0:
            self.counter_badge.configure(text=str(count))
            self.counter_badge.pack(side="right", padx=(5, 12), pady=20)
        else:
            self.counter_badge.pack_forget()


# ==========================================
# CUSTOM WIDGET: PREMIUM SELECTION CHECKBOX
# ==========================================
class SelectionCheckbox(ctk.CTkLabel):
    """Premium circular checkbox styled like Telegram Desktop's message-selection bubbles.
    Inherits CTkLabel directly for maximum reliability — no nested place() overhead."""

    CHECKED_COLOR   = "#2481cc"    # Telegram blue when selected
    UNCHECKED_COLOR = "#1e2d3d"    # Subtle dark circle always visible against card bg
    HOVER_COLOR     = "#2b4870"    # Slightly brighter blue on hover
    CHECK_ICON      = "✓"

    def __init__(self, parent, checked=False, on_toggle=None, size=22):
        super().__init__(
            parent,
            text=self.CHECK_ICON if checked else "",
            width=size, height=size,
            corner_radius=size // 2,          # makes it a circle
            fg_color=self.CHECKED_COLOR if checked else self.UNCHECKED_COLOR,
            text_color="#ffffff",
            font=ctk.CTkFont(family="Segoe UI", size=int(size * 0.65), weight="bold"),
            cursor="hand2",
        )
        self._checked = checked
        self._on_toggle = on_toggle

        self.bind("<Button-1>", self._on_click)
        self.bind("<Enter>",    self._on_enter)
        self.bind("<Leave>",    self._on_leave)

    # ------------------------------------------------------------------
    def _on_click(self, event=None):
        self.set_checked(not self._checked)
        if self._on_toggle:
            self._on_toggle(self._checked)

    def _on_enter(self, event=None):
        if not self._checked:
            self.configure(fg_color=self.HOVER_COLOR)

    def _on_leave(self, event=None):
        if not self._checked:
            self.configure(fg_color=self.UNCHECKED_COLOR)

    # ------------------------------------------------------------------
    def set_checked(self, value: bool):
        self._checked = value
        if value:
            self.configure(text=self.CHECK_ICON, fg_color=self.CHECKED_COLOR)
        else:
            self.configure(text="", fg_color=self.UNCHECKED_COLOR)

    def get(self) -> bool:
        return self._checked




# ==========================================
# PREMIUM TOAST NOTIFICATION WIDGET
# ==========================================
class ToastNotification(ctk.CTkToplevel):
    """Non-blocking slide-in toast notification at bottom-right corner.
    Auto-dismisses after `duration` ms. Types: success, error, info, warning."""

    _STYLE = {
        "success": {"icon": "✓", "accent": "#2bca4f", "bg": "#0d1f14"},
        "error":   {"icon": "✕", "accent": "#ff4d4d", "bg": "#1f0d0d"},
        "info":    {"icon": "ℹ", "accent": "#2481cc", "bg": "#0e1621"},
        "warning": {"icon": "⚠", "accent": "#e6a100", "bg": "#1a1500"},
    }

    def __init__(self, parent, message: str, kind: str = "info", duration: int = 3500):
        super().__init__(parent)
        st = self._STYLE.get(kind, self._STYLE["info"])

        self.overrideredirect(True)
        self.attributes("-topmost", True)
        self.configure(fg_color=st["bg"])
        self.resizable(False, False)

        # Card frame with colored border
        card = ctk.CTkFrame(
            self, fg_color=st["bg"], corner_radius=10,
            border_color=st["accent"], border_width=1
        )
        card.pack(fill="both", expand=True, padx=1, pady=1)

        # Icon circle
        ctk.CTkLabel(
            card, text=st["icon"],
            width=28, height=28, corner_radius=14,
            fg_color=st["accent"], text_color="#ffffff",
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold")
        ).pack(side="left", padx=(12, 8), pady=14)

        # Message label
        ctk.CTkLabel(
            card, text=message, text_color="#dce8f5",
            font=ctk.CTkFont(family="Segoe UI", size=12),
            wraplength=260, justify="left", anchor="w"
        ).pack(side="left", fill="x", expand=True, pady=14)

        # Close button
        ctk.CTkButton(
            card, text="✕", command=self._close,
            width=24, height=24, corner_radius=12,
            fg_color="transparent", text_color="#7f91a4",
            hover_color="#242f3d", font=ctk.CTkFont(size=10)
        ).pack(side="right", padx=(4, 10))

        # Position: bottom-right of parent window
        self.update_idletasks()
        toast_w, toast_h = 360, 60
        self.geometry(f"{toast_w}x{toast_h}")
        parent.update_idletasks()
        x = parent.winfo_x() + parent.winfo_width()  - toast_w - 18
        y = parent.winfo_y() + parent.winfo_height() - toast_h - 42
        self.geometry(f"+{x}+{y}")

        # Click on card closes it too
        card.bind("<Button-1>", lambda e: self._close())

        # Auto-dismiss
        self.after(duration, self._close)

    def _close(self):
        try:
            self.destroy()
        except Exception:
            pass


# ==========================================
# APP MANAGER (MAIN COORDINATOR)
# ==========================================
class TGStorageApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        
        # Configure Main Window
        self.title("Telegram Unlimited Cloud Storage")
        self.geometry("1040x680")
        self.minsize(920, 600)
        
        # Configure Style and Theme
        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("blue")
        
        # Initialize SQLite DB Helper
        self.db = DBHelper()
        
        # Async Controller Setup
        self.async_thread = AsyncLoopThread()
        self.async_thread.start()
        
        # Application States
        self.client = None
        self.current_phone = ""
        self.phone_code_hash = ""
        
        # Transfer Queue System States
        self.transfer_queue = []            # List of jobs
        self.active_job_id = None
        self.job_counter = 0
        self.is_transferring = False
        
        # Default Downloads Folder Configuration
        self.downloads_dir = self.db.get_status_val("downloads_dir", os.path.join(os.path.expanduser("~"), "Downloads"))
        
        # Auto-Backup Watcher Thread States
        self.backup_watcher_running = False
        self.auto_backup_dir = self.db.get_status_val("auto_backup_dir", "")
        
        # Active Storage Backend Toggle (cloud vs local)
        self.active_backend = "cloud"
        
        # Root Frame View Manager
        self.current_frame = None
        
        # Determine startup view
        self.startup_check()

    def startup_check(self):
        """Checks if session credentials are preconfigured or saved in DB, and tests auth state."""
        self.show_loading("Initializing Telegram Client...")
        
        api_id = API_ID
        api_hash = API_HASH
        
        if api_id == 0 or api_hash == "":
            saved_id = self.db.get_status_val("api_id")
            saved_hash = self.db.get_status_val("api_hash")
            if saved_id and saved_hash:
                try:
                    api_id = int(saved_id)
                    api_hash = saved_hash
                except ValueError:
                    pass
                
        if api_id != 0 and api_hash != "":
            self.run_async_coro(self.connect_and_check_auth(api_id, api_hash))
        else:
            self.show_login_frame()

    def run_async_coro(self, coro):
        """Helper to run a coroutine in the background loop thread."""
        return asyncio.run_coroutine_threadsafe(coro, self.async_thread.loop)

    async def connect_and_check_auth(self, api_id, api_hash):
        """Asynchronously connect the Telethon client and check authorization status."""
        try:
            self.client = TelegramClient(SESSION_NAME, api_id, api_hash, loop=self.async_thread.loop)
            await self.client.connect()
            
            authorized = await self.client.is_user_authorized()
            
            if authorized:
                me = await self.client.get_me()
                user_info = {
                    "name": f"{me.first_name or ''} {me.last_name or ''}".strip(),
                    "username": f"@{me.username}" if me.username else "Saved Messages"
                }
                self.after(0, self.show_dashboard_frame, user_info)
            else:
                self.after(0, self.show_login_frame)
        except Exception as e:
            self.after(0, self.show_login_frame, f"Connection error: {str(e)}")

    # ----------------------------------------------------
    # VIEW TRANSITIONS
    # ----------------------------------------------------
    def clear_current_frame(self):
        if self.current_frame is not None:
            self.current_frame.destroy()
            self.current_frame = None

    def show_loading(self, message):
        self.clear_current_frame()
        self.current_frame = LoadingFrame(self, message)
        self.current_frame.pack(fill="both", expand=True)

    def show_login_frame(self, error_msg=""):
        self.clear_current_frame()
        self.current_frame = LoginFrame(self, error_msg)
        self.current_frame.pack(fill="both", expand=True)

    def show_otp_frame(self, phone_number, error_msg=""):
        self.clear_current_frame()
        self.current_frame = OTPFrame(self, phone_number, error_msg)
        self.current_frame.pack(fill="both", expand=True)

    def show_qr_frame(self):
        self.clear_current_frame()
        self.current_frame = QRLoginFrame(self)
        self.current_frame.pack(fill="both", expand=True)

    def show_help_dialog(self):
        """Launches the custom 'Getting Started' help modal."""
        HelpDialog(self)

    def show_dashboard_frame(self, user_info):
        self.clear_current_frame()
        self.current_frame = DashboardFrame(self, user_info)
        self.current_frame.pack(fill="both", expand=True)
        
        # Start the Auto-Backup watcher thread if a mapped directory exists
        if self.auto_backup_dir and not self.backup_watcher_running:
            self.start_backup_watcher()

    # ----------------------------------------------------
    # SIGN IN ACTIONS (ASYNC CALLS TO THE MAIN CONTROLLER)
    # ----------------------------------------------------
    def request_otp(self, api_id, api_hash, phone):
        self.show_loading("Requesting OTP from Telegram...")
        self.run_async_coro(self.async_request_otp(api_id, api_hash, phone))

    async def async_request_otp(self, api_id, api_hash, phone):
        try:
            if self.client is not None:
                try:
                    await self.client.disconnect()
                except Exception:
                    pass
            
            self.client = TelegramClient(SESSION_NAME, api_id, api_hash, loop=self.async_thread.loop)
            await self.client.connect()
            
            sent_code = await self.client.send_code_request(phone)
            self.current_phone = phone
            self.phone_code_hash = sent_code.phone_code_hash
            
            self.db.set_status_val("api_id", api_id)
            self.db.set_status_val("api_hash", api_hash)
            
            self.after(0, self.show_otp_frame, phone)
        except Exception as e:
            self.after(0, self.show_login_frame, f"Failed to send code: {str(e)}")

    def verify_otp(self, otp_code, password=""):
        self.show_loading("Verifying authentication...")
        self.run_async_coro(self.async_verify_otp(otp_code, password))

    async def async_verify_otp(self, otp_code, password):
        try:
            if password:
                await self.client.sign_in(password=password)
            else:
                try:
                    await self.client.sign_in(self.current_phone, otp_code, phone_code_hash=self.phone_code_hash)
                except SessionPasswordNeededError:
                    self.after(0, self.handle_2fa_required)
                    return
            
            if await self.client.is_user_authorized():
                me = await self.client.get_me()
                user_info = {
                    "name": f"{me.first_name or ''} {me.last_name or ''}".strip(),
                    "username": f"@{me.username}" if me.username else "Saved Messages"
                }
                self.after(0, self.show_dashboard_frame, user_info)
            else:
                self.after(0, self.show_otp_frame_with_error, "Auth failed: Unauthorized state.")
        except Exception as e:
            self.after(0, self.show_otp_frame_with_error, str(e))

    def show_otp_frame_with_error(self, error_msg):
        self.show_otp_frame(self.current_phone, error_msg)

    def handle_2fa_required(self):
        self.show_otp_frame(self.current_phone)
        if isinstance(self.current_frame, OTPFrame):
            self.current_frame.enable_2fa_field()
            self.current_frame.set_status("2FA Password is required for this account.", "orange")

    # ----------------------------------------------------
    # DIRECT SCANNED QR LOGIN OPERATIONS (TELEGRAM DIRECT)
    # ----------------------------------------------------
    def request_qr_login(self, api_id, api_hash):
        self.show_qr_frame()
        self.run_async_coro(self.async_qr_login(api_id, api_hash))

    async def async_qr_login(self, api_id, api_hash):
        try:
            if self.client is not None:
                try:
                    await self.client.disconnect()
                except Exception:
                    pass
            
            self.client = TelegramClient(SESSION_NAME, api_id, api_hash, loop=self.async_thread.loop)
            await self.client.connect()
            
            self.db.set_status_val("api_id", api_id)
            self.db.set_status_val("api_hash", api_hash)
            
            qr_login = await self.client.qr_login()
            
            import urllib.request
            import urllib.parse
            import io
            from PIL import Image
            
            api_url = f"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={urllib.parse.quote(qr_login.url)}"
            
            def fetch_image():
                try:
                    req = urllib.request.Request(api_url, headers={'User-Agent': 'Mozilla/5.0'})
                    with urllib.request.urlopen(req) as response:
                        img_data = response.read()
                    img = Image.open(io.BytesIO(img_data))
                    self.after(0, self.on_qr_image_loaded, img, qr_login)
                except Exception as ex:
                    self.after(0, self.on_qr_error, f"Failed to load QR image: {str(ex)}")
            
            threading.Thread(target=fetch_image, daemon=True).start()
        except Exception as e:
            self.after(0, self.on_qr_error, f"QR connection failed: {str(e)}")

    def on_qr_image_loaded(self, pil_image, qr_login):
        if isinstance(self.current_frame, QRLoginFrame):
            self.current_frame.display_qr(pil_image)
            self.run_async_coro(self.async_wait_for_qr(qr_login))

    async def async_wait_for_qr(self, qr_login):
        try:
            user = await qr_login.wait()
            me = await self.client.get_me()
            user_info = {
                "name": f"{me.first_name or ''} {me.last_name or ''}".strip(),
                "username": f"@{me.username}" if me.username else "Saved Messages"
            }
            self.after(0, self.show_dashboard_frame, user_info)
        except SessionPasswordNeededError:
            self.after(0, self.handle_2fa_required)
        except Exception as e:
            self.after(0, self.on_qr_error, f"QR verification failed: {str(e)}")

    def on_qr_error(self, error_msg):
        if isinstance(self.current_frame, QRLoginFrame):
            self.current_frame.show_error(error_msg)
        else:
            self.show_login_frame(error_msg)

    def update_transfer_status(self, text, percent=0.0):
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.progress_label.configure(text=text)
            self.current_frame.progress_bar.set(percent)
            self.current_frame.progress_percent.configure(text=f"{int(percent * 100)}%")

    def update_transfer_progress(self, current, total, percent):
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.progress_bar.set(percent)
            self.current_frame.progress_percent.configure(text=f"{int(percent * 100)}%")
            
            # Format sizes
            curr_formatted = self.current_frame.format_size(current)
            tot_formatted = self.current_frame.format_size(total)
            self.current_frame.progress_size_label.configure(text=f"{curr_formatted} / {tot_formatted}")
            
            # Update specific file card progress bar!
            if self.active_job_id is not None:
                for job in self.transfer_queue:
                    if job['id'] == self.active_job_id:
                        filename = job['filename']
                        if hasattr(self.current_frame, "file_progress_bars") and filename in self.current_frame.file_progress_bars:
                            try:
                                self.current_frame.file_progress_bars[filename].set(percent)
                            except Exception:
                                pass
                        break

    def show_toast(self, message: str, kind: str = "info", duration: int = 3500):
        """Displays a non-blocking premium toast notification at bottom-right."""
        try:
            ToastNotification(self, message, kind=kind, duration=duration)
        except Exception:
            pass  # Fallback: silently ignore if window not ready

    def transfer_complete(self, message):
        self.is_transferring = False
        for job in self.transfer_queue:
            if job['id'] == self.active_job_id:
                job['status'] = 'Completed'
                break
        self.active_job_id = None
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.progress_label.configure(text="Transfer: Idle")
            self.current_frame.progress_bar.set(0.0)
            self.current_frame.progress_percent.configure(text="0%")
            self.current_frame.progress_size_label.configure(text="0.0 MB / 0.0 MB")
            self.current_frame.refresh_files()
            self.current_frame.refresh_queue_card()
        self.show_toast(message, kind="success")
        self.after(0, self.process_queue)

    def transfer_cancelled(self, message):
        for job in self.transfer_queue:
            if job['id'] == self.active_job_id:
                job['status'] = 'Cancelled'
                break
        self.active_job_id = None
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.progress_label.configure(text="Transfer: Idle")
            self.current_frame.progress_bar.set(0.0)
            self.current_frame.progress_percent.configure(text="0%")
            self.current_frame.progress_size_label.configure(text="0.0 MB / 0.0 MB")
            self.current_frame.refresh_files()
            self.current_frame.refresh_queue_card()
        self.show_toast(message, kind="warning")
        self.after(0, self.process_queue)

    def cancel_transfer_job(self, filename):
        """Cancels a transferring or pending job by filename."""
        cancelled_any = False
        for job in self.transfer_queue:
            if job['filename'] == filename and job['status'] in ('Pending', 'Running'):
                job['status'] = 'Cancelled'
                cancelled_any = True
                
                # If it was running, the next progress callback will raise CancelledError.
                # If it was pending, we can remove it instantly on the main thread!
                if job['id'] != self.active_job_id:
                    if isinstance(self.current_frame, DashboardFrame):
                        self.current_frame.refresh_files()
                        self.current_frame.refresh_queue_card()
                    self.show_toast(f"'{filename}' removed from queue.", kind="warning")
                break

    def transfer_failed(self, message):
        self.is_transferring = False
        for job in self.transfer_queue:
            if job['id'] == self.active_job_id:
                job['status'] = 'Failed'
                break
        self.active_job_id = None
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.progress_label.configure(text="Transfer: Idle")
            self.current_frame.progress_bar.set(0.0)
            self.current_frame.progress_percent.configure(text="0%")
            self.current_frame.progress_size_label.configure(text="0.0 MB / 0.0 MB")
            self.current_frame.refresh_queue_card()
        self.show_toast(message, kind="error")
        self.after(0, self.process_queue)

    def on_delete_success(self, message):
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.refresh_files()
        self.show_toast(message, kind="success")

    def on_delete_failure(self, message):
        self.show_toast(message, kind="error")

    def perform_logout(self):
        self.show_loading("Logging out...")
        self.run_async_coro(self.async_logout())

    async def async_logout(self):
        try:
            if self.client:
                await self.client.log_out()
            if os.path.exists(SESSION_NAME + ".session"):
                os.remove(SESSION_NAME + ".session")
            if os.path.exists(DB_NAME):
                os.remove(DB_NAME)
            self.db = DBHelper()
            self.after(0, self.show_login_frame, "Logged out successfully.")
        except Exception as e:
            self.after(0, self.show_login_frame, f"Error logging out: {str(e)}")

    # ----------------------------------------------------
    # TRANSFER QUEUE SYSTEM COORDINATOR
    # ----------------------------------------------------
    def add_to_queue(self, type_val, filepath=None, filename=None, filesize=0, folder_id=None, message_id=None, dest_dir=None, source_path=None):
        """Adds a transfer task cleanly to the sequential transfer queue and initiates scheduling."""
        self.job_counter += 1
        job = {
            'id': self.job_counter,
            'type': type_val,              # 'upload' or 'download'
            'filepath': filepath,          # local source file
            'filename': filename,          # display name
            'filesize': filesize,          # raw bytes
            'folder_id': folder_id,        # virtual folder parent
            'message_id': message_id,      # Telegram cloud message ID
            'dest_dir': dest_dir,          # local download destination
            'source_path': source_path,    # local copy source path
            'status': 'Pending',
            'percent': 0.0,
            'size_text': '0.0 MB / 0.0 MB'
        }
        self.transfer_queue.append(job)
        self.after(0, self.process_queue)
        
        # Instantly update sidebar queue counts
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.refresh_queue_card()

    def process_queue(self):
        """Sequential queue scheduler. Checks for pending jobs and executes them in background loops."""
        if self.active_job_id is not None:
            # A job is currently running, wait for it to complete
            return
            
        # Locate the first pending job
        pending_job = None
        for job in self.transfer_queue:
            if job['status'] == 'Pending':
                pending_job = job
                break
                
        if pending_job is None:
            # Queue is empty or all jobs are processed!
            if isinstance(self.current_frame, DashboardFrame):
                self.current_frame.refresh_queue_card()
            return
            
        self.active_job_id = pending_job['id']
        pending_job['status'] = 'Running'
        
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.refresh_queue_card()
            
        # Dispatch based on type
        if pending_job['type'] == 'upload':
            if self.active_backend == "cloud":
                self.run_async_coro(self.async_upload(pending_job))
            else:
                self.trigger_upload_local(pending_job)
        elif pending_job['type'] == 'download':
            if self.active_backend == "cloud":
                self.run_async_coro(self.async_download(pending_job))
            else:
                self.trigger_download_local(pending_job)

    # ----------------------------------------------------
    # AUTONOMOUS BACKGROUND AUTO-BACKUP SYNCHRONIZER
    # ----------------------------------------------------
    def start_backup_watcher(self):
        """Launches the autonomous background watcher thread."""
        self.backup_watcher_running = True
        threading.Thread(target=self.backup_watcher_loop, daemon=True).start()

    def backup_watcher_loop(self):
        """Scans the mapped auto-backup local directory and auto-queues new files."""
        import time
        while getattr(self, 'backup_watcher_running', False):
            try:
                folder = self.db.get_status_val("auto_backup_dir", "")
                if folder and os.path.exists(folder):
                    for entry in os.scandir(folder):
                        if entry.is_file():
                            filename = entry.name
                            filepath = entry.path
                            filesize = entry.stat().st_size
                            
                            # Check if the filename already exists in SQLite Virtual VFS index
                            with self.db.get_conn() as conn:
                                cursor = conn.cursor()
                                cursor.execute("SELECT id FROM files WHERE filename = ?", (filename,))
                                row = cursor.fetchone()
                                
                            if not row:
                                # New file detected! Push to transfer queue immediately
                                self.after(0, self.add_to_queue, "upload", filepath, filename, filesize, None)
            except Exception:
                pass
            time.sleep(15)  # Scan mapped directory every 15 seconds

    # ----------------------------------------------------
    # FILE & FOLDER CONTROLLERS (UPLOAD, DOWNLOAD, DELETE)
    # ----------------------------------------------------
    def trigger_sync(self):
        """Dispatches an asynchronous Telegram Cloud synchronization job."""
        if self.is_transferring:
            messagebox.showwarning("Active Job", "Another transfer is currently running. Please wait.")
            return
            
        # Add confirmation prompt before sync
        if not messagebox.askyesno("Sync Cloud", "Do you want to synchronize your local SQLite index with Telegram Cloud?\nThis will scan your Telegram Saved Messages to fetch any document files."):
            return
            
        self.is_transferring = True
        self.sync_cancelled = False
        self.after(0, self.update_transfer_status, "Syncing Telegram...", 0.0)
        
        # Instantly refresh sidebar stats to show active sync cancellation
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.refresh_queue_card()
            
        self.run_async_coro(self.async_sync())

    def sync_was_cancelled(self, message):
        self.is_transferring = False
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.progress_label.configure(text="Transfer: Idle")
            self.current_frame.progress_bar.set(0.0)
            self.current_frame.progress_percent.configure(text="0%")
            self.current_frame.progress_size_label.configure(text="0.0 MB / 0.0 MB")
            self.current_frame.refresh_files()
            self.current_frame.refresh_queue_card()
        self.show_toast(message, kind="warning")

    async def async_sync(self):
        """Scans Telegram Saved Messages for sent document files and indexes them in SQLite."""
        try:
            # 1. Fetch all existing message IDs in our SQLite index to prevent duplicate records
            existing_msg_ids = set()
            with self.db.get_conn() as conn:
                cursor = conn.cursor()
                cursor.execute("SELECT telegram_message_id FROM files")
                for row in cursor.fetchall():
                    existing_msg_ids.add(row[0])
            
            # 2. Iterate through messages in Saved Messages ('me')
            synced_count = 0
            # Get up to 1000 messages (or more, but 1000 is a great balance)
            async for message in self.client.iter_messages('me', limit=1000):
                # Check for cancellation
                if getattr(self, "sync_cancelled", False):
                    raise asyncio.CancelledError("Sync was cancelled.")
                    
                # Check if the message contains a document media
                if message.media and hasattr(message.media, 'document'):
                    msg_id = message.id
                    if msg_id not in existing_msg_ids:
                        # Extract file details
                        filename = "Unknown_File"
                        file_size = 0
                        
                        # Extract filename from document attributes
                        for attr in message.media.document.attributes:
                            if hasattr(attr, 'file_name'):
                                filename = attr.file_name
                                break
                                
                        file_size = message.media.document.size
                        # Convert datetime object to string
                        upload_date = message.date.strftime("%Y-%m-%d %H:%M")
                        
                        # Add to our local Virtual File System index (folder_id=None as root)
                        self.db.add_file(filename, file_size, msg_id, upload_date, None)
                        synced_count += 1
                        
            self.after(0, self.transfer_complete, f"Successfully synchronized! Mapped {synced_count} new files from Telegram into SQLite index.")
        except asyncio.CancelledError:
            self.after(0, self.sync_was_cancelled, "Synchronization cancelled by user.")
        except Exception as e:
            if getattr(self, "sync_cancelled", False):
                self.after(0, self.sync_was_cancelled, "Synchronization was cancelled.")
            else:
                self.after(0, self.transfer_failed, f"Synchronization failed: {str(e)}")

    def trigger_upload(self, filepath, folder_id=None):
        filename = os.path.basename(filepath)
        filesize = os.path.getsize(filepath)
        self.add_to_queue("upload", filepath=filepath, filename=filename, filesize=filesize, folder_id=folder_id)

    async def async_upload(self, job):
        filepath = job['filepath']
        filename = job['filename']
        filesize = job['filesize']
        folder_id = job['folder_id']
        
        try:
            self.after(0, self.update_transfer_status, f"Uploading: {filename}", 0.0)
            
            def progress_callback(current, total):
                if job.get('status') == 'Cancelled':
                    raise asyncio.CancelledError("Upload cancelled by user.")
                percent = current / total if total else 0
                job['percent'] = percent
                self.after(0, self.update_transfer_progress, current, total, percent)

            message = await self.client.send_file('me', filepath, progress_callback=progress_callback)
            
            upload_date = datetime.now().strftime("%H:%M")
            self.db.add_file(filename, filesize, message.id, upload_date, folder_id)
            
            self.after(0, self.transfer_complete, f"Uploaded '{filename}' to Saved Messages!")
        except asyncio.CancelledError:
            self.after(0, self.transfer_cancelled, f"Upload of '{filename}' was cancelled.")
        except Exception as e:
            if job.get('status') == 'Cancelled':
                self.after(0, self.transfer_cancelled, f"Upload of '{filename}' was cancelled.")
            else:
                self.after(0, self.transfer_failed, f"Upload failed: {str(e)}")

    def trigger_upload_local(self, job):
        filepath = job['filepath']
        filename = job['filename']
        dest_dir = os.path.join(LOCAL_DRIVE_ROOT, self.current_frame.local_current_rel_path)
        dest_path = os.path.join(dest_dir, filename)
        
        self.update_transfer_status(f"Copying to Local: {filename}", 0.0)
        
        def local_copy_thread():
            try:
                shutil.copy2(filepath, dest_path)
                self.after(0, self.transfer_complete, f"Successfully copied '{filename}' to Local storage!")
            except Exception as e:
                self.after(0, self.transfer_failed, f"Local copy failed: {str(e)}")
                
        threading.Thread(target=local_copy_thread, daemon=True).start()

    def trigger_download(self, file_id, message_id, filename, local_filepath=None):
        dest_dir = self.downloads_dir
        if not dest_dir or not os.path.exists(dest_dir):
            dest_dir = filedialog.askdirectory(title="Select Destination Folder")
            if not dest_dir:
                return
            # Save it as default if none exists!
            self.db.set_status_val("downloads_dir", dest_dir)
            self.downloads_dir = dest_dir
            if isinstance(self.current_frame, DashboardFrame):
                self.show_toast(f"Default downloads folder set to:\n{dest_dir}", kind="info")
            
        self.add_to_queue("download", filename=filename, message_id=message_id, dest_dir=dest_dir, source_path=local_filepath)

    async def async_download(self, job):
        message_id = job['message_id']
        filename = job['filename']
        dest_dir = job['dest_dir']
        dest_path = os.path.join(dest_dir, filename)
        
        try:
            self.after(0, self.update_transfer_status, f"Downloading: {filename}", 0.0)

            messages = await self.client.get_messages('me', ids=[message_id])
            if not messages or not messages[0] or not messages[0].media:
                raise ValueError("Source file message not found on Telegram. It may have been deleted.")
            
            message = messages[0]

            def progress_callback(current, total):
                if job.get('status') == 'Cancelled':
                    raise asyncio.CancelledError("Download cancelled by user.")
                percent = current / total if total else 0
                job['percent'] = percent
                self.after(0, self.update_transfer_progress, current, total, percent)

            await self.client.download_media(message, file=dest_path, progress_callback=progress_callback)
            
            self.after(0, self.transfer_complete, f"Successfully downloaded to:\n{dest_path}")
        except asyncio.CancelledError:
            self.after(0, self.transfer_cancelled, f"Download of '{filename}' was cancelled.")
            # Remove incomplete download file if it was created
            if os.path.exists(dest_path):
                try:
                    os.remove(dest_path)
                except Exception:
                    pass
        except Exception as e:
            if job.get('status') == 'Cancelled':
                self.after(0, self.transfer_cancelled, f"Download of '{filename}' was cancelled.")
                if os.path.exists(dest_path):
                    try:
                        os.remove(dest_path)
                    except Exception:
                        pass
            else:
                self.after(0, self.transfer_failed, f"Download failed: {str(e)}")

    def trigger_download_local(self, job):
        source_path = job['source_path']
        filename = job['filename']
        dest_dir = job['dest_dir']
        
        dest_path = os.path.join(dest_dir, filename)
        self.update_transfer_status(f"Copying: {filename}", 0.0)
        
        def local_copy_thread():
            try:
                shutil.copy2(source_path, dest_path)
                self.after(0, self.transfer_complete, f"Successfully saved to:\n{dest_path}")
            except Exception as e:
                self.after(0, self.transfer_failed, f"Local copy failed: {str(e)}")
                
        threading.Thread(target=local_copy_thread, daemon=True).start()

    def trigger_preview(self, file_id, message_id, filename, local_filepath=None):
        """Previews a file. If local, previews immediately. If cloud, downloads to temp first."""
        temp_dir = os.path.join(LOCAL_DRIVE_ROOT, ".previews")
        os.makedirs(temp_dir, exist_ok=True)
        
        if local_filepath and os.path.exists(local_filepath):
            self.show_preview_window(local_filepath, filename)
        elif self.active_backend == "cloud":
            cached_path = os.path.join(temp_dir, filename)
            if os.path.exists(cached_path):
                self.show_preview_window(cached_path, filename)
            else:
                # Open loading dialog
                dialog = PreviewLoadingDialog(self, filename)
                
                # Progress callback
                def progress_cb(current, total):
                    percent = current / total if total else 0.0
                    def safe_update():
                        if dialog.winfo_exists():
                            try:
                                dialog.progress.set(percent)
                                dialog.percent_lbl.configure(text=f"{int(percent * 100)}%")
                            except Exception:
                                pass
                    self.after(0, safe_update)
                
                # Asynchronous preview download
                async def run_download():
                    try:
                        messages = await self.client.get_messages('me', ids=[message_id])
                        if not messages or not messages[0] or not messages[0].media:
                            raise ValueError("File not found on Telegram cloud.")
                        
                        await self.client.download_media(messages[0], file=cached_path, progress_callback=progress_cb)
                        def safe_success():
                            if dialog.winfo_exists():
                                dialog.destroy()
                            self.show_preview_window(cached_path, filename)
                        self.after(0, safe_success)
                    except Exception as e:
                        def safe_error():
                            if dialog.winfo_exists():
                                dialog.destroy()
                            messagebox.showerror("Preview Failed", f"Failed to download preview: {str(e)}")
                        self.after(0, safe_error)
                
                self.run_async_coro(run_download())
        else:
            messagebox.showerror("Error", "File path not found.")

    def show_preview_window(self, filepath, filename):
        FilePreviewWindow(self, filepath, filename)

    def trigger_rename(self, file_id, current_name, local_filepath=None):
        """Renames a file: updates SQLite (cloud) or renames on disk (local)."""
        dialog = ctk.CTkInputDialog(
            text=f"Enter new name for '{current_name}':",
            title="Rename File"
        )
        new_name = dialog.get_input()
        if not new_name or not new_name.strip() or new_name.strip() == current_name:
            return
        new_name = new_name.strip()
        try:
            if self.active_backend == "cloud" and file_id is not None:
                with self.db.get_conn() as conn:
                    conn.execute("UPDATE files SET filename = ? WHERE id = ?", (new_name, file_id))
                    conn.commit()
            elif local_filepath and os.path.exists(local_filepath):
                new_path = os.path.join(os.path.dirname(local_filepath), new_name)
                os.rename(local_filepath, new_path)
            if isinstance(self.current_frame, DashboardFrame):
                self.current_frame.refresh_files()
            self.show_toast(f"Renamed to '{new_name}'", kind="success")
        except Exception as e:
            self.show_toast(f"Rename failed: {str(e)}", kind="error")

    def trigger_delete(self, file_id, message_id, filename, local_filepath=None):
        if messagebox.askyesno("Confirm Delete", f"Are you sure you want to delete '{filename}'?\nThis will remove it permanently."):
            if self.active_backend == "cloud":
                self.run_async_coro(self.async_delete(file_id, message_id, filename))
            else:
                self.trigger_delete_local(local_filepath, filename)

    async def async_delete(self, file_id, message_id, filename):
        try:
            await self.client.delete_messages('me', [message_id])
            self.db.delete_file(file_id)
            self.after(0, self.on_delete_success, f"Deleted '{filename}' successfully from Cloud.")
        except Exception as e:
            self.after(0, self.on_delete_failure, f"Failed to delete: {str(e)}")

    def trigger_delete_local(self, filepath, filename):
        try:
            if os.path.exists(filepath):
                os.remove(filepath)
            self.on_delete_success(f"Deleted '{filename}' successfully from Local Storage.")
        except Exception as e:
            self.on_delete_failure(f"Failed to delete: {str(e)}")

    async def async_multi_delete(self, items):
        """Asynchronously deletes a batch of cloud files from Telegram and database."""
        try:
            message_ids = [item["telegram_message_id"] for item in items]
            
            # Delete from Telegram in batches of 100
            if message_ids:
                for i in range(0, len(message_ids), 100):
                    batch = message_ids[i:i+100]
                    await self.client.delete_messages('me', batch)
                    
            # Delete from SQLite VFS
            for item in items:
                self.db.delete_file(item["id"])
                
            # Reload Dashboard
            me = await self.client.get_me()
            user_info = {
                "name": f"{me.first_name or ''} {me.last_name or ''}".strip(),
                "username": f"@{me.username}" if me.username else "Saved Messages"
            }
            self.after(0, self.show_dashboard_frame, user_info)
            self.after(0, lambda: messagebox.showinfo("Success", f"Deleted {len(items)} files successfully from Cloud!"))
        except Exception as e:
            me = await self.client.get_me()
            user_info = {
                "name": f"{me.first_name or ''} {me.last_name or ''}".strip(),
                "username": f"@{me.username}" if me.username else "Saved Messages"
            }
            self.after(0, self.show_dashboard_frame, user_info)
            self.after(0, lambda: messagebox.showerror("Deletion Failed", f"Failed to delete files: {str(e)}"))

    def trigger_multi_delete_local(self, items):
        """Physically deletes a batch of local files from disk."""
        deleted_count = 0
        failed_count = 0
        for item in items:
            filepath = item["local_path"]
            try:
                if os.path.exists(filepath):
                    os.remove(filepath)
                deleted_count += 1
            except Exception:
                failed_count += 1
                
        # Reload dashboard
        if isinstance(self.current_frame, DashboardFrame):
            self.current_frame.selected_files.clear()
            self.current_frame.update_selection_bar()
            self.current_frame.refresh_files()
            
        if failed_count > 0:
            messagebox.showwarning("Multi-Delete", f"Deleted {deleted_count} files. Failed to delete {failed_count} files.")
        else:
            messagebox.showinfo("Success", f"Deleted all {deleted_count} files successfully from Local Storage!")

    def trigger_delete_folder(self, folder_id, folder_name, local_filepath=None):
        if self.active_backend == "cloud":
            if messagebox.askyesno("Confirm Folder Delete", f"Are you sure you want to delete folder '{folder_name}'?\nThis will recursively delete all nested subfolders and files from Telegram Saved Messages and the database index!"):
                self.show_loading(f"Deleting folder '{folder_name}' recursively...")
                self.run_async_coro(self.async_delete_folder(folder_id, folder_name))
        else:
            if messagebox.askyesno("Confirm Local Folder Delete", f"Are you sure you want to delete local folder '{folder_name}'?\nThis will physically delete the folder and all its contents recursively from disk!"):
                self.trigger_delete_folder_local(local_filepath, folder_name)

    async def async_delete_folder(self, folder_id, folder_name):
        try:
            files_to_delete = self.db.get_files_in_folder_recursive(folder_id)
            message_ids = [f["telegram_message_id"] for f in files_to_delete]
            
            if message_ids:
                for i in range(0, len(message_ids), 100):
                    batch = message_ids[i:i+100]
                    await self.client.delete_messages('me', batch)
            
            self.db.delete_folder_recursive(folder_id)
            
            me = await self.client.get_me()
            user_info = {
                "name": f"{me.first_name or ''} {me.last_name or ''}".strip(),
                "username": f"@{me.username}" if me.username else "Saved Messages"
            }
            self.after(0, self.show_dashboard_frame, user_info)
            self.after(0, lambda: messagebox.showinfo("Success", f"Folder '{folder_name}' and all its files deleted recursively!"))
        except Exception as e:
            me = await self.client.get_me()
            user_info = {
                "name": f"{me.first_name or ''} {me.last_name or ''}".strip(),
                "username": f"@{me.username}" if me.username else "Saved Messages"
            }
            self.after(0, self.show_dashboard_frame, user_info)
            self.after(0, lambda: messagebox.showerror("Deletion Failed", f"Failed to delete folder: {str(e)}"))

    def trigger_delete_folder_local(self, filepath, folder_name):
        try:
            if os.path.exists(filepath):
                shutil.rmtree(filepath)
            
            if isinstance(self.current_frame, DashboardFrame):
                self.current_frame.refresh_files()
            messagebox.showinfo("Success", f"Deleted local folder '{folder_name}' recursively.")
        except Exception as e:
            messagebox.showerror("Error", f"Failed to delete folder recursively: {str(e)}")

# ==========================================
# CUSTOM MODAL DIALOGS
# ==========================================
class PreviewLoadingDialog(ctk.CTkToplevel):
    def __init__(self, parent, filename):
        super().__init__(parent)
        self.title("Downloading Preview")
        self.geometry("380x150")
        self.resizable(False, False)
        self.configure(fg_color="#17212b")
        
        self.transient(parent)
        self.grab_set()
        
        # Center window
        self.update_idletasks()
        parent_x = parent.winfo_x()
        parent_y = parent.winfo_y()
        parent_w = parent.winfo_width()
        parent_h = parent.winfo_height()
        x = parent_x + (parent_w - 380) // 2
        y = parent_y + (parent_h - 150) // 2
        self.geometry(f"+{x}+{y}")
        
        lbl = ctk.CTkLabel(self, text=f"Downloading preview for:\n{filename}", text_color="#ffffff", font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"))
        lbl.pack(pady=(20, 10))
        
        self.progress = ctk.CTkProgressBar(self, width=280, height=6, fg_color="#242f3d", progress_color="#2481cc")
        self.progress.set(0.0)
        self.progress.pack(pady=10)
        
        self.percent_lbl = ctk.CTkLabel(self, text="0%", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=10))
        self.percent_lbl.pack()

class FilePreviewWindow(ctk.CTkToplevel):
    def __init__(self, parent, filepath, filename):
        super().__init__(parent)
        self.title(f"Preview - {filename}")
        self.geometry("700x550")
        self.minsize(500, 400)
        self.configure(fg_color="#0e1621")
        
        self.transient(parent)
        self.grab_set()
        self.focus()
        
        # Center relative to parent
        self.update_idletasks()
        parent_x = parent.winfo_x()
        parent_y = parent.winfo_y()
        parent_w = parent.winfo_width()
        parent_h = parent.winfo_height()
        x = parent_x + (parent_w - 700) // 2
        y = parent_y + (parent_h - 550) // 2
        self.geometry(f"+{x}+{y}")
        
        # Header bar
        header = ctk.CTkFrame(self, fg_color="#17212b", height=50, corner_radius=0)
        header.pack(fill="x")
        header.pack_propagate(False)
        
        lbl_title = ctk.CTkLabel(header, text=filename, text_color="#ffffff", font=ctk.CTkFont(family="Segoe UI", size=14, weight="bold"))
        lbl_title.pack(side="left", padx=15)
        
        btn_close = ctk.CTkButton(
            header, text="Close", command=self.destroy,
            width=80, height=30, corner_radius=6, fg_color="#ff4d4d", hover_color="#cc3333", text_color="#ffffff"
        )
        btn_close.pack(side="right", padx=15)
        
        # Content frame
        self.content_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.content_frame.pack(fill="both", expand=True, padx=15, pady=15)
        
        self.load_preview(filepath, filename)
        
    def load_preview(self, filepath, filename):
        ext = os.path.splitext(filename)[1].lower().strip(".")
        img_exts = {"png", "jpg", "jpeg", "gif", "bmp"}
        txt_exts = {"txt", "py", "json", "csv", "log", "ini", "md", "html", "css", "js", "xml", "yaml", "yml", "sh", "bat"}
        
        if ext in img_exts:
            # Load Image
            from PIL import Image
            try:
                pil_img = Image.open(filepath)
                # Resize to fit nicely in 650x450 while keeping aspect ratio
                max_w, max_h = 650, 450
                w, h = pil_img.size
                ratio = min(max_w / w, max_h / h)
                new_w, new_h = int(w * ratio), int(h * ratio)
                
                ctk_img = ctk.CTkImage(light_image=pil_img, dark_image=pil_img, size=(new_w, new_h))
                lbl_img = ctk.CTkLabel(self.content_frame, image=ctk_img, text="")
                lbl_img.pack(expand=True)
            except Exception as e:
                self.show_error(f"Failed to load image:\n{str(e)}")
                
        elif ext in txt_exts:
            # Load Text
            try:
                with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
                    content = f.read(50000) # Load up to 50KB for preview
                    
                txt_area = ctk.CTkTextbox(self.content_frame, fg_color="#17212b", border_color="#242f3d", text_color="#ffffff", font=ctk.CTkFont(family="Consolas", size=12))
                txt_area.insert("1.0", content)
                txt_area.configure(state="disabled")
                txt_area.pack(fill="both", expand=True)
            except Exception as e:
                self.show_error(f"Failed to read text file:\n{str(e)}")
        else:
            # Fallback: Open with system default application on Windows
            try:
                os.startfile(filepath)
                self.destroy()
            except Exception as e:
                self.show_error(f"Unsupported preview format.\nSystem launch failed: {str(e)}")
                
    def show_error(self, message):
        lbl = ctk.CTkLabel(self.content_frame, text=message, text_color="red", font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"))
        lbl.pack(expand=True)

class HelpDialog(ctk.CTkToplevel):
    """A beautiful modal popup that explains to the user how to obtain their Telegram API credentials."""
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Getting Started")
        self.geometry("500x640")
        self.resizable(False, False)
        
        self.transient(parent)
        self.grab_set()
        self.focus()
        
        self.configure(fg_color="#17212b")
        
        # Center relative to parent window
        self.update_idletasks()
        parent_x = parent.winfo_x()
        parent_y = parent.winfo_y()
        parent_w = parent.winfo_width()
        parent_h = parent.winfo_height()
        
        x = parent_x + (parent_w - 500) // 2
        y = parent_y + (parent_h - 640) // 2
        self.geometry(f"+{x}+{y}")
        
        # Main padding container
        container = ctk.CTkFrame(self, fg_color="transparent")
        container.pack(fill="both", expand=True, padx=25, pady=25)
        
        # Header (Title + Close X Action)
        header_frame = ctk.CTkFrame(container, fg_color="transparent", height=40)
        header_frame.pack(fill="x", pady=(0, 15))
        header_frame.pack_propagate(False)
        
        lbl_title = ctk.CTkLabel(
            header_frame, text="Getting Started", text_color="#ffffff", 
            font=ctk.CTkFont(family="Segoe UI", size=20, weight="bold")
        )
        lbl_title.pack(side="left")
        
        btn_close = ctk.CTkButton(
            header_frame, text="✕", command=self.destroy,
            width=28, height=28, corner_radius=14, fg_color="transparent", 
            text_color="#808080", hover_color="#262626", font=ctk.CTkFont(family="Segoe UI", size=14, weight="bold")
        )
        btn_close.pack(side="right")
        
        # Top Yellow/Orange Warning/Info Card
        top_card = ctk.CTkFrame(container, fg_color="#2b2214", border_color="#e6a100", border_width=1, corner_radius=10)
        top_card.pack(fill="x", pady=(0, 20))
        
        lbl_top_desc = ctk.CTkLabel(
            top_card, 
            text="Telegram Drive uses your Telegram account as secure cloud storage.\nYou'll need a Telegram account and API credentials to get started.",
            text_color="#e6a100", font=ctk.CTkFont(family="Segoe UI", size=12),
            justify="left", wraplength=420
        )
        lbl_top_desc.pack(padx=15, pady=12, fill="x")
        
        # Steps container
        steps_frame = ctk.CTkFrame(container, fg_color="transparent")
        steps_frame.pack(fill="both", expand=True, pady=(0, 15))
        
        self.add_step(
            steps_frame, "1", "Go to Telegram's Developer Portal",
            "Visit my.telegram.org and log in with your phone number."
        )
        self.add_step(
            steps_frame, "2", "Create a New Application",
            "Click on \"API development tools\" and create a new application.\nUse any name and description you like."
        )
        self.add_step(
            steps_frame, "3", "Copy Your Credentials",
            "After creating the app, you'll see your API ID (a number) and API Hash\n(a string). Copy both and paste them into the fields on the previous screen."
        )
        
        # Privacy Notice Card
        privacy_card = ctk.CTkFrame(container, fg_color="#181818", border_color="#2b2b2b", border_width=1, corner_radius=10)
        privacy_card.pack(fill="x", pady=(0, 20))
        
        lbl_privacy = ctk.CTkLabel(
            privacy_card, 
            text="🔒 Privacy: Your credentials are stored locally on your device and are never\nsent to any third-party servers. All data goes directly between you and Telegram.",
            text_color="#808080", font=ctk.CTkFont(family="Segoe UI", size=11),
            justify="left", wraplength=420
        )
        lbl_privacy.pack(padx=15, pady=10, fill="x")
        
        # Large Golden Button
        self.btn_open = ctk.CTkButton(
            container, text="🔗  Open my.telegram.org", command=self.open_link,
            height=44, corner_radius=8, font=ctk.CTkFont(family="Segoe UI", size=14, weight="bold"),
            fg_color="#e6a100", hover_color="#cc8f00", text_color="#121212"
        )
        self.btn_open.pack(fill="x")

    def add_step(self, parent, number, title, body):
        row = ctk.CTkFrame(parent, fg_color="transparent")
        row.pack(fill="x", pady=10)
        
        badge = ctk.CTkLabel(
            row, text=number, corner_radius=12, fg_color="#e6a100", 
            text_color="#121212", width=24, height=24, 
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold")
        )
        badge.pack(side="left", anchor="n", padx=(0, 15))
        badge.pack_propagate(False)
        
        text_col = ctk.CTkFrame(row, fg_color="transparent")
        text_col.pack(side="left", fill="both", expand=True)
        
        lbl_title = ctk.CTkLabel(
            text_col, text=title, text_color="#ffffff",
            font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"), anchor="w"
        )
        lbl_title.pack(fill="x")
        
        lbl_body = ctk.CTkLabel(
            text_col, text=body, text_color="#a0a0a0",
            font=ctk.CTkFont(family="Segoe UI", size=11), justify="left", anchor="w"
        )
        lbl_body.pack(fill="x", pady=(2, 0))

    def open_link(self):
        webbrowser.open("https://my.telegram.org")

# ==========================================
# VIEW 1: LOADING SCREEN FRAME
# ==========================================
class LoadingFrame(ctk.CTkFrame):
    def __init__(self, master, message="Loading..."):
        super().__init__(master, fg_color="#0e1621")
        
        self.label = ctk.CTkLabel(
            self, text="Saved Messages Drive", text_color="#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=26, weight="bold")
        )
        self.label.pack(pady=(180, 10))
        
        self.status = ctk.CTkLabel(
            self, text=message, text_color="#7f91a4", 
            font=ctk.CTkFont(family="Segoe UI", size=13)
        )
        self.status.pack(pady=10)
        
        self.progressbar = ctk.CTkProgressBar(self, width=280, height=8, indeterminate_speed=1.5, fg_color="#182533", progress_color="#2481cc")
        self.progressbar.pack(pady=20)
        self.progressbar.start()

# ==========================================
# VIEW 2: AUTH LOGIN SCREEN FRAME
# ==========================================
class LoginFrame(ctk.CTkFrame):
    def __init__(self, master, error_msg=""):
        super().__init__(master, fg_color="#0e1621")
        self.master = master
        
        card = ctk.CTkFrame(self, fg_color="#17212b", corner_radius=16, border_color="#101921", border_width=1)
        card.pack(pady=40, padx=20, expand=True)
        
        lbl_title = ctk.CTkLabel(
            card, text="Telegram Cloud Storage", text_color="#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=22, weight="bold")
        )
        lbl_title.pack(pady=(25, 5), padx=40)
        
        lbl_desc = ctk.CTkLabel(
            card, text="Enter API ID & API Hash to link secure storage.", 
            text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        lbl_desc.pack(pady=(0, 15))
        
        self.show_credentials_fields = (API_ID == 0 or API_HASH == "")
        
        if self.show_credentials_fields:
            self.lbl_api_id = ctk.CTkLabel(card, text="Telegram API ID", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=12))
            self.lbl_api_id.pack(anchor="w", padx=40, pady=(3, 2))
            
            saved_id = self.master.db.get_status_val("api_id", "")
            self.entry_api_id = ctk.CTkEntry(card, placeholder_text="e.g. 123456", width=320, height=36, corner_radius=8, fg_color="#242f3d", border_color="#101921")
            self.entry_api_id.insert(0, saved_id)
            self.entry_api_id.pack(padx=40, pady=(0, 8))
            
            self.lbl_api_hash = ctk.CTkLabel(card, text="Telegram API HASH", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=12))
            self.lbl_api_hash.pack(anchor="w", padx=40, pady=(3, 2))
            
            saved_hash = self.master.db.get_status_val("api_hash", "")
            self.entry_api_hash = ctk.CTkEntry(card, placeholder_text="e.g. 7ba45fa8f090...", width=320, height=36, corner_radius=8, fg_color="#242f3d", border_color="#101921")
            self.entry_api_hash.insert(0, saved_hash)
            self.entry_api_hash.pack(padx=40, pady=(0, 8))
        
        self.lbl_phone = ctk.CTkLabel(card, text="Phone Number (International)", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=12))
        self.lbl_phone.pack(anchor="w", padx=40, pady=(3, 2))
        
        self.entry_phone = ctk.CTkEntry(card, placeholder_text="e.g. +1234567890", width=320, height=36, corner_radius=8, fg_color="#242f3d", border_color="#101921")
        self.entry_phone.pack(padx=40, pady=(0, 15))
        
        self.lbl_status = ctk.CTkLabel(
            card, text=error_msg, text_color="red" if "error" in error_msg.lower() or "fail" in error_msg.lower() else "#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=11), wraplength=300
        )
        self.lbl_status.pack(pady=(0, 10))
        
        # Action Buttons
        self.btn_submit = ctk.CTkButton(
            card, text="Send Verification Code", command=self.on_submit, 
            width=320, height=40, corner_radius=8, font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"),
            fg_color="#2481cc", hover_color="#2b5278"
        )
        self.btn_submit.pack(pady=(10, 5), padx=40)
        
        # Orange Help Link
        self.btn_help = ctk.CTkButton(
            card, text="ℹ️ How to get API ID / API Hash?", command=self.on_help_click, 
            width=320, height=36, corner_radius=8, font=ctk.CTkFont(family="Segoe UI", size=12),
            fg_color="transparent", text_color="#e6a100", hover_color="#26221a"
        )
        self.btn_help.pack(pady=(0, 5), padx=40)
        
        # QR Code Login Link
        self.btn_qr = ctk.CTkButton(
            card, text="Scan QR Code to Login Direct", command=self.on_qr_click, 
            width=320, height=36, corner_radius=8, font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            fg_color="transparent", text_color="#2481cc", hover_color="#202b36"
        )
        self.btn_qr.pack(pady=(0, 20), padx=40)

    def get_credentials(self):
        if self.show_credentials_fields:
            api_id_str = self.entry_api_id.get().strip()
            api_hash = self.entry_api_hash.get().strip()
            
            if not api_id_str or not api_hash:
                self.lbl_status.configure(text="API ID and API Hash are required first.", text_color="red")
                return None, None
                
            if not api_id_str.isdigit():
                self.lbl_status.configure(text="API ID must be a numeric integer value.", text_color="red")
                return None, None
            return int(api_id_str), api_hash
        else:
            return API_ID, API_HASH

    def on_submit(self):
        api_id, api_hash = self.get_credentials()
        if api_id is None:
            return
            
        phone = self.entry_phone.get().strip()
        if not phone:
            self.lbl_status.configure(text="Phone Number must be filled in.", text_color="red")
            return
            
        self.master.request_otp(api_id, api_hash, phone)

    def on_qr_click(self):
        api_id, api_hash = self.get_credentials()
        if api_id is None:
            return
            
        self.master.request_qr_login(api_id, api_hash)

    def on_help_click(self):
        self.master.show_help_dialog()

# ==========================================
# VIEW 3: OTP & 2FA VERIFICATION FRAME
# ==========================================
class OTPFrame(ctk.CTkFrame):
    def __init__(self, master, phone_number, error_msg=""):
        super().__init__(master, fg_color="#0e1621")
        self.master = master
        
        card = ctk.CTkFrame(self, fg_color="#17212b", corner_radius=16, border_color="#101921", border_width=1)
        card.pack(pady=80, padx=20, expand=True)
        
        lbl_title = ctk.CTkLabel(
            card, text="Security Verification", text_color="#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=22, weight="bold")
        )
        lbl_title.pack(pady=(25, 5), padx=40)
        
        self.lbl_subtitle = ctk.CTkLabel(
            card, text=f"Sent code to your Telegram app: {phone_number}", 
            text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        self.lbl_subtitle.pack(pady=(0, 20))
        
        self.lbl_otp = ctk.CTkLabel(card, text="OTP Code", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=12))
        self.lbl_otp.pack(anchor="w", padx=40, pady=(5, 2))
        
        self.entry_otp = ctk.CTkEntry(card, placeholder_text="Enter Code", width=320, height=36, corner_radius=8, fg_color="#242f3d", border_color="#101921")
        self.entry_otp.pack(padx=40, pady=(0, 15))
        
        self.lbl_pass = ctk.CTkLabel(card, text="Two-Factor Password (Optional)", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=12))
        self.entry_pass = ctk.CTkEntry(card, placeholder_text="Enter 2FA Password", show="*", width=320, height=36, corner_radius=8, fg_color="#242f3d", border_color="#101921")
        
        self.lbl_status = ctk.CTkLabel(
            card, text=error_msg, text_color="red" if error_msg else "#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=11), wraplength=300
        )
        self.lbl_status.pack(pady=(0, 10))
        
        self.btn_submit = ctk.CTkButton(
            card, text="Verify Code", command=self.on_verify, 
            width=320, height=40, corner_radius=8, font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"),
            fg_color="#2481cc", hover_color="#2b5278"
        )
        self.btn_submit.pack(pady=(10, 5), padx=40)
        
        self.btn_back = ctk.CTkButton(
            card, text="Back to Login", command=lambda: self.master.show_login_frame(), 
            width=320, height=36, corner_radius=8, font=ctk.CTkFont(family="Segoe UI", size=12),
            fg_color="transparent", text_color="#7f91a4", hover_color="#202b36"
        )
        self.btn_back.pack(pady=(0, 20), padx=40)

    def enable_2fa_field(self):
        self.lbl_pass.pack(anchor="w", padx=40, pady=(5, 2), before=self.lbl_status)
        self.entry_pass.pack(padx=40, pady=(0, 15), before=self.lbl_status)
        self.btn_submit.configure(text="Verify with 2FA Password")

    def set_status(self, text, color="orange"):
        self.lbl_status.configure(text=text, text_color=color)

    def on_verify(self):
        code = self.entry_otp.get().strip()
        pwd = self.entry_pass.get().strip()
        
        if not code and not pwd:
            self.set_status("Please provide the verification code.", "red")
            return
            
        self.master.verify_otp(code, pwd)

# ==========================================
# VIEW 5: SCANNED QR CODE VERIFICATION FRAME
# ==========================================
class QRLoginFrame(ctk.CTkFrame):
    def __init__(self, master):
        super().__init__(master, fg_color="#0e1621")
        self.master = master
        
        card = ctk.CTkFrame(self, fg_color="#17212b", corner_radius=16, border_color="#101921", border_width=1)
        card.pack(pady=40, padx=20, expand=True)
        
        lbl_title = ctk.CTkLabel(
            card, text="Login with QR Code", text_color="#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=22, weight="bold")
        )
        lbl_title.pack(pady=(25, 5), padx=40)
        
        lbl_desc = ctk.CTkLabel(
            card, text="Scan with Telegram on your phone to link device.", 
            text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        lbl_desc.pack(pady=(0, 15))
        
        # QR Image Display Canvas
        self.qr_container = ctk.CTkFrame(card, width=260, height=260, fg_color="#242f3d", corner_radius=10, border_color="#101921", border_width=1)
        self.qr_container.pack(pady=10)
        self.qr_container.pack_propagate(False)
        
        self.status_lbl = ctk.CTkLabel(self.qr_container, text="Generating QR Token...", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=12))
        self.status_lbl.pack(expand=True)
        
        # Walkthrough steps
        instructions = (
            "1. Open Telegram on your phone\n"
            "2. Go to Settings › Devices › Link Desktop Device\n"
            "3. Scan the QR code above"
        )
        self.lbl_inst = ctk.CTkLabel(
            card, text=instructions, text_color="#7f91a4", 
            font=ctk.CTkFont(family="Segoe UI", size=11), justify="left"
        )
        self.lbl_inst.pack(pady=15)
        
        self.btn_back = ctk.CTkButton(
            card, text="Back to Phone Login", command=lambda: self.master.show_login_frame(), 
            width=260, height=36, corner_radius=8, font=ctk.CTkFont(family="Segoe UI", size=12),
            fg_color="transparent", text_color="#7f91a4", hover_color="#202b36"
        )
        self.btn_back.pack(pady=(0, 20), padx=40)

    def display_qr(self, pil_image):
        """Replaces loading message with fetched dynamic QR code image."""
        for child in self.qr_container.winfo_children():
            child.destroy()
        
        ctk_img = ctk.CTkImage(light_image=pil_image, dark_image=pil_image, size=(250, 250))
        self.img_lbl = ctk.CTkLabel(self.qr_container, image=ctk_img, text="")
        self.img_lbl.pack(expand=True)
        
        self.lbl_inst.configure(text="QR Code loaded successfully!\nPoint your phone's link scanner at the screen.", text_color="#2481cc")

    def show_error(self, error_msg):
        """Displays verification/connection errors safely in the container with a retry action."""
        for child in self.qr_container.winfo_children():
            child.destroy()
            
        err_lbl = ctk.CTkLabel(
            self.qr_container, text=f"Error:\n{error_msg}", 
            text_color="red", font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            wraplength=220
        )
        err_lbl.pack(pady=(40, 10))
        
        btn_retry = ctk.CTkButton(
            self.qr_container, text="🔄 Regenerate QR", command=self.on_retry_click,
            width=140, height=32, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            fg_color="#2481cc", hover_color="#2b5278"
        )
        btn_retry.pack(pady=10)
        
        self.lbl_inst.configure(text="Click 'Regenerate QR' above to request a new secure linking token.", text_color="red")

    def on_retry_click(self):
        """Clears old states and dispatches a fresh QR generation request."""
        for child in self.qr_container.winfo_children():
            child.destroy()
            
        self.status_lbl = ctk.CTkLabel(self.qr_container, text="Generating QR Token...", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=12))
        self.status_lbl.pack(expand=True)
        
        self.lbl_inst.configure(
            text="1. Open Telegram on your phone\n2. Go to Settings › Devices › Link Desktop Device\n3. Point your camera at the QR code above", 
            text_color="#7f91a4"
        )
        
        saved_id = self.master.db.get_status_val("api_id")
        saved_hash = self.master.db.get_status_val("api_hash")
        if saved_id and saved_hash:
            self.master.request_qr_login(int(saved_id), saved_hash)

# ==========================================
# VIEW 4: MAIN DASHBOARD FRAME
# ==========================================
class DashboardFrame(ctk.CTkFrame):
    def __init__(self, master, user_info):
        super().__init__(master, fg_color="#0e1621")
        self.master = master
        
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)
        
        # Navigation pointers
        self.current_folder_id = None       # Cloud hierarchical state
        self.local_current_rel_path = ""    # Local relative subdirectory state
        self.active_filter = "all"          # Dynamic file type category filter
        self.file_progress_bars = {}        # Dynamic live file progress bars
        
        # ----------------------------------------------------
        # SIDEBAR PANEL (Left) - Styled like Telegram Desktop Chat List
        # ----------------------------------------------------
        sidebar = ctk.CTkFrame(self, width=280, fg_color="#17212b", corner_radius=0)
        sidebar.grid(row=0, column=0, sticky="nsew")
        sidebar.grid_propagate(False)
        
        # Custom Logo Branding
        lbl_logo = ctk.CTkLabel(
            sidebar, text="Telegram Desktop Storage", text_color="#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=16, weight="bold")
        )
        lbl_logo.pack(pady=(15, 2))
        
        lbl_mode = ctk.CTkLabel(
            sidebar, text="online", text_color="#5288c1", 
            font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        lbl_mode.pack(pady=(0, 10))
        
        # Divider Line
        div = ctk.CTkFrame(sidebar, height=1, fg_color="#101921")
        div.pack(fill="x", pady=2)
        
        # CHATS LIST CONTAINER (Toggles backends natively)
        chats_container = ctk.CTkFrame(sidebar, fg_color="transparent")
        chats_container.pack(fill="x", pady=10)
        
        # Saved Messages chat row (Cloud Backend)
        self.chat_cloud = ChatRow(
            chats_container, "Saved Messages", "Cloud Storage Backend", 
            "🔖", "#2481cc", "#2b5278", self.select_cloud
        )
        
        # Local Disk chat row (Local Backend)
        self.chat_local = ChatRow(
            chats_container, "Local Disk Explorer", "Local Directory Backend", 
            "📁", "#5288c1", "#2b5278", self.select_local
        )
        
        # Highlight Cloud by default
        self.chat_cloud.set_active(True)
        
        # Divider
        div2 = ctk.CTkFrame(sidebar, height=1, fg_color="#101921")
        div2.pack(fill="x", pady=2)
        
        # Storage Stats Card
        self.storage_card = ctk.CTkFrame(sidebar, fg_color="#101921", corner_radius=10, border_color="#242f3d", border_width=1)
        self.storage_card.pack(fill="x", padx=15, pady=10)
        
        self.lbl_storage_title = ctk.CTkLabel(
            self.storage_card, text="Cloud Storage stats", text_color="#ffffff", 
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold")
        )
        self.lbl_storage_title.pack(anchor="w", padx=15, pady=(10, 2))
        
        self.lbl_usage_text = ctk.CTkLabel(
            self.storage_card, text="0 files • 0 MB", text_color="#7f91a4", 
            font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        self.lbl_usage_text.pack(anchor="w", padx=15, pady=(0, 8))
        
        self.storage_progress = ctk.CTkProgressBar(self.storage_card, height=5, fg_color="#242f3d", progress_color="#2481cc")
        self.storage_progress.set(0.0)
        self.storage_progress.pack(fill="x", padx=15, pady=2)
        
        self.lbl_usage_cap = ctk.CTkLabel(
            self.storage_card, text="0 bytes of Unlimited space", text_color="#7f91a4", 
            font=ctk.CTkFont(family="Segoe UI", size=10)
        )
        self.lbl_usage_cap.pack(anchor="w", padx=15, pady=(2, 10))
        
        # Transfer Stats Container
        transfer_card = ctk.CTkFrame(sidebar, fg_color="#101921", corner_radius=10, border_color="#242f3d", border_width=1)
        transfer_card.pack(fill="x", padx=15, pady=5)
        
        self.progress_label = ctk.CTkLabel(
            transfer_card, text="Transfer: Idle", text_color="#ffffff", 
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold")
        )
        self.progress_label.pack(anchor="w", padx=15, pady=(10, 5))
        
        self.progress_bar = ctk.CTkProgressBar(transfer_card, height=5, fg_color="#242f3d", progress_color="#2bca4f")
        self.progress_bar.set(0.0)
        self.progress_bar.pack(fill="x", padx=15, pady=5)
        
        stats_frame = ctk.CTkFrame(transfer_card, fg_color="transparent")
        stats_frame.pack(fill="x", padx=15, pady=(2, 10))
        
        self.progress_size_label = ctk.CTkLabel(
            stats_frame, text="0.0 MB / 0.0 MB", text_color="#7f91a4", 
            font=ctk.CTkFont(family="Segoe UI", size=10)
        )
        self.progress_size_label.pack(side="left")
        
        self.progress_percent = ctk.CTkLabel(
            stats_frame, text="0%", text_color="#2481cc", 
            font=ctk.CTkFont(family="Segoe UI", size=10, weight="bold")
        )
        self.progress_percent.pack(side="right")
        
        # Active cancellation button for both sync and queue transfers
        self.btn_cancel_active = ctk.CTkButton(
            transfer_card, text="✕ Cancel Transfer", command=self.on_cancel_active_click,
            height=24, corner_radius=5, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            fg_color="#ff4d4d", hover_color="#cc3333", text_color="#ffffff"
        )
        self.btn_cancel_active.pack_propagate(False)
        
        # Profile Details / Logout (placed in sidebar footer)
        self.profile_card = ctk.CTkFrame(sidebar, fg_color="#1e2936", corner_radius=10)
        self.profile_card.pack(fill="x", padx=15, pady=(10, 5), side="bottom")
        
        self.lbl_user = ctk.CTkLabel(
            self.profile_card, text=user_info["name"], text_color="#ffffff", 
            font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold")
        )
        self.lbl_user.pack(anchor="w", padx=15, pady=(10, 2))
        
        self.lbl_username = ctk.CTkLabel(
            self.profile_card, text=user_info["username"], text_color="#5288c1", 
            font=ctk.CTkFont(family="Segoe UI", size=11)
        )
        self.lbl_username.pack(anchor="w", padx=15, pady=(0, 6))
        
        self.btn_backup = ctk.CTkButton(
            self.profile_card, text="⚙️  Set Auto-Backup Folder", command=self.on_backup_click, 
            height=26, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11),
            fg_color="transparent", text_color="#e6a100", hover_color="#2b2214"
        )
        self.btn_backup.pack(fill="x", padx=10, pady=(2, 2))

        self.btn_downloads = ctk.CTkButton(
            self.profile_card, text="📥  Set Downloads Folder", command=self.on_downloads_click, 
            height=26, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11),
            fg_color="transparent", text_color="#2481cc", hover_color="#202b36"
        )
        self.btn_downloads.pack(fill="x", padx=10, pady=(2, 2))

        self.btn_logout = ctk.CTkButton(
            self.profile_card, text="Disconnect Account", command=self.master.perform_logout, 
            height=26, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11),
            fg_color="transparent", text_color="#ff4d4d", hover_color="#2b1a1a"
        )
        self.btn_logout.pack(fill="x", padx=10, pady=(2, 10))
        
        # ----------------------------------------------------
        # MAIN CONTENT EXPLORER PANEL (Right)
        # ----------------------------------------------------
        main_content = ctk.CTkFrame(self, fg_color="transparent")
        main_content.grid(row=0, column=1, sticky="nsew", padx=15, pady=15)
        
        main_content.grid_columnconfigure(0, weight=1)
        main_content.grid_rowconfigure(2, weight=1)
        
        # 1. TOP HEADER BAR (Chat top info toolbar in Telegram Desktop)
        header_bar = ctk.CTkFrame(main_content, fg_color="#17212b", height=62, corner_radius=12, border_color="#101921", border_width=1)
        header_bar.grid(row=0, column=0, sticky="ew", pady=(5, 8))
        header_bar.pack_propagate(False)
        
        # Chat title & state text on left of header
        chat_info_frame = ctk.CTkFrame(header_bar, fg_color="transparent")
        chat_info_frame.pack(side="left", padx=18, pady=10)
        
        self.lbl_chat_title = ctk.CTkLabel(
            chat_info_frame, text="Saved Messages", text_color="#ffffff",
            font=ctk.CTkFont(family="Segoe UI", size=15, weight="bold"), anchor="w"
        )
        self.lbl_chat_title.pack(fill="x")
        
        # Subtitle frame containing glowing pulsing online dot
        self.sub_frame = ctk.CTkFrame(chat_info_frame, fg_color="transparent")
        self.sub_frame.pack(fill="x")
        
        self.lbl_status_dot = ctk.CTkLabel(
            self.sub_frame, text="●", text_color="#2bca4f",
            font=ctk.CTkFont(family="Segoe UI", size=10), anchor="w"
        )
        self.lbl_status_dot.pack(side="left", padx=(0, 4))
        
        self.lbl_chat_subtitle = ctk.CTkLabel(
            self.sub_frame, text="online • unlimited cloud storage", text_color="#5288c1",
            font=ctk.CTkFont(family="Segoe UI", size=11), anchor="w"
        )
        self.lbl_chat_subtitle.pack(side="left")
        
        # Trigger the pulsing status indicator loop
        self.pulse_status()
        
        # Header action buttons on right of header
        header_actions = ctk.CTkFrame(header_bar, fg_color="transparent")
        header_actions.pack(side="right", padx=18)
        
        self.btn_upload = ctk.CTkButton(
            header_actions, text="⬆️  Upload File", command=self.on_upload_click, 
            width=110, height=34, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            fg_color="#2481cc", hover_color="#2b5278"
        )
        self.btn_upload.pack(side="left", padx=4)
        
        self.btn_new_folder = ctk.CTkButton(
            header_actions, text="📁  New Folder", command=self.on_new_folder_click, 
            width=110, height=34, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            fg_color="#242f3d", hover_color="#313e4f", border_color="#101921", border_width=1, text_color="#2481cc"
        )
        self.btn_new_folder.pack(side="left", padx=4)
        
        # Authentic Telegram cloud synchronization action button
        self.btn_sync = ctk.CTkButton(
            header_actions, text="🔄  Sync Cloud", command=self.on_sync_click, 
            width=110, height=34, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold"),
            fg_color="#2bca4f", hover_color="#249e3e", text_color="#ffffff"
        )
        self.btn_sync.pack(side="left", padx=4)
        
        # 2. SUB HEADER ROW (Search bar + breadcrumbs path)
        sub_header = ctk.CTkFrame(main_content, fg_color="transparent")
        sub_header.grid(row=1, column=0, sticky="ew", pady=(0, 8))
        sub_header.grid_columnconfigure(0, weight=1)
        
        search_refresh = ctk.CTkFrame(sub_header, fg_color="transparent")
        search_refresh.grid(row=0, column=0, sticky="ew", pady=(0, 4))
        
        self.search_entry = ctk.CTkEntry(
            search_refresh, placeholder_text="🔍 Search", 
            height=34, corner_radius=17, fg_color="#242f3d", border_color="#101921",
            text_color="#ffffff", placeholder_text_color="#7f91a4"
        )
        self.search_entry.pack(side="left", fill="x", expand=True, padx=(0, 10))
        self.search_entry.bind("<KeyRelease>", self.on_search_key)
        
        self.btn_refresh = ctk.CTkButton(
            search_refresh, text="🔄 Refresh", command=self.refresh_files, 
            width=90, height=34, corner_radius=8, fg_color="#17212b", hover_color="#242f3d", text_color="#ffffff"
        )
        self.btn_refresh.pack(side="right")
        
        # Segmented Capsule Filters Row (under Search bar)
        self.filter_frame = ctk.CTkFrame(sub_header, fg_color="transparent", height=38)
        self.filter_frame.grid(row=1, column=0, sticky="ew", pady=(4, 4))
        
        self.filter_buttons = {}
        filters_list = [
            ("🌐 All", "all"),
            ("📄 Docs", "docs"),
            ("🎬 Media", "media"),
            ("📦 Zips", "zips"),
            ("⚙️ Others", "others")
        ]
        
        for label, filter_name in filters_list:
            btn = ctk.CTkButton(
                self.filter_frame, text=label,
                command=lambda f=filter_name: self.set_filter(f),
                width=85, height=28, corner_radius=14,
                font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold")
            )
            btn.pack(side="left", padx=4)
            self.filter_buttons[filter_name] = btn
            
        # Initialize filter states
        self.update_filter_buttons_ui()
        
        # Interactive Breadcrumbs Row
        self.breadcrumb_row = ctk.CTkFrame(sub_header, fg_color="transparent", height=28)
        self.breadcrumb_row.grid(row=2, column=0, sticky="ew", pady=(2, 0))
        self.breadcrumb_row.pack_propagate(False)
        
        # Selected Files Storage Dictionary
        self.selected_files = {}
        
        # Multi-Selection Action Bar (hidden by default)
        self.selection_bar = ctk.CTkFrame(sub_header, fg_color="#182533", height=38, corner_radius=8, border_color="#2481cc", border_width=1)
        self.selection_bar.grid_propagate(False)
        
        self.lbl_selection_count = ctk.CTkLabel(
            self.selection_bar, text="Selected: 0 items", text_color="#ffffff",
            font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold")
        )
        self.lbl_selection_count.pack(side="left", padx=15)
        
        btn_clear = ctk.CTkButton(
            self.selection_bar, text="✕", command=self.clear_selection,
            width=28, height=28, corner_radius=14, fg_color="transparent",
            text_color="#ff4d4d", hover_color="#2b1a1a", font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold")
        )
        btn_clear.pack(side="right", padx=(5, 10))
        
        self.btn_multi_delete = ctk.CTkButton(
            self.selection_bar, text="🗑️ Delete Selected", command=self.on_multi_delete,
            width=120, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            fg_color="transparent", text_color="#ff4d4d", hover_color="#2b1a1a"
        )
        self.btn_multi_delete.pack(side="right", padx=5)
        
        self.btn_multi_download = ctk.CTkButton(
            self.selection_bar, text="📥 Download Selected", command=self.on_multi_download,
            width=140, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
            fg_color="#2481cc", hover_color="#2b5278", text_color="#ffffff"
        )
        self.btn_multi_download.pack(side="right", padx=5)
        
        # 3. FILE EXPLORER AREA (Styled as the Telegram Chat Bubble stream)
        explorer_card = ctk.CTkFrame(main_content, fg_color="#0e1621", corner_radius=12, border_color="#101921", border_width=1)
        explorer_card.grid(row=2, column=0, sticky="nsew")
        explorer_card.grid_columnconfigure(0, weight=1)
        explorer_card.grid_rowconfigure(1, weight=1)
        
        # Column headers
        tbl_headers = ctk.CTkFrame(explorer_card, fg_color="#17212b", height=36, corner_radius=0)
        tbl_headers.grid(row=0, column=0, sticky="ew")
        
        # Master Select All: premium custom checkbox + label
        chk_all_wrap = ctk.CTkFrame(tbl_headers, fg_color="transparent")
        chk_all_wrap.pack(side="left", padx=(12, 8), pady=6)

        self.chk_select_all = SelectionCheckbox(
            chk_all_wrap, checked=False, on_toggle=self._on_select_all_toggled, size=20
        )
        self.chk_select_all.pack(side="left")

        lbl_chk_name = ctk.CTkLabel(
            chk_all_wrap, text="  Name", text_color="#7f91a4",
            font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold")
        )
        lbl_chk_name.pack(side="left")
        lbl_chk_name.bind("<Button-1>", lambda e: self.chk_select_all._on_click())
        
        lbl_h_actions = ctk.CTkLabel(tbl_headers, text="Actions", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"))
        lbl_h_actions.pack(side="right", padx=(10, 20), pady=6)
        
        lbl_h_date = ctk.CTkLabel(tbl_headers, text="Upload Date", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"))
        lbl_h_date.pack(side="right", padx=(10, 40), pady=6)
        
        lbl_h_size = ctk.CTkLabel(tbl_headers, text="Size", text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"))
        lbl_h_size.pack(side="right", padx=(10, 40), pady=6)
        
        self.scroll_frame = ctk.CTkScrollableFrame(explorer_card, fg_color="transparent", corner_radius=0)
        self.scroll_frame.grid(row=1, column=0, sticky="nsew", padx=2, pady=2)
        
        # Footer
        self.status_footer = ctk.CTkLabel(
            main_content, text="Connected.", text_color="#7f91a4", 
            font=ctk.CTkFont(family="Segoe UI", size=11), anchor="w"
        )
        self.status_footer.grid(row=3, column=0, sticky="ew", pady=(10, 0))
        
        self.refresh_files()

        # -----------------------------------------------
        # KEYBOARD SHORTCUTS (bound on root Tk window)
        # -----------------------------------------------
        root = self.master  # TGStorageApp (CTk root window)
        root.bind("<Control-a>",    lambda e: self._kb_select_all(e))
        root.bind("<Control-A>",    lambda e: self._kb_select_all(e))
        root.bind("<Delete>",       lambda e: self._kb_delete(e))
        root.bind("<Escape>",       lambda e: self._kb_escape(e))
        root.bind("<F5>",           lambda e: self.refresh_files())
        root.bind("<Control-u>",    lambda e: self.on_upload_click())
        root.bind("<Control-U>",    lambda e: self.on_upload_click())
        root.bind("<Control-d>",    lambda e: self._kb_download(e))
        root.bind("<Control-D>",    lambda e: self._kb_download(e))

    # -----------------------------------------------
    # KEYBOARD SHORTCUT HANDLERS
    # -----------------------------------------------
    def _kb_select_all(self, event):
        """Ctrl+A — select all visible files (ignore if typing in a text entry)."""
        if isinstance(event.widget, (tk.Entry, ctk.CTkEntry)):
            return  # let the entry handle it natively
        self.chk_select_all.set_checked(True)
        self._on_select_all_toggled(True)

    def _kb_delete(self, event):
        """Delete key — batch-delete selected files if any are selected."""
        if isinstance(event.widget, (tk.Entry, ctk.CTkEntry)):
            return
        if self.selected_files:
            self.master.on_multi_delete(list(self.selected_files.values()))

    def _kb_escape(self, event):
        """Escape — clear current selection."""
        if self.selected_files:
            self.clear_selection()

    def _kb_download(self, event):
        """Ctrl+D — batch-download selected files."""
        if isinstance(event.widget, (tk.Entry, ctk.CTkEntry)):
            return
        if self.selected_files:
            self.master.on_multi_download(list(self.selected_files.values()))

    def select_cloud(self):
        self.chat_cloud.set_active(True)
        self.chat_local.set_active(False)
        self.on_backend_change("Telegram Cloud")

    def select_local(self):
        self.chat_cloud.set_active(False)
        self.chat_local.set_active(True)
        self.on_backend_change("Local Storage")

    def on_backend_change(self, selected_backend):
        if selected_backend == "Telegram Cloud":
            self.master.active_backend = "cloud"
            self.lbl_storage_title.configure(text="Cloud Storage Stats")
            self.lbl_chat_title.configure(text="Saved Messages")
            self.lbl_chat_subtitle.configure(text="online • unlimited cloud storage")
            if hasattr(self, "lbl_status_dot"):
                self.lbl_status_dot.configure(text_color="#2bca4f")
            self.profile_card.pack(fill="x", padx=15, pady=(10, 5), side="bottom")
            if hasattr(self, "btn_sync"):
                self.btn_sync.pack(side="left", padx=4)
        else:
            self.master.active_backend = "local"
            self.lbl_storage_title.configure(text="Local Disk Storage")
            self.lbl_chat_title.configure(text="Local Disk Explorer")
            self.lbl_chat_subtitle.configure(text="folder explorer • workspace disk")
            if hasattr(self, "lbl_status_dot"):
                self.lbl_status_dot.configure(text_color="#7f91a4")
            self.profile_card.pack_forget()
            if hasattr(self, "btn_sync"):
                self.btn_sync.pack_forget()
            
        self.search_entry.delete(0, 'end')
        self.refresh_files()

    def pulse_status(self):
        """Creates a beautiful glowing pulsing animation on the online status dot."""
        if hasattr(self, "lbl_status_dot") and self.lbl_status_dot.winfo_exists():
            if self.master.active_backend == "cloud":
                curr_color = self.lbl_status_dot.cget("text_color")
                next_color = "#3ff267" if curr_color == "#2bca4f" else "#2bca4f"
                self.lbl_status_dot.configure(text_color=next_color)
            else:
                self.lbl_status_dot.configure(text_color="#7f91a4")
            self.after(800, self.pulse_status)

    def set_filter(self, filter_name):
        """Sets the active category filter and refreshes the explorer view."""
        self.active_filter = filter_name
        self.update_filter_buttons_ui()
        self.refresh_files()

    def update_filter_buttons_ui(self):
        """Updates the styling of capsule filter buttons to highlight the active one."""
        for filter_name, btn in self.filter_buttons.items():
            if filter_name == self.active_filter:
                btn.configure(
                    fg_color="#2481cc",
                    text_color="#ffffff",
                    hover_color="#2b5278"
                )
            else:
                btn.configure(
                    fg_color="#17212b",
                    text_color="#7f91a4",
                    hover_color="#242f3d"
                )

    def file_matches_filter(self, filename):
        """Checks if a filename matches the currently active category filter."""
        if self.active_filter == "all":
            return True
            
        ext = os.path.splitext(filename)[1].strip(".").lower()
        
        doc_exts = {"pdf", "txt", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "epub", "csv"}
        media_exts = {"png", "jpg", "jpeg", "gif", "bmp", "svg", "webp", "mp4", "mkv", "avi", "mov", "mp3", "wav", "flac", "ogg"}
        zip_exts = {"zip", "rar", "7z", "tar", "gz", "bz2"}
        
        if self.active_filter == "docs":
            return ext in doc_exts
        elif self.active_filter == "media":
            return ext in media_exts
        elif self.active_filter == "zips":
            return ext in zip_exts
        elif self.active_filter == "others":
            return ext not in doc_exts and ext not in media_exts and ext not in zip_exts
            
        return True

    def refresh_queue_card(self):
        """Refreshes the sidebar transfer progress card with live queue details."""
        running_job = None
        pending_count = 0
        completed_count = 0
        failed_count = 0
        for job in self.master.transfer_queue:
            if job['status'] == 'Running':
                running_job = job
            elif job['status'] == 'Pending':
                pending_count += 1
            elif job['status'] == 'Completed':
                completed_count += 1
            elif job['status'] == 'Failed':
                failed_count += 1
                
        if running_job:
            job_type = "Uploading" if running_job['type'] == 'upload' else "Downloading"
            lbl_text = f"{job_type}: {running_job['filename']}"
            if pending_count > 0:
                lbl_text += f" (+{pending_count} pending)"
            self.progress_label.configure(text=lbl_text)
            
            # Update progress bar and percent from job if set
            percent = running_job.get('percent', 0.0)
            self.progress_bar.set(percent)
            self.progress_percent.configure(text=f"{int(percent * 100)}%")
            
            # Size details
            curr_formatted = self.format_size(int(percent * running_job['filesize']))
            tot_formatted = self.format_size(running_job['filesize'])
            self.progress_size_label.configure(text=f"{curr_formatted} / {tot_formatted}")
            
            # Pack active cancel button
            if hasattr(self, "btn_cancel_active"):
                self.btn_cancel_active.configure(text="✕ Cancel Transfer")
                self.btn_cancel_active.pack(fill="x", padx=15, pady=(5, 10))
        elif self.master.is_transferring:
            self.progress_label.configure(text="Syncing Telegram Cloud...")
            self.progress_bar.set(0.0)
            self.progress_percent.configure(text="--")
            self.progress_size_label.configure(text="Scanning Saved Messages...")
            
            # Pack active cancel button for sync
            if hasattr(self, "btn_cancel_active"):
                self.btn_cancel_active.configure(text="✕ Cancel Sync")
                self.btn_cancel_active.pack(fill="x", padx=15, pady=(5, 10))
        else:
            self.progress_label.configure(text="Transfer: Idle")
            self.progress_bar.set(0.0)
            self.progress_percent.configure(text="0%")
            self.progress_size_label.configure(text="0.0 MB / 0.0 MB")
            
            # Unpack active cancel button
            if hasattr(self, "btn_cancel_active"):
                self.btn_cancel_active.pack_forget()

    def on_cancel_active_click(self):
        """Cancels the currently active transfer job or cloud sync."""
        if self.master.active_job_id is not None:
            # Cancel the active queue job by filename!
            for job in self.master.transfer_queue:
                if job['id'] == self.master.active_job_id:
                    self.master.cancel_transfer_job(job['filename'])
                    break
        elif self.master.is_transferring:
            # Cancel cloud sync!
            self.master.sync_cancelled = True
            messagebox.showinfo("Sync", "Cancelling cloud synchronization...")

    def navigate_to_folder(self, folder_id):
        self.current_folder_id = folder_id
        self.search_entry.delete(0, 'end')
        self.refresh_files()

    def navigate_to_local_path(self, relative_path):
        self.local_current_rel_path = relative_path
        self.search_entry.delete(0, 'end')
        self.refresh_files()

    def render_breadcrumbs(self):
        for child in self.breadcrumb_row.winfo_children():
            child.destroy()
            
        if self.master.active_backend == "cloud":
            path = self.master.db.get_breadcrumb_path(self.current_folder_id)
            for index, (name, f_id) in enumerate(path):
                if index > 0:
                    sep = ctk.CTkLabel(
                        self.breadcrumb_row, text=" › ", text_color="#7f91a4", 
                        font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold")
                    )
                    sep.pack(side="left")
                    
                lbl = ctk.CTkButton(
                    self.breadcrumb_row, text=name, command=lambda target_id=f_id: self.navigate_to_folder(target_id),
                    height=26, fg_color="transparent", text_color="#2481cc" if f_id != self.current_folder_id else "#ffffff",
                    hover_color="#1c2a38", font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold" if f_id == self.current_folder_id else "normal"),
                    anchor="w", width=0
                )
                lbl.pack(side="left")
        else:
            path_parts = [p for p in self.local_current_rel_path.split(os.sep) if p]
            
            lbl = ctk.CTkButton(
                self.breadcrumb_row, text="Local Root", command=lambda: self.navigate_to_local_path(""),
                height=26, fg_color="transparent", text_color="#2481cc" if self.local_current_rel_path else "#ffffff",
                hover_color="#1c2a38", font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold" if not self.local_current_rel_path else "normal"),
                anchor="w", width=0
            )
            lbl.pack(side="left")
            
            accumulated = ""
            for index, part in enumerate(path_parts):
                sep = ctk.CTkLabel(
                    self.breadcrumb_row, text=" › ", text_color="#7f91a4", 
                    font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold")
                )
                sep.pack(side="left")
                
                accumulated = os.path.join(accumulated, part)
                lbl = ctk.CTkButton(
                    self.breadcrumb_row, text=part, command=lambda target_path=accumulated: self.navigate_to_local_path(target_path),
                    height=26, fg_color="transparent", text_color="#2481cc" if index < len(path_parts) - 1 else "#ffffff",
                    hover_color="#1c2a38", font=ctk.CTkFont(family="Segoe UI", size=12, weight="bold" if index == len(path_parts) - 1 else "normal"),
                    anchor="w", width=0
                )
                lbl.pack(side="left")

    def _on_select_all_toggled(self, checked: bool):
        """Called when the master checkbox is clicked – select/deselect visible files."""
        if not hasattr(self, "current_visible_files") or not self.current_visible_files:
            self.chk_select_all.set_checked(False)
            return
        if checked:
            for item in self.current_visible_files:
                self.selected_files[item['filename']] = item
        else:
            self.selected_files.clear()
        self.update_selection_bar()
        # Only refresh checkbox states, NOT the full list
        self._refresh_row_checkboxes()

    def _refresh_row_checkboxes(self):
        """Re-sync per-row checkbox widgets without rebuilding the whole list."""
        for filename, chk_widget in getattr(self, "_row_checkboxes", {}).items():
            try:
                if chk_widget.winfo_exists():
                    chk_widget.set_checked(filename in self.selected_files)
            except Exception:
                pass
        # Sync master checkbox state
        files = getattr(self, "current_visible_files", [])
        if files and all(f['filename'] in self.selected_files for f in files):
            self.chk_select_all.set_checked(True)
        else:
            self.chk_select_all.set_checked(False)

    def refresh_files(self, search_query=None):
        for child in self.scroll_frame.winfo_children():
            child.destroy()
            
        self.render_breadcrumbs()
        self.file_progress_bars = {}
        
        if search_query is None:
            search_query = self.search_entry.get().strip() if hasattr(self, "search_entry") else ""
        
        if self.master.active_backend == "cloud":
            count, size = self.master.db.get_storage_stats()
            formatted_size = self.format_size(size)
            self.lbl_usage_text.configure(text=f"{count} files • {formatted_size}")
            
            gdrive_cap = 15.0 * 1024 * 1024 * 1024
            usage_percent = min(size / gdrive_cap, 1.0) if size else 0.0
            self.storage_progress.set(usage_percent)
            self.lbl_usage_cap.configure(text=f"{formatted_size} of Unlimited (15GB GDrive Equivalent)")
            
            # Update sidebar chat counters
            self.chat_cloud.set_counter(count)
            try:
                local_count = 0
                for root, dirs, f_list in os.walk(LOCAL_DRIVE_ROOT):
                    local_count += len(f_list) + len(dirs)
                self.chat_local.set_counter(local_count)
            except Exception:
                pass
            
            if search_query:
                with self.master.db.get_conn() as conn:
                    cursor = conn.cursor()
                    cursor.execute("SELECT id, name, parent_id FROM folders WHERE name LIKE ? ORDER BY name ASC", (f"%{search_query}%",))
                    folders = [dict(row) for row in cursor.fetchall()]
                    cursor.execute("SELECT id, filename, file_size, telegram_message_id, upload_date, folder_id FROM files WHERE filename LIKE ? ORDER BY id DESC", (f"%{search_query}%",))
                    files = [dict(row) for row in cursor.fetchall()]
                self.status_footer.configure(text=f"Global Cloud Search: Mapped {len(folders)} folders and {len(files)} files.")
            else:
                folders = self.master.db.get_folders_in_folder(self.current_folder_id)
                files = self.master.db.get_files_in_folder(self.current_folder_id)
                self.status_footer.configure(text=f"Active Cloud Folder: {len(folders)} folders, {len(files)} files.")
                
        else:
            local_count = 0
            local_size = 0
            for root, dirs, f_list in os.walk(LOCAL_DRIVE_ROOT):
                for f in f_list:
                    local_count += 1
                    try:
                        local_size += os.path.getsize(os.path.join(root, f))
                    except Exception:
                        pass
                        
            formatted_size = self.format_size(local_size)
            self.lbl_usage_text.configure(text=f"{local_count} files • {formatted_size}")
            
            local_cap = 50.0 * 1024 * 1024 * 1024
            usage_percent = min(local_size / local_cap, 1.0) if local_size else 0.0
            self.storage_progress.set(usage_percent)
            self.lbl_usage_cap.configure(text=f"{formatted_size} of 50.0 GB Local limit")
            
            # Update sidebar chat counters
            self.chat_local.set_counter(local_count)
            try:
                cloud_count, _ = self.master.db.get_storage_stats()
                self.chat_cloud.set_counter(cloud_count)
            except Exception:
                pass
            
            folders = []
            files = []
            
            abs_path = os.path.join(LOCAL_DRIVE_ROOT, self.local_current_rel_path)
            os.makedirs(abs_path, exist_ok=True)
            
            try:
                for entry in os.scandir(abs_path):
                    stat = entry.stat()
                    size_val = stat.st_size
                    date_val = datetime.fromtimestamp(stat.st_mtime).strftime("%Y-%m-%d %H:%M")
                    
                    if entry.is_dir():
                        if not search_query or search_query.lower() in entry.name.lower():
                            folders.append({
                                "name": entry.name,
                                "local_path": entry.path,
                                "upload_date": date_val
                            })
                    elif entry.is_file():
                        if not search_query or search_query.lower() in entry.name.lower():
                            files.append({
                                "filename": entry.name,
                                "file_size": size_val,
                                "upload_date": date_val,
                                "local_path": entry.path
                            })
            except Exception:
                pass
                
            folders.sort(key=lambda x: x["name"].lower())
            files.sort(key=lambda x: x["upload_date"], reverse=True)
            
            self.status_footer.configure(text=f"Active Local Directory: {len(folders)} folders, {len(files)} files.")

        # Apply capsule file type filters
        if self.active_filter != "all":
            folders = []
            files = [f for f in files if self.file_matches_filter(f.get("filename", ""))]

        # Clean selection keys to ensure only active files inside the folder are selected
        active_filenames = {f.get("filename", "") for f in files}
        self.selected_files = {k: v for k, v in self.selected_files.items() if k in active_filenames}
        
        # Keep track of visible files for master Select All toggle
        self.current_visible_files = files
        # Reset per-row checkbox registry for this render pass
        self._row_checkboxes = {}

        # Update Master Select All state
        if files and all(f['filename'] in self.selected_files for f in files):
            self.chk_select_all.set_checked(True)
        else:
            self.chk_select_all.set_checked(False)

        self.update_selection_bar()

        if not folders and not files:
            empty_lbl = ctk.CTkLabel(
                self.scroll_frame, text="This folder context is empty.\nClick 'Upload File' or 'New Folder' to start adding items.", 
                text_color="#7f91a4", font=ctk.CTkFont(family="Segoe UI", size=13, slant="italic")
            )
            empty_lbl.pack(pady=80, expand=True)
            return

        index = 0
        
        # Helpers for recursive binding
        def bind_double_click(widget, callback_func):
            widget.bind("<Double-Button-1>", callback_func)
            def bind_recursive(parent):
                for child in parent.winfo_children():
                    if not isinstance(child, (ctk.CTkButton,)):
                        child.bind("<Double-Button-1>", callback_func)
                        if isinstance(child, ctk.CTkFrame):
                            bind_recursive(child)
            bind_recursive(widget)

        # ----------------------------------------------------
        # DRAW DIR ROWS (Folders - rendered as Telegram Folder Bubbles)
        # ----------------------------------------------------
        for item in folders:
            normal_bubble = "#182533"  # Telegram dark incoming message bubble
            
            row_frame = ctk.CTkFrame(self.scroll_frame, fg_color=normal_bubble, height=60, corner_radius=12)
            row_frame.pack(fill="x", pady=4, padx=8)
            row_frame.pack_propagate(False)
            
            # Left Circular Badge (Folder Icon Badge)
            badge_icon = ctk.CTkLabel(
                row_frame, text="📁", corner_radius=21, fg_color="#285f50",
                text_color="#ffffff", width=42, height=42,
                font=ctk.CTkFont(family="Segoe UI", size=16, weight="bold")
            )
            badge_icon.pack(side="left", padx=(12, 12), pady=9)
            badge_icon.pack_propagate(False)
            
            # Details Frame (Double Line)
            lbl_frame = ctk.CTkFrame(row_frame, fg_color="transparent")
            lbl_frame.pack(side="left", fill="both", expand=True, pady=8)
            
            lbl_name = ctk.CTkLabel(
                lbl_frame, text=item["name"], text_color="#2481cc", 
                font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"), anchor="w"
            )
            lbl_name.pack(fill="x", anchor="w")
            
            lbl_sub = ctk.CTkLabel(
                lbl_frame, text="Folder • Double-click to open", text_color="#7f91a4", 
                font=ctk.CTkFont(family="Segoe UI", size=11), anchor="w"
            )
            lbl_sub.pack(fill="x", anchor="w", pady=(1, 0))
            
            # Bind Double-Click Action for Navigation
            if self.master.active_backend == "cloud":
                nav_callback = lambda e, f_id=item["id"]: self.navigate_to_folder(f_id)
            else:
                rel = os.path.relpath(item["local_path"], LOCAL_DRIVE_ROOT)
                nav_callback = lambda e, r_path=rel: self.navigate_to_local_path(r_path)
                
            bind_double_click(row_frame, nav_callback)
            
            # Far Right Checkmark & Timestamp Badge (Pins to bottom-right corner)
            time_str = "12:00"
            raw_date = item.get("upload_date", "")
            if raw_date:
                if " " in raw_date:
                    parts = raw_date.split(" ")
                    if len(parts) >= 2:
                        time_str = parts[1][:5]
                elif ":" in raw_date:
                    time_str = raw_date[:5]
            else:
                time_str = datetime.now().strftime("%H:%M")
                
            checkmark = " ✓✓" if self.master.active_backend == "cloud" else " ✓"
            checkmark_color = "#5288c1" if self.master.active_backend == "cloud" else "#7f91a4"
            
            lbl_status_mark = ctk.CTkLabel(
                row_frame, text=f"{time_str}{checkmark}", text_color=checkmark_color,
                font=ctk.CTkFont(family="Segoe UI", size=9, slant="italic")
            )
            lbl_status_mark.pack(side="right", padx=(10, 15), anchor="se", pady=(0, 6))
            
            # Actions Row (between details and checkmarks)
            act_container = ctk.CTkFrame(row_frame, fg_color="transparent")
            act_container.pack(side="right", padx=5, pady=16)
            
            if self.master.active_backend == "cloud":
                btn_open = ctk.CTkButton(
                    act_container, text="Open", command=lambda f_id=item["id"]: self.navigate_to_folder(f_id),
                    width=65, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
                    fg_color="#242f3d", hover_color="#2481cc", text_color="#2481cc"
                )
                btn_del = ctk.CTkButton(
                    act_container, text="Delete", command=lambda f_id=item["id"], name=item["name"]: self.master.trigger_delete_folder(f_id, name),
                    width=65, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11),
                    fg_color="transparent", text_color="#ff4d4d", hover_color="#2b1a1a"
                )
            else:
                rel = os.path.relpath(item["local_path"], LOCAL_DRIVE_ROOT)
                btn_open = ctk.CTkButton(
                    act_container, text="Open", command=lambda r_path=rel: self.navigate_to_local_path(r_path),
                    width=65, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
                    fg_color="#242f3d", hover_color="#2481cc", text_color="#2481cc"
                )
                btn_del = ctk.CTkButton(
                    act_container, text="Delete", command=lambda path=item["local_path"], name=item["name"]: self.master.trigger_delete_folder(None, name, path),
                    width=65, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11),
                    fg_color="transparent", text_color="#ff4d4d", hover_color="#2b1a1a"
                )
                
            btn_open.pack(side="left", padx=2)
            btn_del.pack(side="left", padx=2)
            
            self.bind_row_hover(row_frame, normal_bubble)
            index += 1

        # ----------------------------------------------------
        # DRAW FILE ROWS (Files - rendered as Telegram Document Bubbles)
        # ----------------------------------------------------
        for item in files:
            normal_bubble = "#182533"  # Telegram dark incoming message bubble
            
            # Check if this file is actively downloading/uploading or pending in the queue
            is_in_queue = False
            job_status = "Pending"
            job_percent = 0.0
            for job in self.master.transfer_queue:
                if job['filename'] == item['filename'] and job['status'] in ('Pending', 'Running'):
                    is_in_queue = True
                    job_status = job['status']
                    job_percent = job.get('percent', 0.0)
                    break
                    
            card_height = 64 if is_in_queue else 60
            # Highlight selected cards with a blue tint
            is_selected = item['filename'] in self.selected_files
            normal_bubble = "#182533"
            selected_bubble = "#1a2d42"
            card_bg = selected_bubble if is_selected else normal_bubble

            row_frame = ctk.CTkFrame(self.scroll_frame, fg_color=card_bg, height=card_height, corner_radius=12)
            row_frame.pack(fill="x", pady=4, padx=8)
            row_frame.pack_propagate(False)
            
            # Dynamic File Extension Extract + colored badge per type
            ext = os.path.splitext(item["filename"])[1].strip(".").upper()
            if not ext or len(ext) > 4:
                ext = "DOC" if not ext else ext[:3]
            # Color-code by file category
            _ext_low = ext.lower()
            doc_exts  = {"pdf","txt","doc","docx","xls","xlsx","ppt","pptx","epub","csv"}
            media_exts= {"png","jpg","jpeg","gif","bmp","svg","webp","mp4","mkv","avi","mov","mp3","wav","flac","ogg"}
            zip_exts  = {"zip","rar","7z","tar","gz","bz2"}
            if _ext_low in doc_exts:
                badge_bg = "#1e6b55"   # teal-green → documents
            elif _ext_low in media_exts:
                badge_bg = "#5b3fa6"   # purple → media
            elif _ext_low in zip_exts:
                badge_bg = "#a65f1e"   # orange → archives
            else:
                badge_bg = "#2e4a6b"   # steel-blue → others
                
            # Premium custom checkbox for multi-selection
            _fname = item['filename']
            _item_data = item

            def _make_toggle(fname, idata, card):
                def _on_toggle(checked):
                    if checked:
                        self.selected_files[fname] = idata
                        try:
                            card.configure(fg_color=selected_bubble)
                        except Exception:
                            pass
                    else:
                        self.selected_files.pop(fname, None)
                        try:
                            card.configure(fg_color=normal_bubble)
                        except Exception:
                            pass
                    self.update_selection_bar()
                    # Sync master checkbox without full rebuild
                    self._refresh_row_checkboxes()
                return _on_toggle

            chk = SelectionCheckbox(
                row_frame,
                checked=(_fname in self.selected_files),
                on_toggle=_make_toggle(_fname, _item_data, row_frame),
                size=22
            )
            chk.pack(side="left", padx=(10, 4), pady=19)
            # Register in per-row map for efficient sync
            self._row_checkboxes[_fname] = chk
            
            # Left Circular Badge (color-coded by file type)
            badge_icon = ctk.CTkLabel(
                row_frame, text=ext, corner_radius=21, fg_color=badge_bg,
                text_color="#ffffff", width=42, height=42,
                font=ctk.CTkFont(family="Segoe UI", size=9, weight="bold")
            )
            badge_icon.pack(side="left", padx=(10, 12), pady=9)
            badge_icon.pack_propagate(False)
            
            # Details Frame (Double Line)
            lbl_frame = ctk.CTkFrame(row_frame, fg_color="transparent")
            lbl_frame.pack(side="left", fill="both", expand=True, pady=8)
            
            lbl_name = ctk.CTkLabel(
                lbl_frame, text=item["filename"], text_color="#ffffff", 
                font=ctk.CTkFont(family="Segoe UI", size=13, weight="bold"), anchor="w"
            )
            lbl_name.pack(fill="x", anchor="w")
            
            formatted_size = self.format_size(item["file_size"])
            lbl_sub = ctk.CTkLabel(
                lbl_frame, text=f"{formatted_size} • {item['upload_date']}", text_color="#7f91a4", 
                font=ctk.CTkFont(family="Segoe UI", size=11), anchor="w"
            )
            lbl_sub.pack(fill="x", anchor="w", pady=(1, 0))
            
            # Bind Double-Click to Download File
            if self.master.active_backend == "cloud":
                dl_callback = lambda e, f_id=item["id"], msg_id=item["telegram_message_id"], name=item["filename"]: self.master.trigger_download(f_id, msg_id, name)
            else:
                dl_callback = lambda e, path=item["local_path"], name=item["filename"]: self.master.trigger_download(None, None, name, path)
                
            bind_double_click(row_frame, dl_callback)
            
            # Far Right Checkmark & Timestamp Badge (Pins to bottom-right corner)
            time_str = "12:00"
            raw_date = item.get("upload_date", "")
            if raw_date:
                if " " in raw_date:
                    parts = raw_date.split(" ")
                    if len(parts) >= 2:
                        time_str = parts[1][:5]
                elif ":" in raw_date:
                    time_str = raw_date[:5]
            else:
                time_str = datetime.now().strftime("%H:%M")
                
            checkmark = " ✓✓" if self.master.active_backend == "cloud" else " ✓"
            checkmark_color = "#5288c1" if self.master.active_backend == "cloud" else "#7f91a4"
            
            lbl_status_mark = ctk.CTkLabel(
                row_frame, text=f"{time_str}{checkmark}", text_color=checkmark_color,
                font=ctk.CTkFont(family="Segoe UI", size=9, slant="italic")
            )
            lbl_status_mark.pack(side="right", padx=(10, 15), anchor="se", pady=(0, 6))
            
            # Actions Row (between details and checkmarks)
            act_container = ctk.CTkFrame(row_frame, fg_color="transparent")
            act_container.pack(side="right", padx=5, pady=16)
            
            # Rename button (✏️)
            btn_rename = ctk.CTkButton(
                act_container, text="✏️",
                command=lambda f_id=item.get("id"), name=item["filename"], path=item.get("local_path"): self.master.trigger_rename(f_id, name, path),
                width=34, height=28, corner_radius=6,
                fg_color="#242f3d", hover_color="#2b4870", text_color="#ffffff",
                font=ctk.CTkFont(family="Segoe UI", size=12)
            )

            if is_in_queue:
                btn_cancel = ctk.CTkButton(
                    act_container, text="✕ Cancel", command=lambda name=item["filename"]: self.master.cancel_transfer_job(name),
                    width=85, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
                    fg_color="#ff4d4d", hover_color="#cc3333", text_color="#ffffff"
                )
                btn_cancel.pack(side="left", padx=2)
            elif self.master.active_backend == "cloud":
                btn_prev = ctk.CTkButton(
                    act_container, text="👁️", command=lambda f_id=item["id"], msg_id=item["telegram_message_id"], name=item["filename"]: self.master.trigger_preview(f_id, msg_id, name),
                    width=34, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=12),
                    fg_color="#242f3d", hover_color="#2481cc", text_color="#ffffff"
                )
                btn_dl = ctk.CTkButton(
                    act_container, text="Download", command=lambda f_id=item["id"], msg_id=item["telegram_message_id"], name=item["filename"]: self.master.trigger_download(f_id, msg_id, name),
                    width=80, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
                    fg_color="#2481cc", hover_color="#2b5278"
                )
                btn_del = ctk.CTkButton(
                    act_container, text="Delete", command=lambda f_id=item["id"], msg_id=item["telegram_message_id"], name=item["filename"]: self.master.trigger_delete(f_id, msg_id, name),
                    width=65, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11),
                    fg_color="transparent", text_color="#ff4d4d", hover_color="#2b1a1a"
                )
                btn_prev.pack(side="left", padx=2)
                btn_rename.pack(side="left", padx=2)
                btn_dl.pack(side="left", padx=2)
                btn_del.pack(side="left", padx=2)
            else:
                btn_prev = ctk.CTkButton(
                    act_container, text="👁️", command=lambda path=item["local_path"], name=item["filename"]: self.master.trigger_preview(None, None, name, path),
                    width=34, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=12),
                    fg_color="#242f3d", hover_color="#2481cc", text_color="#ffffff"
                )
                btn_dl = ctk.CTkButton(
                    act_container, text="Download", command=lambda path=item["local_path"], name=item["filename"]: self.master.trigger_download(None, None, name, path),
                    width=80, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11, weight="bold"),
                    fg_color="#2481cc", hover_color="#2b5278"
                )
                btn_del = ctk.CTkButton(
                    act_container, text="Delete", command=lambda path=item["local_path"], name=item["filename"]: self.master.trigger_delete(None, None, name, path),
                    width=65, height=28, corner_radius=6, font=ctk.CTkFont(family="Segoe UI", size=11),
                    fg_color="transparent", text_color="#ff4d4d", hover_color="#2b1a1a"
                )
                btn_prev.pack(side="left", padx=2)
                btn_rename.pack(side="left", padx=2)
                btn_dl.pack(side="left", padx=2)
                btn_del.pack(side="left", padx=2)

            # RIGHT-CLICK CONTEXT MENU on file card
            def _make_context_menu(it, rf):
                def show_menu(event):
                    menu = tk.Menu(
                        self, tearoff=0,
                        bg="#17212b", fg="#dce8f5",
                        activebackground="#2481cc", activeforeground="#ffffff",
                        font=("Segoe UI", 11), bd=0, relief="flat"
                    )
                    if self.master.active_backend == "cloud":
                        menu.add_command(
                            label="  👁️  Preview",
                            command=lambda: self.master.trigger_preview(it.get("id"), it.get("telegram_message_id"), it["filename"])
                        )
                        menu.add_command(
                            label="  📥  Download",
                            command=lambda: self.master.trigger_download(it.get("id"), it.get("telegram_message_id"), it["filename"])
                        )
                        menu.add_separator()
                        menu.add_command(
                            label="  ✏️  Rename",
                            command=lambda: self.master.trigger_rename(it.get("id"), it["filename"])
                        )
                        menu.add_separator()
                        menu.add_command(
                            label="  🗑️  Delete",
                            command=lambda: self.master.trigger_delete(it.get("id"), it.get("telegram_message_id"), it["filename"])
                        )
                    else:
                        menu.add_command(
                            label="  👁️  Preview",
                            command=lambda: self.master.trigger_preview(None, None, it["filename"], it.get("local_path"))
                        )
                        menu.add_command(
                            label="  📥  Download",
                            command=lambda: self.master.trigger_download(None, None, it["filename"], it.get("local_path"))
                        )
                        menu.add_separator()
                        menu.add_command(
                            label="  ✏️  Rename",
                            command=lambda: self.master.trigger_rename(None, it["filename"], it.get("local_path"))
                        )
                        menu.add_separator()
                        menu.add_command(
                            label="  🗑️  Delete",
                            command=lambda: self.master.trigger_delete(None, None, it["filename"], it.get("local_path"))
                        )
                    try:
                        menu.tk_popup(event.x_root, event.y_root)
                    finally:
                        menu.grab_release()
                return show_menu

            _ctx = _make_context_menu(item, row_frame)
            row_frame.bind("<Button-3>", _ctx)
            
            if is_in_queue:
                pbar = ctk.CTkProgressBar(
                    row_frame, height=3, fg_color="#242f3d", 
                    progress_color="#2bca4f" if job_status == "Running" else "#5288c1"
                )
                pbar.set(job_percent)
                pbar.pack(side="bottom", fill="x", padx=12, pady=(0, 4))
                
                # Store the progress bar inside the dictionary for live status set operations
                self.file_progress_bars[item["filename"]] = pbar
                
            self.bind_row_hover(row_frame, normal_bubble, selected_bubble, _fname)
            index += 1

    def bind_row_hover(self, widget, normal_bg, selected_bg=None, fname=None):
        """Binds hover on the row frame only – lightweight, no recursive re-binding."""
        hover_color = "#203040"
        selected_hover = "#1f3550"

        def on_enter(e):
            # Don't override selected highlight colour strongly
            if selected_bg and fname and fname in self.selected_files:
                widget.configure(fg_color=selected_hover)
            else:
                widget.configure(fg_color=hover_color)

        def on_leave(e):
            x, y = e.x_root, e.y_root
            try:
                w = widget.winfo_containing(x, y)
                if w and (w is widget or str(w).startswith(str(widget))):
                    return
            except Exception:
                pass
            # Restore correct resting colour
            if selected_bg and fname and fname in self.selected_files:
                widget.configure(fg_color=selected_bg)
            else:
                widget.configure(fg_color=normal_bg)

        widget.bind("<Enter>", on_enter)
        widget.bind("<Leave>", on_leave)

    def format_size(self, size_bytes):
        """Formats bytes into human-readable KB, MB, GB, etc."""
        if size_bytes == 0:
            return "0 B"
        size_name = ("B", "KB", "MB", "GB", "TB")
        import math
        i = int(math.floor(math.log(size_bytes, 1024)))
        p = math.pow(1024, i)
        s = round(size_bytes / p, 2)
        return f"{s} {size_name[i]}"

    def on_search_key(self, event):
        """Debounced search – waits 200 ms after last keypress before filtering to avoid lag."""
        if hasattr(self, "_search_after_id"):
            try:
                self.after_cancel(self._search_after_id)
            except Exception:
                pass
        self._search_after_id = self.after(200, self._do_search)

    def _do_search(self):
        query = self.search_entry.get().strip()
        self.refresh_files(query)

    def on_upload_click(self):
        filepaths = filedialog.askopenfilenames(title="Select File(s) to Upload")
        if filepaths:
            for path in filepaths:
                self.master.trigger_upload(path, self.current_folder_id)

    def on_new_folder_click(self):
        dialog = ctk.CTkInputDialog(text="Enter folder name:", title="Create Folder")
        folder_name = dialog.get_input()
        if folder_name and folder_name.strip():
            if self.master.active_backend == "cloud":
                self.master.db.create_folder(folder_name.strip(), self.current_folder_id)
            else:
                os.makedirs(os.path.join(LOCAL_DRIVE_ROOT, self.local_current_rel_path, folder_name.strip()), exist_ok=True)
            self.refresh_files()

    def on_sync_click(self):
        """Dispatches an index synchronization request for Cloud Storage."""
        if self.master.active_backend == "cloud":
            self.master.trigger_sync()

    def on_backup_click(self):
        """Launches directory picker to choose the local folder mapped for auto-backups."""
        folder = filedialog.askdirectory(title="Select Local Directory to Auto-Backup")
        if folder:
            self.master.db.set_status_val("auto_backup_dir", folder)
            self.master.auto_backup_dir = folder
            self.master.start_backup_watcher()
            messagebox.showinfo("Success", f"Auto-Backup mapped successfully to folder:\n{folder}\nAny new files placed in this folder will be backed up silently!")

    def on_downloads_click(self):
        """Launches directory picker to choose the default downloads directory."""
        folder = filedialog.askdirectory(title="Select Default Downloads Folder")
        if folder:
            self.master.db.set_status_val("downloads_dir", folder)
            self.master.downloads_dir = folder
            messagebox.showinfo("Success", f"Default downloads folder set to:\n{folder}\nAny future downloads will go there directly!")

    def clear_selection(self):
        """Clears all selected files and hides the selection bar without full list rebuild."""
        self.selected_files.clear()
        self.update_selection_bar()
        self._refresh_row_checkboxes()

    def update_selection_bar(self):
        """Shows or hides the multi-selection action bar based on selected items."""
        count = len(self.selected_files)
        if count > 0:
            self.lbl_selection_count.configure(text=f"Selected: {count} items")
            self.selection_bar.grid(row=3, column=0, sticky="ew", pady=(4, 2))
        else:
            self.selection_bar.grid_forget()

    def on_multi_download(self):
        """Dispatches sequential download jobs for all selected files."""
        count = len(self.selected_files)
        if count == 0:
            return
            
        # Get downloads directory
        dest_dir = self.master.downloads_dir
        if not dest_dir or not os.path.exists(dest_dir):
            dest_dir = filedialog.askdirectory(title="Select Destination Folder")
            if not dest_dir:
                return
            self.master.db.set_status_val("downloads_dir", dest_dir)
            self.master.downloads_dir = dest_dir
            
        # Queue all selected files
        for filename, item in self.selected_files.items():
            if self.master.active_backend == "cloud":
                self.master.add_to_queue(
                    "download", filename=filename, 
                    message_id=item["telegram_message_id"], dest_dir=dest_dir
                )
            else:
                self.master.add_to_queue(
                    "download", filename=filename, 
                    dest_dir=dest_dir, source_path=item["local_path"]
                )
                
        # Clear selection and notify
        self.selected_files.clear()
        self.update_selection_bar()
        self.refresh_files()
        
        messagebox.showinfo("Downloads Queued", f"Successfully queued {count} selected files for sequential download!")

    def on_multi_delete(self):
        """Deletes all selected files simultaneously (with a single confirmation)."""
        count = len(self.selected_files)
        if count == 0:
            return
            
        if not messagebox.askyesno("Confirm Multi-Delete", f"Are you sure you want to delete {count} selected files permanently?"):
            return
            
        self.master.show_loading(f"Deleting {count} files...")
        
        # We can delete them asynchronously in a single batch!
        if self.master.active_backend == "cloud":
            self.master.run_async_coro(self.master.async_multi_delete(list(self.selected_files.values())))
        else:
            self.master.trigger_multi_delete_local(list(self.selected_files.values()))

# ==========================================
# APPLICATION START ENTRYPOINT
# ==========================================
if __name__ == "__main__":
    try:
        app = TGStorageApp()
        app.mainloop()
    except KeyboardInterrupt:
        sys.exit(0)
