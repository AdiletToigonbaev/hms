import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// next-intl: конфигурация запроса — src/i18n/request.ts
const withNextIntl = createNextIntlPlugin();

const nextConfig: NextConfig = {
  // Минимальный сервер для Docker-образа (deploy/), см. node_modules/next/dist/docs/.../output.md
  output: "standalone",
  poweredByHeader: false,
  experimental: {
    // Фото номеров до 10 МБ передаются через Server Actions (по умолчанию лимит 1 МБ).
    serverActions: { bodySizeLimit: "11mb" },
  },
  cacheComponents: true,
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default withNextIntl(nextConfig);
