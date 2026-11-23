"""
从 HanVGamepad 源图生成全部图标素材。

源图: docs/png/Han-1024x1024.png（单一源，不分深色/浅色）
输出:
  - docs/png/                                     MSIX / 商店素材全套
  - src/desktop/Resources/HanVGamepadIcon.ico     托盘、窗口标题栏、两个 exe 的图标
  - web-client/public/iconWeb.png                 web 端 favicon（index.html 引用）

关键: MSIX 图标内容必须占画布 75-85%，否则 Windows 会自动补背景板。

相较 Macast-Han 版本的改动:
  - 只用一张源图，altform-colorful_theme-light / StoreLogo 也用同一张
  - Wide310x150Logo 不再手工准备（原脚本被注释掉），改为等比 contain + 边缘延展
  - 补上 altform-lightunplated_targetsize-*（原脚本只生成了 altform-unplated）
  - 应用内图标（.ico / favicon）也在这里生成，换图标只需重跑本脚本
"""

from PIL import Image
import os

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUTPUT_DIR = os.path.join(PROJECT, "docs", "png")
SRC = os.path.join(OUTPUT_DIR, "Han-1024x1024.png")
APP_ID = "HANVGAMEPAD"  # 文件名前缀
FILL_RATIO = 0.80

# 应用内图标：换源图后重跑本脚本即可，不要再手改这两个文件
DESKTOP_ICON = os.path.join(PROJECT, "src", "desktop", "Resources", "HanVGamepadIcon.ico")
WEB_ICON = os.path.join(PROJECT, "web-client", "public", "iconWeb.png")
ICO_SIZES = [
    (16, 16), (20, 20), (24, 24), (32, 32), (40, 40),
    (48, 48), (64, 64), (128, 128), (256, 256),
]

# 标题栏小图标：只给 Win32 ICON_SMALL 用，源图是 docs/png/icon.png（透明手柄）
TITLE_SRC = os.path.join(OUTPUT_DIR, "icon.png")
TITLE_ICON = os.path.join(PROJECT, "src", "desktop", "Assets", "TitleIcon.ico")
TITLE_ICON_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (48, 48)]
WEB_ICON_SIZE = 512

# Square44x44Logo 的 targetsize 变体尺寸
TARGET_SIZES = [16, 20, 24, 32, 44, 48, 256]

# (宽, 高, scale)
SCALES_44 = [(44, 44, "100"), (55, 55, "125"), (66, 66, "150"), (88, 88, "200"), (176, 176, "400")]
SCALES_71 = [(71, 71, "100"), (89, 89, "125"), (107, 107, "150"), (142, 142, "200"), (284, 284, "400")]
SCALES_150 = [(150, 150, "100"), (188, 188, "125"), (225, 225, "150"), (300, 300, "200"), (600, 600, "400")]
SCALES_310 = [(310, 310, "100"), (388, 388, "125"), (465, 465, "150"), (620, 620, "200"), (1240, 1240, "400")]
SCALES_WIDE = [(310, 150, "100"), (388, 188, "125"), (465, 225, "150"), (620, 300, "200"), (1240, 600, "400")]
SCALES_STORE = [(50, 50, "100"), (63, 63, "125"), (75, 75, "150"), (100, 100, "200"), (200, 200, "400")]


def prepare_icon(source: Image.Image, fill_ratio: float = FILL_RATIO) -> Image.Image:
    """
    将图标放大到 fill_ratio 占画布比例，让 Windows 不自动补背景板。
    Windows 对比度检测要求图标主体占画布 75-85%。
    """
    rgba = source.convert("RGBA")
    # 计算实际内容的边界框（排除全透明像素）
    bbox = rgba.getbbox()
    if bbox is None:
        return rgba
    content_w = bbox[2] - bbox[0]
    content_h = bbox[3] - bbox[1]
    # 当前内容占画布比例
    current_ratio = max(content_w / rgba.width, content_h / rgba.height)
    if current_ratio >= fill_ratio:
        return rgba
    # 放大到目标比例
    scale = fill_ratio / current_ratio
    new_w = int(rgba.width * scale)
    new_h = int(rgba.height * scale)
    resized = rgba.resize((new_w, new_h), Image.LANCZOS)
    # 居中粘贴到原始尺寸画布
    canvas = Image.new("RGBA", rgba.width, rgba.height, (0, 0, 0, 0))
    offset_x = (rgba.width - new_w) // 2
    offset_y = (rgba.height - new_h) // 2
    canvas.paste(resized, (offset_x, offset_y))
    return canvas


