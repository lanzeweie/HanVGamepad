export type Language = 'ar' | 'en' | 'zh';

const ar = {
    // Connection status
    connected: 'متصل',
    connecting: 'جاري...',
    disconnected: 'غير متصل',

    // TopBar buttons
    hide: 'إخفاء القائمة',
    minimize: 'نافذة',
    fullscreen: 'شاشة كاملة',
    done: 'تم',
    edit: 'تعديل التخطيط',
    showTouchArea: 'إظهار مناطق اللمس',
    buttons: 'الأزرار',
    leftStick: 'عصا اليسار',
    rightStick: 'عصا اليمين',

    // Menu
    reset: 'إعادة ضبط التخطيط',
    saveLayout: 'حفظ التخطيط',
    selectLayout: 'اختيار التخطيط',
    layoutNamePrompt: 'أدخل اسم التخطيط:',
    saveLayoutFailed: 'فشل حفظ التخطيط',
    loadLayoutsFailed: 'فشل تحميل التخطيطات',
    noSavedLayouts: 'لا توجد تخطيطات محفوظة على الخادم بعد',
    connection: 'اتصال',
    menu: 'قائمة التخطيط',
    showBar: 'إظهار الشريط',

    // Settings Modal
    controllerType: 'نوع ذراع التحكم',
    typeXbox360: 'Xbox 360',
    typeDualShock4: 'DualShock 4',
    enableVibration: 'تفعيل الاهتزاز',
    on: 'مفعّل',
    off: 'معطّل',
    show: 'إظهار',
    hideControl: 'إخفاء',
    sensitivity: 'حساسية الأزرار/العصا',
    password: 'كلمة المرور (اختياري)',
    passwordPlaceholder: 'اتركها فارغة إذا لم يكن هناك باسورد',
    connect: 'اتصال',

    // Connection gate (未连接时的全屏覆盖层)
    gateTitle: 'الاتصال بالجهاز',
    gateLandscape: 'يُفضّل الاستخدام أفقيًا — اضغط «شاشة كاملة» للحصول على تجربة أفضل',

    // Stable reason codes from the server
    reasonSlotTaken: 'هذا المقعد مشغول، اختر مقعداً آخر',
    reasonNoFreeSlot: 'لا توجد مقاعد متاحة',
    reasonSlotInvalid: 'رقم المقعد غير صالح',
    reasonKickedByAdmin: 'تم قطع اتصالك من الخادم',
    connectFailed: 'فشل الاتصال، حاول مرة أخرى',
    authFailed: 'كلمة مرور الخادم غير صحيحة',

    // EditBar
    leftStickFull: 'عصا التحكم اليسرى (LS)',
    rightStickFull: 'عصا التحكم اليمنى (RS)',
    buttonPrefix: 'زر',
    size: 'الحجم',
    shape: 'الشكل',
    circular: '● دائري',
    rectangle: '■ مستطيل',
    position: 'الموقع',
    align: 'محاذاة',
    resetButton: 'إعادة تعيين الزر',
    closePanel: 'إغلاق',
    editHint: 'اختر أي زر بالضغط عليه لتغيير حجمه وشكله ومكانه بدقة',

    // Language menu
    language: '🌐 اللغة',
    arabic: 'العربية',
    english: 'English',
    chinese: '中文',
};

