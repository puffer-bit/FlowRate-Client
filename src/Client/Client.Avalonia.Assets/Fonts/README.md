# Bundled application fonts

The desktop and Android clients use the same embedded Noto text fonts. No system font installation
or network access is required. Noto Sans is the primary face for Latin, Cyrillic and Greek text.
Noto Sans SC and Noto Sans JP cover Chinese and Japanese; the selected interface language determines
which regional Han glyph forms are preferred. Fonts outside this coverage can still use platform fallback.
Code blocks retain their existing monospace font.

The two Noto Color Emoji builds contain the same upstream 2D artwork. Windows uses the
Windows-compatible CBDT/CBLC build, avoiding the COLRv1 rendering problem on Windows 10.
Linux and Android use the COLRv1 build. The Windows bitmap format requires Windows 10 version 1607
or later; support in the client must also be checked on Windows. Rasterization and smoothing can differ
between operating systems even when the font files and artwork are the same.

## Sources and reproduction

Text fonts are from [google/fonts](https://github.com/google/fonts/tree/cc5661d43cd4f9aad5567033a8e15ca28dc35c86/ofl),
commit `cc5661d43cd4f9aad5567033a8e15ca28dc35c86`:

| Upstream file under `ofl/` | Input SHA-256 | Bundled instances |
|---|---|---|
| `notosans/NotoSans[wdth,wght].ttf` | `bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d` | Regular 400, Medium 500, SemiBold 600, Bold 700 |
| `notosans/NotoSans-Italic[wdth,wght].ttf` | `58e6e0ebd1931b29a365aa2d3e2ee9a9e831a3af7cf3ad1462d4e72154f0b291` | Italic 400, BoldItalic 700 |
| `notosansjp/NotoSansJP[wght].ttf` | `c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f` | Regular 400, Bold 700 |
| `notosanssc/NotoSansSC[wght].ttf` | `a3041811a78c361b1de50f953c805e0244951c21c5bd412f7232ef0d899af0da` | Regular 400, Bold 700 |

The text fonts are static instances generated using FontTools 4.61.1. Avalonia's embedded font
collection registers faces by weight/style; explicit instances preserve real text weights instead of
relying on variable-font defaults or synthesized Latin bold/italic. Chinese and Japanese intermediate
weights use the nearest included face. All upstream character coverage is retained.

For example, create Noto Sans Regular with:

```sh
python -m fontTools.varLib.instancer 'NotoSans[wdth,wght].ttf' \
    wght=400 wdth=100 --update-name-table -o NotoSans-Regular.ttf
```

Repeat for the listed weights and italic source. CJK sources only have the `wght` axis, so omit `wdth`.
Keep the resulting faces in their own family folders to prevent platform fallbacks or an unrelated
installed font from taking precedence. The original variable fonts are not shipped.

Emoji fonts are unmodified files from [googlefonts/noto-emoji](https://github.com/googlefonts/noto-emoji/tree/99701e07d3d1fe1bdbcdb14964547f0fd592966f/2D/fonts),
commit `99701e07d3d1fe1bdbcdb14964547f0fd592966f`:

| Upstream file under `2D/fonts/` | Bundled path | SHA-256 |
|---|---|---|
| `Noto-COLRv1.ttf` | `Emoji/NotoColorEmoji.ttf` | `b8e25ea68db82f9e4d0aee921f4420be2be39887bd5c893a2ad98710531f9d0c` |
| `NotoColorEmoji_WindowsCompatible.ttf` | `EmojiWindows/NotoColorEmoji.ttf` | `2c7ede2f5438f9c1da098778bd681535933a345334008bb03fc51119f6b1cd72` |

Both emoji fonts intentionally have the family name `Noto Color Emoji`. They are loaded from separate
asset directories so the chosen build cannot depend on resource enumeration order.

Every font is licensed under SIL Open Font License 1.1. The text families include their upstream
`LICENSE.txt`; both emoji builds use `NotoColorEmoji-LICENSE.txt`. License files are embedded with the
font assets. See also the repository's third-party notices.