def _solid_extent(px, y: int, w: int, threshold: int = 250):
    """返回该行最左/最右实色像素的 x，整行没有实色则返回 None。"""
    left = None
    for x in range(w):
        if px[x, y][3] >= threshold:
            left = x
            break
    if left is None:
        return None
    right = None
    for x in range(w - 1, -1, -1):
        if px[x, y][3] >= threshold:
            right = x
            break
    return left, right


def _luma(color) -> float:
    return 0.299 * color[0] + 0.587 * color[1] + 0.114 * color[2]


def _is_background(color) -> bool:
    """
    判断是否橙色底本身。

    底色是高饱和高亮度的橙（S≈0.7-0.95，V≈0.9）；
    圆角高光描边 S≈0.25，与底色的过渡像素 S≈0.5-0.6，
    深色卡片 V≈0.43，手柄 S≈0.1，都会被排除。
    """
    high = max(color)
    if high == 0:
        return False
    saturation = (high - min(color)) / high
    return saturation >= 0.6 and high >= 178  # V >= 0.7


def _row_background(px, y: int, start: int, step: int, w: int, max_scan: int):
    """从行的最外实色像素向内扫，取第一块橙色底；找不到返回 None。"""
    for i in range(max_scan):
        x = start + step * i
        if not 0 <= x < w:
            break
        c = px[x, y]
        if c[3] >= 250 and _is_background(c[:3]):
            return c[:3]
    return None


def _fill_missing(colors):
    """把 None 行用最近的有效行颜色补齐。"""
    n = len(colors)
    known = [i for i in range(n) if colors[i] is not None]
    if not known:
        return colors
    out = list(colors)
    for i in range(n):
        if out[i] is None:
            near = min(known, key=lambda v: abs(v - i))
            out[i] = colors[near]
    return out


