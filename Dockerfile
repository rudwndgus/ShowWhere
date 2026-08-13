FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY tsconfig.json vite.config.ts ./
COPY src ./src
COPY services/api ./services/api
RUN npm run build:api

FROM node:22-alpine AS runtime
ENV NODE_ENV=production
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --omit=dev --ignore-scripts && npm cache clean --force
COPY --from=build /app/services/api/dist ./services/api/dist
COPY knowledge/web/catalogs ./knowledge/web/catalogs
COPY knowledge/web/patterns ./knowledge/web/patterns

USER node
EXPOSE 8787
CMD ["node", "services/api/dist/server.js"]
