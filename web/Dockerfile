FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# The browser talks to the gateway and Keycloak directly, so these are the addresses as seen from the host.
ARG VITE_API_URL=http://localhost:8080
ARG VITE_OIDC_AUTHORITY=http://localhost:8180/realms/orderflow
ENV VITE_API_URL=$VITE_API_URL VITE_OIDC_AUTHORITY=$VITE_OIDC_AUTHORITY
RUN npm run build

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
