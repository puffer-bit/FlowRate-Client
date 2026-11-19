# Emoji catalog

`catalog.tsv` contains Unicode Emoji 16.0 fully-qualified sequences, excluding skin-tone
variants and standalone components. Columns: sequence, category index, English name,
Russian name, English keywords, Russian keywords. Variation selectors and joiners are preserved.

Sources:
- https://www.unicode.org/Public/emoji/16.0/emoji-test.txt
- https://github.com/unicode-org/cldr/blob/release-46/common/annotations/en.xml
- https://github.com/unicode-org/cldr/blob/release-46/common/annotations/ru.xml

Names fall back to the Unicode name when an annotation is missing. Category indices follow
the nine non-component groups in `emoji-test.txt`. These data are covered by the Unicode
license in `LICENSE.txt`.
