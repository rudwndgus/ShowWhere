# Local development

## Prerequisites

- Node.js and npm
- .NET 8 SDK with WindowsDesktop support
- Windows 10 or later
- Chrome only when testing the optional browser adapter

Install dependencies and verify everything:

```powershell
npm install
npm test
npm run typecheck
npm run lint
npm run build
dotnet test apps/windows/ShowWhere.Windows.Tests/ShowWhere.Windows.Tests.csproj -c Release
dotnet build apps/windows/ShowWhere.Windows.sln -c Release
```

## Backend mock mode

Copy `.env.example` to the ignored `.env` and set:

```dotenv
SHOWWHERE_AI_MODE=mock
SHOWWHERE_API_HOST=127.0.0.1
SHOWWHERE_API_PORT=8787
```

Run the backend:

```powershell
npm run dev:api
```

No API key or provider request is used in mock mode.

## Windows desktop client

With the backend running:

```powershell
npm run run:windows
```

The `?` assistant appears above normal applications. Drag it to move it, left-click to open the panel, and right-click for Pause/Exit. The last position is stored under the current user's Local Application Data, outside the repository.

Create a normal framework-dependent executable output:

```powershell
npm run publish:windows
```

Output: `build/windows/ShowWhere.exe`. A production installer is intentionally not part of this phase.

Optional non-secret Windows environment settings:

```dotenv
SHOWWHERE_BACKEND_URL=http://127.0.0.1:8787/api/guide
SHOWWHERE_BACKEND_TIMEOUT_SECONDS=75
```

Never add `FEATHERLESS_API_KEY` to Windows environment settings, `appsettings.json`, or the executable build.

## Live backend mode

Only the backend `.env` receives provider configuration:

```dotenv
SHOWWHERE_AI_MODE=featherless
FEATHERLESS_API_KEY=your-server-only-key
FEATHERLESS_BASE_URL=https://api.featherless.ai/v1
FEATHERLESS_GUIDE_MODEL=your-configured-model
```

Restart the backend after changing its environment. The desktop application continues to call only `SHOWWHERE_BACKEND_URL`.

## Chrome extension

Set the public endpoint and exact extension origin in `.env`, then build:

```dotenv
VITE_SHOWWHERE_GUIDE_API_URL=http://127.0.0.1:8787/api/guide
SHOWWHERE_ALLOWED_ORIGINS=chrome-extension://YOUR_EXTENSION_ID
```

```powershell
npm run build:extension
```

Load `dist/` from `chrome://extensions` using **Load unpacked**, reload the extension, and reload the target page. Backend calls are performed by the extension service worker, not by the page-origin content script.

Chrome-protected pages such as `chrome://extensions` and the Chrome Web Store do not allow ordinary content-script injection.
