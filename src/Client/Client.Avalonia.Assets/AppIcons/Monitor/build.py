"""Build the FlowRate monitor identity assets using local SVG rendering."""
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
BODY = '''<g fill="none" stroke="currentColor" stroke-width="26" stroke-linecap="round" stroke-linejoin="round">
<rect x="85" y="114" width="342" height="228" rx="38"/>
<path d="M256 342V418M180 418H332"/></g>
<g fill="currentColor" transform="translate(88 64) scale(.64)">
<path d="M127 369V143H239V183H171V235H227V275H171V369Z"/>
<path fill-rule="evenodd" d="M260 369V143H313C364 143 391 167 391 208C391 238 377 258 353 269L397 369H347L310 281H304V369ZM304 183V242H313C336 242 347 231 347 212C347 193 336 183 313 183Z"/></g>'''

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
    mark = save(f'mark-{name}', document(BODY, color=color, title='FlowRate monitor mark'))
    for size in (64, 128, 256, 512):
        render(mark, size)

# Dedicated geometry for small tray and favicon sizes, with wider letter strokes.
TRAY = '''<g fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
<rect x="3" y="5" width="26" height="18" rx="2.7"/>
<path d="M16 23V28M11 28H21"/></g>
<g fill="currentColor"><path d="M8.5 19V9H14V11.2H10.8V13H13.5V15.2H10.8V19Z"/>
<path fill-rule="evenodd" d="M17 19V9H19.5C22 9 23.2 10.3 23.2 12.3C23.2 13.7 22.5 14.8 21.5 15.2L23.5 19H20.8L19.3 15.6V19ZM19.3 11.2V13.5H19.6C20.5 13.5 20.9 13.1 20.9 12.3S20.5 11.2 19.6 11.2Z"/></g>'''

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

def embed(name, x, y, width, height):
    element = ET.parse(SVG / f'{name}.svg').getroot()
    content = ''.join(ET.tostring(child, encoding='unicode') for child in element if not child.tag.endswith('title'))
    return f'<svg x="{x}" y="{y}" width="{width}" height="{height}" viewBox="{element.get("viewBox")}">{content}</svg>'

board = '<rect width="1200" height="840" fill="#F5F4F8"/><rect x="610" y="90" width="550" height="700" rx="24" fill="#232323"/>'
board += '<g font-family="sans-serif" fill="#232323"><text x="40" y="55" font-size="30" font-weight="bold">FlowRate — monitor identity</text><text x="40" y="370" font-size="20">App icon</text><text x="330" y="370" font-size="20">Transparent mark</text><text x="40" y="490" font-size="20">Tray: 16 / 24 / 32 / 48 px</text></g>'
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
