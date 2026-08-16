FROM node:26-alpine AS builder
WORKDIR /app
ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci
COPY . .
RUN npm run build

FROM alpine:3.22
ARG RELEASE_VERSION=dev
LABEL org.opencontainers.image.version=$RELEASE_VERSION
RUN apk add --no-cache nginx nginx-mod-http-brotli
COPY --from=builder /app/dist /usr/share/nginx/html
RUN rm /etc/nginx/http.d/default.conf
COPY nginx/nginx.conf /etc/nginx/http.d/default.conf
COPY nginx/security-headers.inc /etc/nginx/security-headers.inc
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
