using System;
using System.Collections.Generic;

namespace TelegramDrive.Helpers;

public static class LocalizationHelper
{
    private static string _currentLanguage = "ar"; // Default to Arabic as requested by the user

    public static string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (value == "ar" || value == "en")
            {
                _currentLanguage = value;
                try
                {
                    App.Database?.SetStatusVal("language", value);
                }
                catch { }
            }
        }
    }

    public static void Initialize()
    {
        try
        {
            var savedLang = App.Database?.GetStatusVal("language", "ar");
            if (savedLang == "ar" || savedLang == "en")
            {
                _currentLanguage = savedLang;
            }
        }
        catch
        {
            _currentLanguage = "ar";
        }
    }

    public static string ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == "ar" ? "en" : "ar";
        return CurrentLanguage;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en"] = new()
        {
            ["login_title"] = "Telegram Cloud Storage",
            ["login_subtitle"] = "Enter API ID & API Hash to link secure storage.",
            ["api_id"] = "Telegram API ID",
            ["api_hash"] = "Telegram API HASH",
            ["phone_number"] = "Phone Number (International)",
            ["send_code"] = "Send Verification Code",
            ["how_to_get"] = "ℹ️ How to get API ID / API Hash?",
            ["scan_qr"] = "Scan QR Code to Login Direct",
            ["getting_started"] = "Getting Started",
            ["getting_started_close"] = "Close",
            ["open_telegram_org"] = "🔗 Open my.telegram.org",
            ["requesting_otp"] = "Requesting OTP from Telegram...",
            ["failed_send_code"] = "Failed to send code. Check your credentials and phone number.",
            ["api_id_required"] = "API ID and API Hash are required first.",
            ["api_id_numeric"] = "API ID must be a numeric integer value.",
            ["phone_required"] = "Phone Number must be filled in.",
            ["otp_title"] = "Verify Code",
            ["otp_subtitle"] = "Sent code to your Telegram app: ",
            ["otp_label"] = "Verification Code",
            ["otp_placeholder"] = "Enter the code",
            ["pwd_label"] = "2FA Password (Optional)",
            ["pwd_placeholder"] = "Enter 2FA password if enabled",
            ["verify_login"] = "Verify & Login",
            ["back_to_login"] = "Back to Login",
            ["verifying_auth"] = "Verifying authentication...",
            ["pwd_required_msg"] = "2FA Password may be required. Enter it below.",
            ["verification_failed"] = "Verification failed: ",
            ["db_title"] = "Telegram Desktop Storage",
            ["online"] = "online",
            ["saved_messages"] = "Saved Messages",
            ["cloud_backend"] = "Cloud Storage Backend",
            ["local_disk"] = "Local Disk Explorer",
            ["local_backend"] = "Local Directory Backend",
            ["unlimited_cloud"] = "online • unlimited cloud storage",
            ["local_explorer"] = "Local Explorer • Local Disk",
            ["upload_file"] = "⬆️ Upload File",
            ["new_folder"] = "📁 New Folder",
            ["sync_cloud"] = "🔄 Sync Cloud",
            ["search_placeholder"] = "Search",
            ["refresh"] = "Refresh",
            ["filter_all"] = "All",
            ["filter_docs"] = "Docs",
            ["filter_media"] = "Media",
            ["filter_zips"] = "Zips",
            ["filter_others"] = "Others",
            ["set_backup"] = "⚙️  Set Auto-Backup Folder",
            ["set_downloads"] = "📥  Set Downloads Folder",
            ["disconnect"] = "Disconnect Account",
            ["confirm_logout"] = "Are you sure you want to disconnect? Your session will be safely remembered for quick access.",
            ["confirm_delete_folder"] = "Are you sure you want to delete folder ",
            ["confirm_delete_title"] = "Confirm Delete",
            ["delete_btn"] = "Delete",
            ["cancel_btn"] = "Cancel",
            ["welcome_back"] = "Welcome Back",
            ["select_account"] = "Select an account to login instantly.",
            ["login_btn"] = "Login",
            ["use_another_account"] = "Login with another account",
            ["back_to_accounts"] = "⬅️ Back to saved accounts",
            ["confirm_remove_account"] = "Are you sure you want to delete this login session? This will remove all local data for this user to prevent unauthorized access."
        },
        ["ar"] = new()
        {
            ["login_title"] = "تخزين سحابي تليجرام",
            ["login_subtitle"] = "أدخل معرف API وهاش API لربط التخزين الآمن.",
            ["api_id"] = "معرف تليجرام (API ID)",
            ["api_hash"] = "هاش تليجرام (API HASH)",
            ["phone_number"] = "رقم الهاتف الدولي (International)",
            ["send_code"] = "إرسال كود التحقق",
            ["how_to_get"] = "ℹ️ كيف تحصل على معرف وهاش الـ API؟",
            ["scan_qr"] = "امسح رمز الـ QR لتسجيل الدخول المباشر",
            ["getting_started"] = "البدء بالاستخدام",
            ["getting_started_close"] = "إغلاق",
            ["open_telegram_org"] = "🔗 افتح موقع my.telegram.org",
            ["requesting_otp"] = "جاري طلب رمز التحقق من تليجرام...",
            ["failed_send_code"] = "فشل إرسال الكود. تحقق من البيانات ورقم الهاتف.",
            ["api_id_required"] = "معرف وهاش الـ API مطلوبان أولاً.",
            ["api_id_numeric"] = "يجب أن يكون معرف الـ API قيمة رقمية.",
            ["phone_required"] = "يجب إدخال رقم الهاتف الدولي.",
            ["otp_title"] = "التحقق من الكود",
            ["otp_subtitle"] = "تم إرسال الكود إلى تطبيق تليجرام الخاص بك: ",
            ["otp_label"] = "كود التحقق (OTP)",
            ["otp_placeholder"] = "أدخل الكود المستلم",
            ["pwd_label"] = "رمز التحقق بخطوتين (اختياري)",
            ["pwd_placeholder"] = "أدخل كلمة المرور في حال تفعيل التحقق بخطوتين",
            ["verify_login"] = "التحقق وتسجيل الدخول",
            ["back_to_login"] = "العودة لتسجيل الدخول",
            ["verifying_auth"] = "جاري التحقق من الهوية...",
            ["pwd_required_msg"] = "رمز التحقق بخطوتين (2FA) مطلوب. أدخله أدناه.",
            ["verification_failed"] = "فشل التحقق: ",
            ["db_title"] = "مساحة تخزين تليجرام",
            ["online"] = "نشط",
            ["saved_messages"] = "الرسائل المحفوظة",
            ["cloud_backend"] = "مساحة التخزين السحابية",
            ["local_disk"] = "مستكشف القرص المحلي",
            ["local_backend"] = "مساحة التخزين المحلية",
            ["unlimited_cloud"] = "نشط • تخزين سحابي غير محدود",
            ["local_explorer"] = "المستكشف المحلي • القرص المحلي",
            ["upload_file"] = "⬆️ رفع ملف",
            ["new_folder"] = "📁 مجلد جديد",
            ["sync_cloud"] = "🔄 مزامنة السحاب",
            ["search_placeholder"] = "بحث",
            ["refresh"] = "تحديث",
            ["filter_all"] = "الكل",
            ["filter_docs"] = "المستندات",
            ["filter_media"] = "الوسائط",
            ["filter_zips"] = "الأرشيف",
            ["filter_others"] = "أخرى",
            ["set_backup"] = "⚙️  إعداد مجلد النسخ الاحتياطي التلقائي",
            ["set_downloads"] = "📥  إعداد مجلد التنزيلات",
            ["disconnect"] = "فصل الحساب والخروج",
            ["confirm_logout"] = "هل أنت متأكد من فصل الحساب؟ سيتم حفظ جلسة تسجيل الدخول بأمان للوصول السريع لاحقاً.",
            ["confirm_delete_folder"] = "هل أنت متأكد من حذف المجلد ",
            ["confirm_delete_title"] = "تأكيد الحذف",
            ["delete_btn"] = "حذف",
            ["cancel_btn"] = "إلغاء",
            ["welcome_back"] = "مرحباً بك مجدداً",
            ["select_account"] = "اختر حساباً لتسجيل الدخول الفوري.",
            ["login_btn"] = "دخول",
            ["use_another_account"] = "تسجيل الدخول بحساب آخر",
            ["back_to_accounts"] = "⬅️ العودة للحسابات المحفوظة",
            ["confirm_remove_account"] = "هل أنت متأكد من حذف جلسة تسجيل الدخول هذه؟ سيتم إزالة جميع البيانات المحلية لهذا المستخدم لمنع أي شخص آخر من الدخول."
        }
    };

    public static string Get(string key)
    {
        if (Translations.TryGetValue(CurrentLanguage, out var langDict))
        {
            if (langDict.TryGetValue(key, out var val))
            {
                return val;
            }
        }
        
        // Fallback to English, then key itself
        if (Translations["en"].TryGetValue(key, out var fallbackVal))
        {
            return fallbackVal;
        }
        
        return key;
    }
}
