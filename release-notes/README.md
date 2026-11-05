# Release notes

This folder keeps source text for publishing releases on GitHub. The application's
About page loads published notes directly from the repository's GitHub Releases API;
it does not embed these files or require a new build when published notes change.

The history initially follows the update channel selected in Behavior settings and
tracks changes to that setting while the settings window is open. Browsing another
history tab does not change the update preference. Descriptions support text selection
and copying.

The Releases and Dev tabs each show up to six releases, ordered by publication date.
GitHub's `prerelease` flag selects the Dev tab. Drafts are excluded. Requests follow
pagination until six matching releases are found or the end of the list is reached.
Successful results are cached in memory for five minutes. Loading starts when the
About page opens, with a refresh action for retries and an empty state per channel.

Descriptions preserve the language used on GitHub. The interface renders headings,
compact paragraphs and hanging bullet or numbered lists. Bold labels and inline code
retain their emphasis. Recognized English and Russian section headings get a shared
icon; unknown headings use a generic notes icon. Markdown links and bare HTTP(S)
URLs are clickable, with compact labels and full destinations in tooltips. All surfaces,
text and links follow the active application theme. Every card has a button to
open the original release on GitHub for its full formatting and downloads.
