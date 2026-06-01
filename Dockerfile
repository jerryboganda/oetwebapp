# syntax=docker/dockerfile:1.7
FROM node:20-alpine AS deps
WORKDIR /app

COPY package.json pnpm-lock.yaml .npmrc ./
RUN corepack enable
RUN --mount=type=cache,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile

FROM node:20-alpine AS builder
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY . .

RUN corepack enable

ARG NEXT_PUBLIC_API_BASE_URL
ARG NEXT_PUBLIC_SENTRY_DSN
ARG APP_URL

ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL}
ENV NEXT_PUBLIC_SENTRY_DSN=${NEXT_PUBLIC_SENTRY_DSN}
ENV APP_URL=${APP_URL}
ENV NEXT_TELEMETRY_DISABLED=1
ENV NEXT_BUILD_WORKERS=8
ENV NODE_OPTIONS="--max-old-space-size=4096"

# Ensure .env exists for Next.js build (real values come from Docker build args above)
RUN touch .env

RUN --mount=type=cache,target=/app/.next/cache pnpm run build

FROM node:20-alpine AS validate
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .

RUN corepack enable
CMD ["pnpm", "exec", "tsc", "--noEmit"]

FROM node:20-alpine AS runner
WORKDIR /app

RUN addgroup -g 10001 -S nodejs \
    && adduser -S nextjs -u 10001

ENV NODE_ENV=production \
    PORT=3000 \
    HOSTNAME=0.0.0.0 \
    NEXT_TELEMETRY_DISABLED=1

COPY --from=builder /app/.next/standalone ./
COPY --from=builder /app/.next/static ./.next/static
COPY --from=builder /app/public ./public
# next-intl message bundles live outside the .next traced output (they are
# loaded via dynamic import at request time, so Next.js does not include them
# in the standalone trace). Copy them explicitly so server-rendered pages can
# resolve translation keys instead of falling back to the raw key.
COPY --from=builder /app/messages ./messages
COPY --from=builder /app/i18n.ts ./i18n.ts

RUN mkdir -p /app/.next/cache \
    && chown -R nextjs:nodejs /app/.next /app/public

EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
  CMD wget -qO- http://127.0.0.1:3000/api/health >/dev/null || exit 1

USER nextjs

CMD ["node", "server.js"]
