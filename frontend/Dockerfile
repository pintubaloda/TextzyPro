FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
COPY scripts ./scripts
RUN npm install --legacy-peer-deps --no-audit --no-fund

COPY . .
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080
CMD ["sh", "-c", "sed -i \"s/listen 8080;/listen ${PORT:-8080};/\" /etc/nginx/conf.d/default.conf && printf \"window.__APP_CONFIG__={API_BASE:'%s',FACEBOOK_APP_ID:'%s',WABA_EMBEDDED_CONFIG_ID:'%s'};\" \"${REACT_APP_API_BASE:-${VITE_API_BASE:-}}\" \"${REACT_APP_FACEBOOK_APP_ID:-${VITE_FACEBOOK_APP_ID:-}}\" \"${REACT_APP_WABA_EMBEDDED_CONFIG_ID:-${VITE_WABA_EMBEDDED_CONFIG_ID:-}}\" > /usr/share/nginx/html/env.js && nginx -g 'daemon off;'"]
