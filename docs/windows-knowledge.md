# Windows knowledge catalog

ShowWhere keeps Windows navigation facts in `services/api/src/windows-knowledge/WindowsKnowledgeCatalog.ts`.
The catalog contains semantic intent aliases, Korean/English UI labels, navigation stages, completion evidence,
troubleshooting checkpoints, and canonical `ms-settings:` destinations. It never stores screen coordinates.

Runtime resolution is entirely local:

1. classify the Windows settings or troubleshooting intent;
2. inspect visible and interactive UI Automation candidates;
3. prefer the most specific currently visible destination;
4. for troubleshooting, present diagnostic controls in order;
5. revalidate the candidate in the Windows client before drawing the overlay;
6. use Hugging Face or GPT only when the local catalog cannot safely resolve the current screen.

Canonical sources:

- Microsoft Learn, Launch the Windows Settings app (`ms-settings:` URI reference)
- Microsoft Support, Windows help and learning

The catalog stores only structured navigation facts and source links, not copied support articles. Windows release,
locale, edition, OEM software, and enterprise policy can change visible labels or remove pages. Live UIA validation
therefore remains mandatory, and unresolved or conflicting states escalate instead of reusing coordinates.
