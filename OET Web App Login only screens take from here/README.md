# OET Auth App

This repository now contains a focused Next.js auth-only app for the OET flow.

Routes kept in the project:

- `/login`
- `/register`
- `/register/success`
- `/forgot-password`
- `/forgot-password/verify`
- `/reset-password`
- `/reset-password/success`
- `/verify`
- `/terms`

## Run locally

```bash
npm install
npm run dev -- --port 3001
```

For a production-style local run:

```bash
npm run build
npm run start -- -p 3001
```

## Project shape

- `src/app` holds only the auth routes and shared auth page components.
- `src/lib/auth` holds route helpers, mock auth actions, enrollment data, and validation.
- `src/lib/icons/tabler.ts` keeps the small Turbopack-safe icon wrapper used by the auth screens.
- `public/oet-mark.svg` is the only runtime public asset kept for this auth build.
