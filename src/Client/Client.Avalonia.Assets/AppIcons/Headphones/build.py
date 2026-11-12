"""Build the FlowRate headphones identity assets using local SVG rendering."""
from pathlib import Path
import xml.etree.ElementTree as ET
import cairo
import gi
from PIL import Image

gi.require_version('Rsvg', '2.0')
from gi.repository import Rsvg

ROOT = Path(__file__).parent
SVG = ROOT / 'svg'
PNG = ROOT / 'png'
SVG.mkdir(exist_ok=True)
PNG.mkdir(exist_ok=True)
PURPLE = '#6C63FF'
COLORS = {'purple': PURPLE, 'white': '#FFFFFF', 'dark': '#232323'}
BODY = '<ns0:path xmlns:ns0="http://www.w3.org/2000/svg" d="M112 281V240C112 160 176 100 256 100S400 160 400 240V281" fill="none" stroke="currentColor" stroke-width="30" stroke-linecap="round" />\n  <ns0:g xmlns:ns0="http://www.w3.org/2000/svg" fill="currentColor">\n    <ns0:rect x="88" y="249" width="58" height="137" rx="27" />\n    <ns0:rect x="366" y="249" width="58" height="137" rx="27" />\n  </ns0:g>\n  <ns0:g xmlns:ns0="http://www.w3.org/2000/svg" fill="currentColor" transform="translate(83.1 139.2) scale(.66)">\n    <ns0:path d="M127 369V143H239V183H171V235H227V275H171V369Z" />\n    <ns0:path fill-rule="evenodd" d="M260 369V143H313C364 143 391 167 391 208C391 238 377 258 353 269L397 369H347L310 281H304V369ZM304 183V242H313C336 242 347 231 347 212C347 193 336 183 313 183Z" />\n  </ns0:g>\n'

def document(body, width=512, height=512, color=PURPLE, title='FlowRate'):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{title}"><title>{title}</title><g color="{color}">{body}</g></svg>\n'

def save(name, content):
    path = SVG / f'{name}.svg'
    path.write_text(content, encoding='utf-8')
    ET.parse(path)
    return path

def render(path, width, height=None, dest=None):
    if height is None:
        height = width
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, width, height)
    context = cairo.Context(surface)
    handle = Rsvg.Handle.new_from_file(str(path))
    viewport = Rsvg.Rectangle()
    viewport.x, viewport.y, viewport.width, viewport.height = 0, 0, width, height
    handle.render_document(context, viewport)
    target = dest or PNG / f'{path.stem}-{width}.png'
    surface.write_to_png(str(target))
    return target

app = save('app-icon', document(f'<rect width="512" height="512" rx="112" fill="{PURPLE}"/>{BODY}', color='white', title='FlowRate app icon'))
square = save('app-icon-square', document(f'<rect width="512" height="512" fill="{PURPLE}"/>{BODY}', color='white', title='FlowRate square app icon'))
for size in (16, 24, 32, 48, 64, 128, 256, 512, 1024):
    render(app, size)
render(square, 1024)

for name, color in COLORS.items():
    mark = save(f'mark-{name}', document(BODY, color=color, title='FlowRate headphones mark'))
    for size in (64, 128, 256, 512):
        render(mark, size)

# Dedicated geometry for small tray and favicon sizes, with wider letter strokes.
TRAY = '''<path d="M5 18V14C5 7.9 9.9 3 16 3S27 7.9 27 14V18" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round"/>
<g fill="currentColor"><rect x="3" y="16" width="4.5" height="11" rx="2"/><rect x="24.5" y="16" width="4.5" height="11" rx="2"/>
<path d="M10 25V14H15V16.4H12.4V18.3H14.8V20.7H12.4V25Z"/>
<path fill-rule="evenodd" d="M17 25V14H19.5C22.1 14 23.2 15.4 23.2 17.5C23.2 19 22.6 20 21.5 20.6L23.5 25H20.8L19.4 21.2V25ZM19.4 16.3V18.8H19.6C20.5 18.8 20.9 18.3 20.9 17.5S20.5 16.3 19.6 16.3Z"/></g>'''
for name, color in COLORS.items():
    tray = save(f'tray-{name}', document(TRAY, 32, 32, color, 'FlowRate tray icon'))
    for size in (16, 20, 24, 32, 48):
        render(tray, size)
