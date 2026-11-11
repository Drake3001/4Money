# React + TypeScript + Vite

4Money application with React, TypeScript, Vite and Tailwind CSS 4. Screens call
the backend through `src/api/`. Files under `src/mocks/` supply UI types,
validation and formatting; API requests are not simulated.

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
