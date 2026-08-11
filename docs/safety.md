# Safety model

- ShowWhere highlights controls but never clicks them automatically.
- Only the Node backend reads `FEATHERLESS_API_KEY`.
- The Windows executable contains only the non-secret local backend URL.
- UI Automation passwords are masked before candidate normalization.
- A semantic model may select only a candidate ID from the current observation.
- A visual model may highlight only normalized coordinates tied to the attached screenshot.
- Malformed, stale, unknown, out-of-bounds, and low-confidence decisions are rejected.
- ShowWhere's own panel, assistant, and overlay are excluded from screenshots.
- Provider errors and API keys are not returned to the desktop client or written to diagnostics.
- Full-screen images are sent to Featherless only when local UI Automation cannot resolve the next control.