favicon = save('favicon', document(f'<rect width="32" height="32" rx="7" fill="{PURPLE}"/><g transform="translate(2 2) scale(.875)">{TRAY}</g>', 32, 32, 'white', 'FlowRate favicon'))
for size in (16, 32, 48):
    render(favicon, size)

# Outline the wordmark so the SVG does not depend on an installed font.
ctx = cairo.Context(cairo.ImageSurface(cairo.FORMAT_ARGB32, 1, 1))
ctx.select_font_face('Noto Sans', cairo.FONT_SLANT_NORMAL, cairo.FONT_WEIGHT_BOLD)
ctx.set_font_size(112)
ctx.move_to(290, 167)
ctx.text_path('FlowRate')
commands = []
for kind, points in ctx.copy_path():
    letter = {cairo.PATH_MOVE_TO: 'M', cairo.PATH_LINE_TO: 'L', cairo.PATH_CURVE_TO: 'C', cairo.PATH_CLOSE_PATH: 'Z'}[kind]
    commands.append(letter + ' '.join(f'{n:.3f}' for n in points))
text_path = ' '.join(commands)
for name, text_color in (('light', '#232323'), ('dark', '#FFFFFF')):
    wordmark = save(f'wordmark-on-{name}', document(f'<g transform="scale(.5)">{BODY}</g><path fill="{text_color}" d="{text_path}"/>', 850, 256, PURPLE, 'FlowRate horizontal logo'))
    render(wordmark, 1700, 512)

