# syntax=docker/dockerfile:1
# oet-agent-egress — allowlist HTTP CONNECT / plain-HTTP forward proxy for the
# Owner Agent Console (agent-console/CONTRACT.md §6). The ONLY container of the
# console stack with an internet route. Built on GitHub Actions
# (.github/workflows/agent-console.yml) and pulled by the VPS; never built there.
# Zero runtime npm dependencies: the runtime stage carries only dist/ + package.json.
ARG NODE_IMAGE=node:22.23.3-alpine3.24@sha256:0a7108bf6c7bf5de370ffb1a3ed6be93d405b43ff159f681a8d18c0e2bc2e402

FROM ${NODE_IMAGE} AS build
WORKDIR /build
COPY package.json package-lock.json* .npmrc ./
RUN if [ -f package-lock.json ]; then npm ci --no-audit --no-fund --ignore-scripts; else npm install --no-audit --no-fund --ignore-scripts; fi
COPY tsconfig.json tsconfig.build.json ./
COPY src ./src
RUN npm run build

FROM ${NODE_IMAGE}
ENV NODE_ENV=production \
    NODE_OPTIONS=--max-old-space-size=96
# Drop the package managers from the runtime image (nothing is installed at runtime).
RUN rm -rf /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/corepack \
      /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack \
      /opt/yarn-* /usr/local/bin/yarn /usr/local/bin/yarnpkg
WORKDIR /app
COPY package.json ./
COPY --from=build /build/dist ./dist
USER 10010:10010
EXPOSE 3128
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -q -T 4 -O /dev/null http://127.0.0.1:3128/healthz || exit 1
ENTRYPOINT ["node", "dist/main.js"]
