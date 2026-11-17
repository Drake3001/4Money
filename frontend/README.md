# React + TypeScript + Vite

4Money application with React, TypeScript, Vite and Tailwind CSS 4. Screens call
the backend through `src/api/`. Files under `src/mocks/` supply UI types,
validation and formatting; API requests are not simulated.

## Login and Google

Login and registration share a responsive split layout: branding on the left,
the form on the right; panels stack on mobile. Registration offers Google sign-up
and a compact form with paired fields on larger screens. Forgotten passwords
open a modal over login; the existing forgot route opens that modal as well.
Password reset and email-confirmation routes remain available.
Validation, API errors and Google status messages have reserved space, so showing
them does not move fields or change the form scale. On desktop, the form fits the
available viewport height using that reserved space from its first render.
Login and registration reserve the same form-stage height at each width breakpoint,
so switching between them preserves the page height and scrolling threshold.
The side-by-side layout starts at 768px and fills the window width. Both forms
use the same scale, fitted to the viewport height on resize, down to 80%.
The shared minimum page height is 35.5rem (568px at the default root font size);
shorter windows scroll instead of clipping content.
Below the desktop breakpoint, both pages keep the same reserved form space and scroll.
Google buttons and the separator
share a maximum width of 400px, matching the official GIS button width limit.

Google sign-in obtains `clientId` and `nonce` from `POST /api/users/google/challenge`,
then renders the official Google Identity Services button. The credential goes
to the existing Google login endpoint. `google_registration_required` opens the
profile completion form and uses the Google registration endpoint. Only successful
authentication responses enter the normal session store; the Google ID token stays
in component memory. Accounts requiring email confirmation display instructions.

Enable Google in the API and configure its Web Client ID and authorized JavaScript
origins as described in [development](../agent-context/development.md#rejestracja-i-logowanie-google).
The frontend uses the ID returned by the API, without another frontend environment
variable. Keep the same-origin Vite proxy for the challenge cookie. When the API's
Google integration is unavailable, regular sign-in remains usable and retrying the
Google option displays an availability message.

The SDK is loaded from `https://accounts.google.com/gsi/client?hl=pl`; blocklists
and browser popup restrictions can affect real sign-in. SDK reference:
[Google Identity Services](https://developers.google.com/identity/gsi/web/reference/js-reference).

## Account email links

HTML emails link to `/?action=...#token=...`. The frontend confirms registration,
email changes and deactivation automatically and displays the result. Registration
confirmation redirects to login. Password reset opens the new-password form.
Successful email change, password reset and deactivation clear the local session.
Token links are single-use; success removes the token from the address bar.
Forms remind users to check Spam/Offers after requesting an email.

Configure `UserEmailLinks:FrontendBaseUrl` in the API or `USER_EMAIL_FRONTEND_URL`
in local Compose to the frontend address reachable from the recipient's browser
(localhost by default; use the Tailscale address when testing remotely).

## Profile settings

The account page offers password setup/change using the existing email reset
flow. The address must be confirmed; successfully requesting a link does not
change the password or end the current session. Completing the linked form does.
There is no endpoint for changing a password using the current password.

Profile and group images support PNG/JPG/WebP and HEIC/HEIF source files up to 25 MiB,
server-provided presets and explicit removal. The browser fits the image inside
512 × 512 pixels without enlarging it and prepares a WebP upload (PNG fallback),
up to the existing 5 MiB API limit. HEIC decoding uses a lazy-loaded local
`heic-to/csp` dependency; photos are not sent to a third-party converter.
Both pickers use `src/shared/iconImage.ts`. Group creation/update waits for image
preparation to finish before enabling save.
The backend independently validates, orients and resizes uploads, removes metadata
and stores WebP. Presets appear once images are added to storage; an empty
list displays a placeholder message. Profile and sidebar avatars fall back to
initials when there is no image or it fails to load. Signed image URLs refresh
before expiry, and editing other profile fields preserves the selected image.

## Docker development

See [the local Compose instructions](../containers/local/README.md) for environment
setup and configuration. From `containers/local/`:

```bash
docker compose up --build -d frontend
```

Open http://localhost:5173 and edit `src/App.tsx` to test HMR. Node.js is provided
by the container. Dependencies are installed from `package-lock.json` at every
start; restart the frontend after changing dependencies or switching branches.

```bash
docker compose exec frontend npm test
docker compose exec frontend npm run build
```

## Running without Docker

Use Node.js 22.12+ from the Node 22 release line:

```bash
npm ci
npm run dev
```

The Vite configuration enables file polling only when `FRONTEND_USE_POLLING=true`.
Compose supplies this variable by default; no frontend `.env` is required.

## Backend connection

Start the backend separately with its HTTP profile on `http://localhost:5283`.
Its database, migrations and configuration must be ready; see
[development instructions](../agent-context/development.md).

The browser calls `/api` on the frontend origin. Vite dev and preview proxy these
requests to `API_PROXY_TARGET`, defaulting to `http://localhost:5283`.
Compose supplies `http://backend:8080`. This setup does not need backend CORS.
For a different local address, copy `.env.example` to `.env.local`, adjust
`API_PROXY_TARGET`, then restart Vite.

Normally leave `VITE_API_BASE_URL` unset. It overrides the browser API origin
and requires backend CORS for cross-origin requests. Never put secrets in
`VITE_*` variables: they are embedded in the bundle. Production hosting of
`dist/` must route `/api` to the backend, or use a build with an explicit API
origin and appropriate backend CORS. Vite preview is a local check tool.

## Checks and limitations

```bash
npm run build
npm test
```

Build includes TypeScript checks. Tests verify login rendering with all providers
and HTTP proxy transport against a synthetic backend. They do not verify a live
database, SMTP, Tink or OCR integration.

Receipt selection keeps the file and preview locally until the expense is saved.
Explicit OCR sends multipart `file` to `POST /api/expenses/receipts/analyze`.
Creating or updating an expense uploads multipart `expense` (JSON), `file` and
the optional signed `ocrProof` together. Analysis alone does not save a receipt.
Saved receipts are read through `GET /api/expenses/{expenseId}/receipt`; detaching
one is part of the expense update. Dates use `YYYY-MM-DD` without a required time.
Tests cover these request formats against synthetic responses, including saving
without OCR and mapping structured warnings. Live OCR/storage integration still
requires a running backend, worker and storage; see
[implementation status](../agent-context/implementation-status.md).