Image.open(PNG / 'app-icon-256.png').save(ROOT / 'flowrate.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
Image.open(PNG / 'favicon-48.png').save(ROOT / 'favicon.ico', sizes=[(16,16),(32,32),(48,48)])

# Android consumes native resources linked from this kit by the Android project.
android = ROOT / 'Android'
for folder in ('drawable', 'values', 'mipmap-anydpi-v26', 'mipmap-anydpi-v33'):
    (android / folder).mkdir(parents=True, exist_ok=True)
foreground = '''<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="108dp" android:height="108dp" android:viewportWidth="768" android:viewportHeight="768">
    <group android:translateX="128" android:translateY="128">
        <path android:fillColor="#00000000" android:strokeColor="#FFFFFFFF" android:strokeWidth="30" android:strokeLineCap="round"
            android:pathData="M112 281V240C112 160 176 100 256 100S400 160 400 240V281"/>
        <path android:fillColor="#FFFFFFFF" android:pathData="M115 249H119A27 27 0 0 1 146 276V359A27 27 0 0 1 119 386H115A27 27 0 0 1 88 359V276A27 27 0 0 1 115 249Z"/>
        <path android:fillColor="#FFFFFFFF" android:pathData="M393 249H397A27 27 0 0 1 424 276V359A27 27 0 0 1 397 386H393A27 27 0 0 1 366 359V276A27 27 0 0 1 393 249Z"/>
        <group android:translateX="83.1" android:translateY="139.2" android:scaleX="0.66" android:scaleY="0.66">
            <path android:fillColor="#FFFFFFFF" android:pathData="M127 369V143H239V183H171V235H227V275H171V369Z"/>
            <path android:fillColor="#FFFFFFFF" android:fillType="evenOdd" android:pathData="M260 369V143H313C364 143 391 167 391 208C391 238 377 258 353 269L397 369H347L310 281H304V369ZM304 183V242H313C336 242 347 231 347 212C347 193 336 183 313 183Z"/>
        </group>
    </group>
</vector>
'''
(android / 'drawable/ic_launcher_foreground.xml').write_text(foreground, encoding='utf-8')
# Small-size tray geometry, with no background: Android tints notification icons.
notification = '''<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="24dp" android:height="24dp" android:viewportWidth="32" android:viewportHeight="32">
    <path android:fillColor="#00000000" android:strokeColor="#FFFFFFFF" android:strokeWidth="2.6" android:strokeLineCap="round"
        android:pathData="M5 18V14C5 7.9 9.9 3 16 3S27 7.9 27 14V18"/>
    <path android:fillColor="#FFFFFFFF"
        android:pathData="M5 16H5.5A2 2 0 0 1 7.5 18V25A2 2 0 0 1 5.5 27H5A2 2 0 0 1 3 25V18A2 2 0 0 1 5 16Z"/>
    <path android:fillColor="#FFFFFFFF"
        android:pathData="M26.5 16H27A2 2 0 0 1 29 18V25A2 2 0 0 1 27 27H26.5A2 2 0 0 1 24.5 25V18A2 2 0 0 1 26.5 16Z"/>
    <path android:fillColor="#FFFFFFFF" android:pathData="M10 25V14H15V16.4H12.4V18.3H14.8V20.7H12.4V25Z"/>
    <path android:fillColor="#FFFFFFFF" android:fillType="evenOdd"
        android:pathData="M17 25V14H19.5C22.1 14 23.2 15.4 23.2 17.5C23.2 19 22.6 20 21.5 20.6L23.5 25H20.8L19.4 21.2V25ZM19.4 16.3V18.8H19.6C20.5 18.8 20.9 18.3 20.9 17.5S20.5 16.3 19.6 16.3Z"/>
</vector>
'''
(android / 'drawable/ic_stat_flowrate.xml').write_text(notification, encoding='utf-8')
(android / 'values/icon_colors.xml').write_text('<resources><color name="flowrate_icon_background">#FF6C63FF</color></resources>\n', encoding='utf-8')
for version in (26, 33):
    monochrome = '<monochrome android:drawable="@drawable/ic_launcher_foreground"/>' if version == 33 else ''
    adaptive = '<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android"><background android:drawable="@color/flowrate_icon_background"/><foreground android:drawable="@drawable/ic_launcher_foreground"/>' + monochrome + '</adaptive-icon>\n'
    (android / f'mipmap-anydpi-v{version}/ic_launcher.xml').write_text(adaptive, encoding='utf-8')
for density, size in (('mdpi',48),('hdpi',72),('xhdpi',96),('xxhdpi',144),('xxxhdpi',192)):
    folder = android / f'mipmap-{density}'
    folder.mkdir(exist_ok=True)
    render(square, size, dest=folder / 'ic_launcher.png')
for path in android.rglob('*.xml'):
    ET.parse(path)

def embed(name, x, y, width, height):
    element = ET.parse(SVG / f'{name}.svg').getroot()
    content = ''.join(ET.tostring(child, encoding='unicode') for child in element if not child.tag.endswith('title'))
    return f'<svg x="{x}" y="{y}" width="{width}" height="{height}" viewBox="{element.get("viewBox")}">{content}</svg>'

board = '<rect width="1200" height="840" fill="#F5F4F8"/><rect x="610" y="90" width="550" height="700" rx="24" fill="#232323"/>'
board += '<g font-family="sans-serif" fill="#232323"><text x="40" y="55" font-size="30" font-weight="bold">FlowRate — headphones identity</text><text x="40" y="370" font-size="20">App icon</text><text x="330" y="370" font-size="20">Transparent mark</text><text x="40" y="490" font-size="20">Tray: 16 / 24 / 32 / 48 px</text></g>'
board += embed('app-icon', 40, 100, 240, 240) + embed('mark-purple', 330, 100, 240, 240)
board += embed('mark-white', 650, 100, 220, 220) + embed('app-icon-square', 900, 120, 180, 180)
for i, size in enumerate((16,24,32,48)):
    board += embed('tray-purple', 40+i*90, 412, size, size)
    board += embed('tray-white', 650+i*90, 412, size, size)
board += embed('wordmark-on-light', 35, 570, 535, 161) + embed('wordmark-on-dark', 630, 570, 510, 154)
preview = ROOT / 'preview.svg'
preview.write_text(document(board, 1200, 840, title='FlowRate asset variants'), encoding='utf-8')
render(preview, 1200, 840, ROOT / 'preview.png')
for path in PNG.glob('*.png'):
    with Image.open(path) as image:
        image.verify()
assert len(Image.open(ROOT / 'flowrate.ico').ico.sizes()) == 7
assert len(Image.open(ROOT / 'favicon.ico').ico.sizes()) == 3
print(f'Built and validated {len(list(SVG.glob("*.svg")))} SVG, {len(list(PNG.glob("*.png")))} PNG and two ICO files.')