const en = {
    // Connection status
    connected: 'Connected',
    connecting: 'Connecting...',
    disconnected: 'Disconnected',

    // TopBar buttons
    hide: 'Hide Menu',
    minimize: 'Window',
    fullscreen: 'Fullscreen',
    done: 'Done',
    edit: 'Edit Layout',
    showTouchArea: 'Show Hitboxes',
    buttons: 'Buttons',
    leftStick: 'Left Stick',
    rightStick: 'Right Stick',

    // Menu
    reset: 'Reset Layout',
    saveLayout: 'Save Layout',
    selectLayout: 'Select Layout',
    layoutNamePrompt: 'Enter layout name:',
    saveLayoutFailed: 'Failed to save layout',
    loadLayoutsFailed: 'Failed to load layouts',
    noSavedLayouts: 'No layouts saved on the server yet',
    connection: 'Connection',
    menu: 'Layout Menu',
    showBar: 'Show Bar',

    // Settings Modal
    controllerType: 'Controller type',
    typeXbox360: 'Xbox 360',
    typeDualShock4: 'DualShock 4',
    enableVibration: 'Enable Vibration',
    on: 'On',
    off: 'Off',
    show: 'Show',
    hideControl: 'Hide',
    sensitivity: 'Button / Stick Sensitivity',
    password: 'Password (optional)',
    passwordPlaceholder: 'Leave empty if no password',
    connect: 'Connect',

    // Connection gate (未连接时的全屏覆盖层)
    gateTitle: 'Connect to device',
    gateLandscape: 'Landscape recommended — tap Fullscreen for a better experience',

    // Stable reason codes from the server
    reasonSlotTaken: 'This slot is in use, pick another one',
    reasonNoFreeSlot: 'No free slot available',
    reasonSlotInvalid: 'Invalid slot',
    reasonKickedByAdmin: 'The server disconnected you',
    connectFailed: 'Connection failed, please try again',
    authFailed: 'Wrong server password',

    // EditBar
    leftStickFull: 'Left Control Stick (LS)',
    rightStickFull: 'Right Control Stick (RS)',
    buttonPrefix: 'Button',
    size: 'Size',
    shape: 'Shape',
    circular: '● Circle',
    rectangle: '■ Rectangle',
    position: 'Position',
    align: 'Align',
    resetButton: 'Reset Button',
    closePanel: 'Close',
    editHint: 'Tap any button to adjust its size, shape, and position precisely',

    // Language menu
    language: '🌐 Language',
    arabic: 'العربية',
    english: 'English',
    chinese: '中文',
} as const;

export type Translations = typeof en;

// 键集合被 Translations 锁死：少一个键或多一个键都编译不过。
const zh = {
  // Connection status
  connected: '已连接',
  connecting: '连接中…',
  disconnected: '未连接',

  // TopBar buttons
  hide: '隐藏菜单',
  minimize: '窗口',
  fullscreen: '全屏',
  done: '完成',
  edit: '编辑布局',
  showTouchArea: '显示触摸区',
  buttons: '按键',
  leftStick: '左摇杆',
  rightStick: '右摇杆',

  // Menu
  reset: '重置布局',
  saveLayout: '保存方案',
  selectLayout: '选择方案',
  layoutNamePrompt: '输入方案名称：',
  saveLayoutFailed: '保存方案失败',
  loadLayoutsFailed: '读取方案失败',
  noSavedLayouts: '服务端还没有保存的方案',
  connection: '连接',
  menu: '布局菜单',
  showBar: '显示顶栏',

  // Settings Modal
  controllerType: '手柄类型',
  typeXbox360: 'Xbox 360',
  typeDualShock4: 'DualShock 4',
  enableVibration: '启动振动',
  on: '开',
  off: '关',
  show: '显示',
  hideControl: '隐藏',
  sensitivity: '按键 / 摇杆灵敏度',
  password: '密码（可选）',
  passwordPlaceholder: '没有密码就留空',
  connect: '连接',

  // Connection gate (未连接时的全屏覆盖层)
  gateTitle: '连接设备',
  gateLandscape: '建议横屏使用，点击全屏获得更好体验',

  // Stable reason codes from the server
  reasonSlotTaken: '该槽位已被占用，请选其他槽位',
  reasonNoFreeSlot: '没有空闲槽位了',
  reasonSlotInvalid: '槽位号无效',
  reasonKickedByAdmin: '你已被管理员踢出',
  connectFailed: '连接失败，请重试',
  authFailed: '服务器密码不正确',

  // EditBar
  leftStickFull: '左控制摇杆（LS）',
  rightStickFull: '右控制摇杆（RS）',
  buttonPrefix: '按键',
  size: '大小',
  shape: '形状',
  circular: '● 圆形',
  rectangle: '■ 方形',
  position: '位置',
  align: '对齐',
  resetButton: '重置按键',
  closePanel: '关闭',
  editHint: '点选任意按键，可精确调整它的大小、形状和位置',

  // Language menu
  language: '🌐 语言',
  arabic: 'العربية',
  english: 'English',
  chinese: '中文',
} satisfies Record<keyof Translations, string>;

export const translations = { ar, en, zh } as const;

export type TranslationSet = typeof translations[Language];
