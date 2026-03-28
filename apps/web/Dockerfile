FROM node:22-alpine AS build
WORKDIR /app

RUN corepack enable

# Copy workspace manifests so pnpm can resolve the workspace graph
COPY pnpm-lock.yaml package.json pnpm-workspace.yaml ./
COPY apps/web/package.json ./apps/web/

RUN pnpm install --frozen-lockfile --filter web

# Copy web source and build
COPY apps/web/ ./apps/web/
WORKDIR /app/apps/web
RUN pnpm build

FROM nginx:alpine
COPY --from=build /app/apps/web/dist /usr/share/nginx/html
COPY apps/web/nginx.conf /etc/nginx/templates/default.conf.template

# nginx:alpine image runs envsubst on /etc/nginx/templates/*.template at startup
# This resolves ${PORT} and ${API_URL} from environment variables
ENV PORT=80
ENV API_URL=http://api:8080

EXPOSE 80
HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
  CMD wget -qO- http://localhost:${PORT}/ || exit 1