def _median_smooth(colors, window: int):
    """垂直中值平滑：压掉圆角处逐行交替的亮暗采样噪声，渐变本身不受影响。"""
    if window <= 1 or len(colors) <= 1:
        return colors
    half = window // 2
    n = len(colors)
    out = []
    for i in range(n):
        seg = sorted(colors[max(0, i - half):min(n, i + half + 1)], key=_luma)
        out.append(seg[len(seg) // 2])
    return out


def extend_edges_horizontal(img: Image.Image) -> Image.Image:
    """
    每行取图标左右边缘的橙色底，向左右铺到画布边界。

    宽磁贴两侧不留透明，用图标自身的边缘颜色延展满。

    取色不能只拿"最外那一个像素"，也不能拿固定宽的色带：
      - 圆角高光描边（(249,220,187)）会在部分行成为最外像素，直铺就是亮横条；
      - 上下圆角处最外像素离手柄很近，色带会把手柄深色当底色。
    所以这里按饱和度/明度筛选"橙色底"，跳过描边与图形；
    整行都看不到底色的（被手柄盖住）用相邻有效行补齐，再做轻度垂直中值平滑。
    """
    w, h = img.size
    px = img.load()
    extents = [_solid_extent(px, y, w) for y in range(h)]
    solid_rows = [y for y in range(h) if extents[y] is not None]
    if not solid_rows:
        return img

    # 向内扫描上限：够越过圆角描边，又不会深入到手柄/卡片
    max_scan = max(4, h // 15)
    left_colors = [None] * h
    right_colors = [None] * h
    for y in solid_rows:
        left, right = extents[y]
        left_colors[y] = _row_background(px, y, left, 1, w, max_scan)
        right_colors[y] = _row_background(px, y, right, -1, w, max_scan)
    left_colors = _fill_missing(left_colors)
    right_colors = _fill_missing(right_colors)

    window = max(5, (h // 60) | 1)
    left_colors = _median_smooth(left_colors, window)
    right_colors = _median_smooth(right_colors, window)

    for y in range(h):
        left_color = tuple(left_colors[y]) + (255,)
        right_color = tuple(right_colors[y]) + (255,)
        if extents[y] is None:
            for x in range(w):
                px[x, y] = left_color
            continue
        left, right = extents[y]
        for x in range(left):
            px[x, y] = left_color
        for x in range(right + 1, w):
            px[x, y] = right_color
    return img


def fit_wide(source: Image.Image, w: int, h: int) -> Image.Image:
    """方形源图等比缩放进 w×h 画布并居中，两侧用边缘像素延展填满。"""
    scale = min(w / source.width, h / source.height)
    new_w = max(1, int(source.width * scale))
    new_h = max(1, int(source.height * scale))
    resized = source.resize((new_w, new_h), Image.LANCZOS)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.alpha_composite(resized, ((w - new_w) // 2, (h - new_h) // 2))
    return extend_edges_horizontal(canvas)


def save(source: Image.Image, filename: str, w: int, h: int):
    filepath = os.path.join(OUTPUT_DIR, filename)
    source.resize((w, h), Image.LANCZOS).save(filepath, "PNG")
    print(f"  [OK] {filename} ({w}x{h})")


def save_wide(source: Image.Image, filename: str, w: int, h: int):
    filepath = os.path.join(OUTPUT_DIR, filename)
    fit_wide(source, w, h).save(filepath, "PNG")
    print(f"  [OK] {filename} ({w}x{h})")


def generate_all():
    icon = prepare_icon(Image.open(SRC))
    print(f"Source: {icon.size} ({SRC})")

    os.makedirs(OUTPUT_DIR, exist_ok=True)

    count = 0

    # --- Square44x44Logo ---
    for w, h, s in SCALES_44:
        save(icon, f"{APP_ID}-Square44x44Logo.scale-{s}.png", w, h)
        count += 1
    save(icon, f"{APP_ID}-Square44x44.png", 44, 44)
    save(icon, f"{APP_ID}-Square44x44Logo.png", 44, 44)
    count += 2
    for sz in TARGET_SIZES:
        save(icon, f"{APP_ID}-Square44x44Logo.targetsize-{sz}.png", sz, sz)
        save(icon, f"{APP_ID}-Square44x44Logo.altform-unplated_targetsize-{sz}.png", sz, sz)
        save(icon, f"{APP_ID}-Square44x44Logo.altform-lightunplated_targetsize-{sz}.png", sz, sz)
        count += 3

    # --- Square71x71Logo ---
    for w, h, s in SCALES_71:
        save(icon, f"{APP_ID}-Square71x71Logo.scale-{s}.png", w, h)
        save(icon, f"{APP_ID}-Square71x71Logo.scale-{s}_altform-unplated.png", w, h)
        count += 2
    save(icon, f"{APP_ID}-Square71x71.png", 71, 71)
    save(icon, f"{APP_ID}-Square71x71Logo.png", 71, 71)
    count += 2

    # --- Square150x150Logo (Start Menu 网格视图) ---
    for w, h, s in SCALES_150:
        save(icon, f"{APP_ID}-Square150x150Logo.scale-{s}.png", w, h)
        save(icon, f"{APP_ID}-Square150x150Logo.scale-{s}_altform-unplated.png", w, h)
        count += 2
    save(icon, f"{APP_ID}-Square150x150.png", 150, 150)
    save(icon, f"{APP_ID}-Square150x150Logo.png", 150, 150)
    count += 2

    # --- Square310x310Logo ---
    for w, h, s in SCALES_310:
        save(icon, f"{APP_ID}-Square310x310Logo.scale-{s}.png", w, h)
        save(icon, f"{APP_ID}-Square310x310Logo.scale-{s}_altform-unplated.png", w, h)
        count += 2
    save(icon, f"{APP_ID}-Square310x310.png", 310, 310)
    save(icon, f"{APP_ID}-Square310x310Logo.png", 310, 310)
    count += 2

    # --- Wide310x150Logo（等比 contain，不再手工铺图） ---
    for w, h, s in SCALES_WIDE:
        save_wide(icon, f"{APP_ID}-Wide310x150Logo.scale-{s}.png", w, h)
        save_wide(icon, f"{APP_ID}-Wide310x150Logo.scale-{s}_altform-unplated.png", w, h)
        count += 2
    save_wide(icon, f"{APP_ID}-Wide310x150.png", 310, 150)
    save_wide(icon, f"{APP_ID}-Wide310x150Logo.png", 310, 150)
    count += 2

    # --- MedTile: 全部使用同一张源图（不再区分主题深色版） ---
    for w, h, s in SCALES_150:
        save(icon, f"{APP_ID}-MedTile.scale-{s}.png", w, h)
        save(icon, f"{APP_ID}-MedTile.scale-{s}_altform-colorful_theme-light.png", w, h)
        count += 2

    # --- StoreLogo ---
    for w, h, s in SCALES_STORE:
        save(icon, f"StoreLogo.scale-{s}.png", w, h)
        count += 1
    save(icon, "StoreLogo.png", 512, 512)
    count += 1

    print(f"\n完成! 共生成 {count} 个图标文件 -> {OUTPUT_DIR}")

    write_app_assets(icon)
    write_title_icon()


def write_app_assets(icon: Image.Image):
    """
    写应用真正加载的图标（不是 MSIX 素材）。

    - .ico 供托盘（TrayController）、窗口（MainWindow.Icon）和两个 csproj
      的 ApplicationIcon 共用。小尺寸档必须齐全：通知区域在 100%/125%/
      150%/200% 缩放下分别取 16/20/24/32，任务栏按钮取 24-48，
      资源管理器大图标取 32-256；缺档时系统会拿相邻尺寸缩放，边缘发虚。
    - favicon 由 web-client/index.html 以 /iconWeb.png 引用，走 Vite public 目录。
    """
    os.makedirs(os.path.dirname(DESKTOP_ICON), exist_ok=True)
    icon.save(DESKTOP_ICON, format="ICO", sizes=ICO_SIZES)
    print(f"  [APP] {os.path.relpath(DESKTOP_ICON, PROJECT)} (ICO {len(ICO_SIZES)} 档)")

    os.makedirs(os.path.dirname(WEB_ICON), exist_ok=True)
    icon.resize((WEB_ICON_SIZE, WEB_ICON_SIZE), Image.LANCZOS).save(WEB_ICON, "PNG")
    print(f"  [APP] {os.path.relpath(WEB_ICON, PROJECT)} ({WEB_ICON_SIZE}x{WEB_ICON_SIZE})")


def write_title_icon():
    """
    标题栏小图标：源图改用 docs/png/icon.png（透明手柄，无橙色底板）。

    只服务 Win32 的 ICON_SMALL（窗口左上角那颗）；任务栏按钮走 ICON_BIG、
    托盘走 TrayController，两者都读 Resources/HanVGamepadIcon.ico，互不影响。
    """
    if not os.path.exists(TITLE_SRC):
        print(f"  [SKIP] 缺少 {os.path.relpath(TITLE_SRC, PROJECT)}，跳过标题栏图标")
        return
    os.makedirs(os.path.dirname(TITLE_ICON), exist_ok=True)
    Image.open(TITLE_SRC).convert("RGBA").save(TITLE_ICON, format="ICO", sizes=TITLE_ICON_SIZES)
    print(f"  [APP] {os.path.relpath(TITLE_ICON, PROJECT)} (标题栏 {len(TITLE_ICON_SIZES)} 档)")


if __name__ == "__main__":
    generate_all()
