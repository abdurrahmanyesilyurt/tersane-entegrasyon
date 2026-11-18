# syntax=docker/dockerfile:1
# React panel: build with Node, serve the static files with nginx, proxy /api to the MVC app.
# glibc image on purpose: Rollup's native parser hits the small musl thread stack on Alpine ("nested too deeply").
FROM node:22-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
