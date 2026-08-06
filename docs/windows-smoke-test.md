# Windows manual smoke test

Use backend mock mode first so UI behavior can be verified without provider availability.

## Notepad

1. Set `SHOWWHERE_AI_MODE=mock` and start the backend with `npm run dev:api`.
2. Open Notepad and leave it as the last non-ShowWhere foreground application.
3. Start ShowWhere with `npm run run:windows`.
4. Confirm a small topmost `?` appears and can be dragged without a normal taskbar window.
5. Click `?` and enter a simple goal such as `설정 메뉴를 열고 싶어요`.
6. Confirm the panel reports the Notepad process/title and a bounded candidate count.
7. Confirm the backend returns a candidate ID and a transparent highlight appears around a native UIA control.
8. Click the real Notepad control yourself. ShowWhere must not click it.
9. Confirm the overlay does not block the click and clears after a meaningful focus, structure, foreground-window, or property-derived observation change.
10. Confirm ShowWhere observes again and requests the next step or asks for clarification.

## Windows Settings

1. Open Windows Settings.
2. Ask ShowWhere to find a visible category or search field.
3. Confirm the application name/title changes, offscreen/zero-size elements are absent, and the highlight remains inside the current monitor.
4. Move Settings and ShowWhere to a monitor with a different scale factor and repeat.

## Standard file picker

1. In Notepad choose File > Open to display the standard file picker.
2. Ask ShowWhere to find the file name field or Open button.
3. Confirm duplicate text/icon children resolve to one clickable parent and password/value contents are never shown in logs or requests.
4. Cancel the task and confirm the overlay disappears immediately.

## Pause, recovery, and exit

1. Right-click `?`, choose Pause, and confirm observation stops.
2. Resume and use **찾을 수 없어요** to request a fresh observation.
3. Close the panel and confirm the floating assistant remains.
4. Right-click `?` and choose Exit; confirm all ShowWhere windows and overlays close.
