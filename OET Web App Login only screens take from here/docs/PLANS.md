# Plans

## Current Focus

- Keep this repository trimmed to the OET auth experience only.
- Preserve the existing login, sign-up, OTP, forgot-password, reset-password, and success-screen design language.
- Keep auth styling self-contained so future cleanup does not reintroduce dashboard/template dependencies.

## Repository Shape

- Next.js 15 auth-only app.
- Public routes:
  `/login`, `/register`, `/register/success`, `/forgot-password`,
  `/forgot-password/verify`, `/reset-password`, `/reset-password/success`,
  `/verify`, and `/terms`.
- Shared auth UI lives under `src/app/auth-pages/_components`.
- Auth helpers and mock data live under `src/lib/auth`.
- The only kept runtime public asset is `public/oet-mark.svg`.

## Validation

- `npm run lint`
- `npm run build`
- `npm run start -- -p 3001`

## Working Rules

- Keep the project auth-only unless a new non-auth route is explicitly requested.
- Add new auth screens by reusing the existing auth shell and module styles first.
- Keep route contracts stable unless there is a clear migration reason.
