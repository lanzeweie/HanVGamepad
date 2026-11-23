namespace HanVGamepad.Desktop.Services
{
    /// <summary>
    /// 界面文案的唯一出处（文案集中取，不许散落字面量）。
    /// 中文/英文/阿拉伯文一一对应；缺键时回退到键名，方便一眼看出漏了哪条。
    /// </summary>
    public static class Strings
    {
        private static readonly Dictionary<string, (string Zh, string En, string Ar)> Map =
            new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
            {
                // 电源与状态
                ["power.start"] = ("启动服务", "Start", "تشغيل الخدمة"),
                ["power.stop"] = ("停止服务", "Stop", "إيقاف الخدمة"),
                ["power.starting"] = ("启动中…", "Starting…", "جارٍ التشغيل…"),
                ["power.stopping"] = ("停止中…", "Stopping…", "جارٍ الإيقاف…"),
                ["state.running"] = ("服务运行中", "Service running", "الخدمة قيد التشغيل"),
                ["state.stopped"] = ("服务已停止", "Service stopped", "الخدمة متوقفة"),
                ["state.starting"] = ("服务启动中", "Service starting", "الخدمة قيد البدء"),
                ["state.stopping"] = ("服务停止中", "Service stopping", "الخدمة قيد الإيقاف"),
                ["state.stoppedHint"] = ("启动后手机即可连接", "Start to let phones connect", "شغّل الخدمة ليتصل الهاتف"),
                ["uptime"] = ("已运行 {0}", "up {0}", "مدة التشغيل {0}"),

                // 地址与设置
                ["addr.title"] = ("手机与主机需在同一 Wi-Fi/局域网，浏览器访问以下地址", "Phone and PC must be on the same Wi-Fi/LAN — open this address in the browser", "يجب أن يكون الهاتف والكمبيوتر على نفس شبكة Wi-Fi/LAN — افتح العنوان التالي في المتصفح"),
                ["addr.hint"] = ("手机与电脑需连接同一 Wi-Fi / 局域网", "Phone and PC must share the same Wi-Fi / LAN.", "يجب أن يتصل الهاتف والكمبيوتر بنفس شبكة Wi-Fi / LAN."),
                ["addr.copy"] = ("复制地址", "Copy", "نسخ العنوان"),
                ["addr.port"] = ("服务器端口", "Port", "منفذ الخادم"),
                ["addr.password"] = ("访问密码", "Password", "كلمة المرور"),
                ["addr.show"] = ("显示", "Show", "إظهار"),
                ["addr.hide"] = ("隐藏", "Hide", "إخفاء"),
                ["config.title"] = ("配置", "Settings", "الإعدادات"),
                ["lang.label"] = ("界面语言", "Language", "لغة الواجهة"),
                ["theme.label"] = ("主题", "Theme", "السمة"),
                ["theme.light"] = ("浅色", "Light", "فاتح"),
                ["theme.dark"] = ("深色", "Dark", "داكن"),
                ["status.online"] = ("{0} 台设备在线 · 局域网可达", "{0} devices online · reachable on LAN", "{0} جهاز متصل · متاح عبر الشبكة المحلية"),
                ["platform.pretend"] = ("测试模式：便携平台", "Test mode: portable platform", "وضع الاختبار: منصة محمولة"),

                // 槽位
                ["slot.section"] = ("手柄槽位", "Controller slots", "خانات ذراع التحكم"),
                ["slot.count"] = ("{0}/{1} 已连接", "{0}/{1} connected", "{0}/{1} متصل"),
                ["slot.systemPads"] = ("系统手柄 {0}", "{0} system controller(s)", "ذراع نظام {0}"),
                ["slot.systemPad"] = ("系统手柄占用", "Used by a system controller", "محجوز لذراع نظام"),
                ["slot.systemPadHint"] = ("已被系统手柄占用", "Used by a system controller", "مستخدم من ذراع نظام"),
                ["slot.padLimit"] = ("本平台最多支持同时使用 4 个手柄（含真实手柄）", "At most 4 controllers can be used at the same time here, physical ones included", "4 أذرع كحد أقصى في آن واحد، مع الأذرع الحقيقية"),
                ["slot.add"] = ("新增手柄", "Add controller", "إضافة ذراع"),
                ["slot.remove"] = ("删除手柄", "Remove controller", "إزالة ذراع"),
                ["slot.player"] = ("手柄 {0}", "Controller {0}", "ذراع التحكم {0}"),
                ["slot.onlineSuffix"] = ("已连接", "connected", "متصل"),
                ["slot.waiting"] = ("等待设备重连", "Waiting for reconnect", "في انتظار إعادة الاتصال"),
                ["slot.waitingDevice"] = ("等待设备接入", "Waiting for a device", "في انتظار جهاز"),
                ["slot.connected"] = ("已连接", "Connected", "متصل"),
                ["slot.idle"] = ("空闲", "Idle", "متاح"),
                ["slot.disconnect"] = ("断开连接", "Disconnect", "قطع الاتصال"),
                ["slot.delete"] = ("卸载", "Uninstall", "إزالة"),
                ["type.xbox"] = ("Xbox 360 手柄", "Xbox 360 controller", "ذراع تحكم Xbox 360"),
                ["type.ds4"] = ("DualShock 4 手柄", "DualShock 4 controller", "ذراع تحكم DualShock 4"),
                ["layout.xbox"] = ("XInput", "XInput", "XInput"),
                ["layout.ds4"] = ("PS4 布局", "PS4 layout", "تخطيط PS4"),

                // 二维码与托盘
                ["qr.hint.stop"] = ("启动服务后可扫码", "Available once running", "متاح بعد تشغيل الخدمة"),
                ["qr.title"] = ("扫码连接", "Scan to connect", "امسح للاتصال"),
                ["tray.show"] = ("显示窗口", "Show window", "إظهار النافذة"),
                ["tray.exit"] = ("退出", "Exit", "خروج"),
                ["tray.minimized.title"] = ("HanVGamepad 仍在运行", "HanVGamepad is still running", "HanVGamepad لا يزال قيد التشغيل"),
                ["tray.minimized.text"] = ("已最小化到通知区域，服务保持运行。点托盘图标即可恢复窗口。", "Minimized to the notification area — the server keeps running. Click the tray icon to reopen.", "تم التصغير إلى منطقة الإشعارات — الخادم يعمل. اضغط أيقونة شريط النظام لاستعادة النافذة."),

                // 驱动管理
                ["driver.section"] = ("驱动管理", "Driver management", "إدارة التعريفات"),
                ["driver.status"] = ("驱动状态", "Driver status", "حالة التعريف"),
                ["driver.installed"] = ("已安装", "Installed", "مثبت"),
                ["driver.notInstalled"] = ("未安装", "Not installed", "غير مثبت"),
                ["driver.available"] = ("可用", "Available", "متاح"),
                ["driver.unavailable"] = ("不可用", "Unavailable", "غير متاح"),
                ["driver.serviceRunning"] = ("驱动服务运行中", "Driver service running", "خدمة التعريف تعمل"),
                ["driver.serviceStopped"] = ("驱动服务未运行", "Driver service not running", "خدمة التعريف متوقفة"),
                ["driver.version"] = ("版本 {0}", "Version {0}", "الإصدار {0}"),
                ["driver.permissionOk"] = ("权限正常", "Permissions OK", "الأذونات سليمة"),
                ["driver.needsElevation"] = ("需要管理员权限", "Administrator rights required", "مطلوب صلاحيات المسؤول"),
                ["driver.permissionDenied"] = ("权限被拒绝", "Permission denied", "تم رفض الأذونات"),
                ["driver.signatureBlocked"] = ("驱动签名被拦截", "Driver signature blocked", "تم حظر توقيع التعريف"),
                ["driver.moduleNotLoaded"] = ("内核模块未加载", "Kernel module not loaded", "وحدة النواة غير محمّلة"),
                ["driver.install"] = ("安装驱动", "Install driver", "تثبيت التعريف"),
                ["driver.installOjd"] = ("安装 OpenJoystickDriver", "Install OpenJoystickDriver", "تثبيت OpenJoystickDriver"),
                ["driver.uninstall"] = ("卸载驱动", "Uninstall driver", "إزالة التعريف"),
                ["driver.installing"] = ("安装中…", "Installing…", "جارٍ التثبيت…"),
                ["driver.uninstalling"] = ("卸载中…", "Uninstalling…", "جارٍ الإزالة…"),
                ["driver.refresh"] = ("刷新状态", "Refresh", "تحديث"),
                ["driver.installOk"] = ("驱动安装完成", "Driver installed", "تم تثبيت التعريف"),
                ["driver.installFailed"] = ("驱动安装失败：{0}", "Driver install failed: {0}", "فشل تثبيت التعريف: {0}"),
                ["driver.ojdNotReady"] = ("OpenJoystickDriver 未就绪，按诊断行的提示完成安装或启动服务", "OpenJoystickDriver is not ready — follow the diagnostic line to install it or start its service", "OpenJoystickDriver غير جاهز — اتبع سطر التشخيص لإكمال التثبيت أو تشغيل الخدمة"),
                ["driver.uninstallOk"] = ("驱动已卸载", "Driver uninstalled", "تمت إزالة التعريف"),
                ["driver.uninstallFailed"] = ("驱动卸载失败：{0}", "Driver uninstall failed: {0}", "فشل إزالة التعريف: {0}"),
                ["driver.uninstallReboot"] = ("驱动已卸载，重启后完全生效", "Driver removed — reboot to finish", "تمت إزالة التعريف — أعد التشغيل لإكمال الإزالة"),
                ["driver.notReady"] = ("驱动未就绪，手柄功能不可用", "Driver not ready — gamepad features unavailable", "التعريف غير جاهز — ميزات ذراع التحكم غير متاحة"),
                ["driver.manualHint"] = ("按提示在终端中以管理员权限执行命令", "Run the commands above in an elevated terminal", "نفّذ الأوامر أعلاه في طرفية بصلاحيات مسؤول"),

                // 系统弹窗
                ["dialog.driverRequired.title"] = ("缺少虚拟手柄驱动", "Gamepad driver required", "مطلوب تعريف ذراع التحكم"),
                ["dialog.driverRequired.text"] = ("需安装虚拟手柄驱动后方可启动服务并正常使用。点击「安装驱动」会打开 ViGEmBus 官方安装程序，请在向导中阅读并同意许可协议后完成安装。", "The virtual gamepad driver is required to start the server. \"Install driver\" opens the official ViGEmBus setup wizard — review and accept its license there to finish the installation.", "يلزم تثبيت تعريف ذراع التحكم لتشغيل الخادم. سيؤدي «تثبيت التعريف» إلى فتح برنامج تثبيت ViGEmBus الرسمي — راجع اتفاقية الترخيص ووافق عليها هناك لإكمال التثبيت."),
                ["dialog.driverRequired.install"] = ("安装驱动", "Install driver", "تثبيت التعريف"),
                ["dialog.driverRequired.cancel"] = ("取消", "Cancel", "إلغاء"),
                ["dialog.ojdRequired.title"] = ("需要 OpenJoystickDriver", "OpenJoystickDriver required", "مطلوب OpenJoystickDriver"),
                ["dialog.ojdRequired.text"] = ("macOS 上由 OpenJoystickDriver 负责发布虚拟手柄，必须先安装它才能启动服务。是否打开下载页？", "On macOS, OpenJoystickDriver publishes the virtual gamepad. Install it before starting the server. Open the download page?", "على macOS ينشر OpenJoystickDriver ذراع التحكم الافتراضي. ثبّته قبل تشغيل الخادم. هل تريد فتح صفحة التنزيل؟"),
                ["dialog.ojdNotRunning.text"] = ("OpenJoystickDriver 已安装，但它的后台服务没有运行，启动服务需要先把它拉起来。是否现在启动？", "OpenJoystickDriver is installed, but its background service is not running. Start it now?", "OpenJoystickDriver مثبت، لكن خدمته الخلفية لا تعمل. هل تريد تشغيلها الآن؟"),
                ["dialog.ojd.action.install"] = ("打开下载页", "Open download page", "فتح صفحة التنزيل"),
                ["dialog.ojd.action.start"] = ("启动 OJD 服务", "Start OJD service", "تشغيل خدمة OJD"),
                ["dialog.driverUnusable.text"] = ("驱动已安装但当前不可用（服务未运行或权限不足），启动后无法创建手柄。是否尝试重新安装驱动？", "The driver is installed but not usable right now (service stopped or permission denied), so controllers cannot be created. Reinstall it?", "التعريف مثبت لكنه غير قابل للاستخدام حاليًا (الخدمة متوقفة أو الأذونات ناقصة). هل تريد إعادة تثبيته؟"),
                ["dialog.driverMissing.title"] = ("运行环境错误", "Runtime error", "خطأ في بيئة التشغيل"),
                ["dialog.driverMissing.text"] = ("本应用需要虚拟手柄驱动才能正常工作，请先安装驱动。", "HanVGamepad needs the gamepad driver to work. Please install the driver first.", "يتطلب HanVGamepad تعريف ذراع التحكم للعمل. يرجى تثبيت التعريف أولاً."),
                ["dialog.driverInstallFailed.title"] = ("系统错误", "System error", "خطأ في النظام"),
                ["dialog.driverInstallFailed.text"] = ("驱动自动安装失败：{0}", "Failed to install the driver automatically: {0}", "فشل التثبيت التلقائي للتعريف: {0}"),
                ["dialog.alreadyRunning.title"] = ("已经在运行", "Already running", "يعمل بالفعل"),
                ["dialog.alreadyRunning.text"] = ("HanVGamepad 已经有一个实例在运行，去通知区域找它的图标。", "HanVGamepad is already running — look for its icon in the notification area.", "HanVGamepad يعمل بالفعل — ابحث عن أيقونته في شريط النظام."),

                // 反馈（toast）
                ["toast.copied"] = ("已复制 {0}", "Copied {0}", "تم نسخ {0}"),
                ["toast.noIp"] = ("本机没有可用的 IPv4 地址", "No IPv4 address available", "لا يوجد عنوان IPv4 متاح"),
                ["toast.copyFailed"] = ("复制失败：{0}", "Copy failed: {0}", "فشل النسخ: {0}"),
                ["toast.portInvalid"] = ("端口必须在 1-65535 之间", "Port must be between 1 and 65535", "يجب أن يكون المنفذ بين 1 و 65535"),
                ["toast.startFailed"] = ("启动失败：{0}", "Start failed: {0}", "فشل التشغيل: {0}"),
                ["toast.selfCheckFailed"] = ("启动自检未通过：{0}", "Startup self-check failed: {0}", "فشل الفحص الذاتي عند البدء: {0}"),
                ["toast.stopFailed"] = ("停止失败：{0}", "Stop failed: {0}", "فشل الإيقاف: {0}"),
                ["toast.settingsSaved"] = ("设置已保存", "Settings saved", "تم حفظ الإعدادات"),
                ["toast.settingsLocked"] = ("服务运行中不能修改端口或密码，请先停止服务", "Stop the service before changing port or password", "أوقف الخدمة قبل تغيير المنفذ أو كلمة المرور"),
                ["toast.slotBusy"] = ("槽位 {0} 正在使用，先断开设备再卸载", "Slot {0} is in use — disconnect first", "الخانة {0} مستخدمة — اقطع الاتصال أولًا"),
                ["toast.deviceRemoved"] = ("已卸载槽位 {0} 的设备", "Removed the device on slot {0}", "تمت إزالة جهاز الخانة {0}"),
                ["toast.slotInvalid"] = ("无效的槽位", "Invalid slot", "خانة غير صالحة"),
                ["toast.notRunning"] = ("服务未运行", "Service is not running", "الخدمة ليست قيد التشغيل"),
                ["toast.noDevice"] = ("槽位 {0} 没有已连接的设备", "Slot {0} has no device", "لا يوجد جهاز متصل في الخانة {0}"),
                ["toast.kickOk"] = ("已踢出槽位 {0} 的连接", "Kicked the connection on slot {0}", "تم قطع الاتصال في الخانة {0}"),
                ["toast.saveFailed"] = ("设置保存失败：无法写入 {0}", "Could not write {0}", "تعذر حفظ الإعدادات: تعذرت الكتابة في {0}")
            };

        /// <summary>取一条文案；lang 不是 en / ar 时一律按中文处理。</summary>
        public static string T(string lang, string key)
        {
            if (!Map.TryGetValue(key, out var triple))
            {
                return key;
            }

            if (string.Equals(lang, Languages.English, StringComparison.OrdinalIgnoreCase))
            {
                return triple.En;
            }

            if (string.Equals(lang, Languages.Arabic, StringComparison.OrdinalIgnoreCase))
            {
                return triple.Ar;
            }

            return triple.Zh;
        }

        public static string T(string lang, string key, params object[] args)
        {
            string format = T(lang, key);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        public static string TypeLabel(string lang, string type)
        {
            return T(lang, "type." + NormalizeType(type));
        }

        public static string TypeLayout(string lang, string type)
        {
            return T(lang, "layout." + NormalizeType(type));
        }

        /// <summary>语言自身名字不随界面语言变化，下拉菜单直接用这个。</summary>
        public static string LanguageName(string code)
        {
            if (string.Equals(code, Languages.English, StringComparison.OrdinalIgnoreCase))
            {
                return "English";
            }

            if (string.Equals(code, Languages.Arabic, StringComparison.OrdinalIgnoreCase))
            {
                return "العربية";
            }

            return "中文";
        }

        /// <summary>该语言是否从右往左读。阿语是 RTL：文字要贴右排，布局也要镜像。</summary>
        public static bool IsRtl(string lang)
        {
            return string.Equals(lang, Languages.Arabic, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 给一段文案套上 Unicode 方向标记（U+202B RLE + U+202C PDF）。
        /// 用于系统通知气泡这类由系统渲染的界面：排版方向由系统语言决定，
        /// 方向标记是显式声明「从右往左」的唯一办法。标记本身不占位也不画出笔画。
        /// </summary>
        public static string WithDirectionMarks(string text, string lang)
        {
            if (string.IsNullOrEmpty(text) || !IsRtl(lang))
            {
                return text;
            }

            return "\u202B" + text + "\u202C";
        }

        private static string NormalizeType(string type)
        {
            return string.Equals(type, "ds4", StringComparison.OrdinalIgnoreCase) ? "ds4" : "xbox";
        }

        public static string IpLabel(string lang, string kind)
        {
            if (string.Equals(lang, Languages.Arabic, StringComparison.OrdinalIgnoreCase))
            {
                return kind switch
                {
                    "wifi" => "Wi-Fi",
                    "ethernet" => "إيثرنت",
                    _ => "أخرى"
                };
            }

            bool en = string.Equals(lang, Languages.English, StringComparison.OrdinalIgnoreCase);
            return kind switch
            {
                "wifi" => "Wi-Fi",
                "ethernet" => en ? "Ethernet" : "以太网",
                _ => en ? "Other" : "其它"
            };
        }
    }
}
