# syntax=docker/dockerfile:1.19
FROM node:22-alpine AS deps
WORKDIR /app

COPY package.json pnpm-lock.yaml .npmrc ./
RUN corepack enable
RUN --mount=type=cache,target=/root/.local/share/pnpm/store pnpm install --frozen-lockfile

FROM node:22-alpine AS builder
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY . .

RUN corepack enable

ARG NEXT_PUBLIC_API_BASE_URL
ARG NEXT_PUBLIC_SENTRY_DSN
ARG NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED
ARG APP_URL

ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL}
ENV NEXT_PUBLIC_SENTRY_DSN=${NEXT_PUBLIC_SENTRY_DSN}
# Mac download kill-switch (17 Sep 2026 handover) — build-time inlined so the
# client badges and the server download/feed routes all see the same value.
ENV NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED=${NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED}
ENV APP_URL=${APP_URL}
ENV NEXT_TELEMETRY_DISABLED=1
# 6144 MB: the old 2048 cap made V8 GC-thrash on this app size. Runners have
# 16 GB, so a 6 GB heap removes the thrash without risking the OOM cascade the
# cap was originally guarding against (which was a small-box problem).
ENV NODE_OPTIONS="--max-old-space-size=6144"

# Ensure .env exists for Next.js build (real values come from Docker build args above)
RUN touch .env

ARG GITHUB_SHA=dev
ENV GITHUB_SHA=${GITHUB_SHA}

RUN --mount=type=cache,target=/app/.next/cache pnpm run build

FROM node:22-alpine AS runner
WORKDIR /app

RUN addgroup -g 10001 -S nodejs \
    && adduser -S nextjs -u 10001

ENV NODE_ENV=production \
    PORT=3000 \
    HOSTNAME=0.0.0.0 \
    NEXT_TELEMETRY_DISABLED=1

# Keep dependencies reusable and root-owned without copying application bytes twice.
COPY --from=builder --exclude=server.js --exclude=.next /app/.next/standalone ./
COPY --from=builder /app/.next/standalone/server.js ./server.js
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone/.next ./.next
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public
# next-intl message bundles live outside the .next traced output (they are
# loaded via dynamic import at request time, so Next.js does not include them
# in the standalone trace). Copy them explicitly so server-rendered pages can
# resolve translation keys instead of falling back to the raw key.
COPY --from=builder /app/messages ./messages
COPY --from=builder /app/i18n.ts ./i18n.ts

RUN --mount=type=bind,from=builder,source=/app/.next/standalone,target=/standalone \
    mkdir -p /app/.next/cache \
    && chown nextjs:nodejs /app/.next /app/.next/cache /app/public \
    && cd /standalone \
    && find . -type f -exec sha256sum {} + > /tmp/standalone.sha256 \
    && cd /app \
    && sha256sum --check --quiet /tmp/standalone.sha256 \
    && rm /tmp/standalone.sha256 \
    && test "$(stat -c '%u:%g' .next)" = "10001:10001" \
    && test "$(stat -c '%u:%g' public)" = "10001:10001" \
    && test "$(stat -c '%u:%g' node_modules)" = "0:0" \
    && test "$(stat -c '%u:%g' server.js)" = "0:0"

EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
  CMD wget -qO- http://127.0.0.1:3000/api/health >/dev/null || exit 1

USER nextjs

CMD ["node", "server.js"]
