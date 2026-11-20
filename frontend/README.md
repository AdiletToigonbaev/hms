# HMS — frontend

Next.js 16 (App Router), TypeScript, Tailwind CSS 4, shadcn/ui (Base UI), next-intl, TanStack Query.
Общие инструкции — [README в корне](../README.md).

```bash
npm ci
npm run dev        # http://localhost:3000, API — API_INTERNAL_URL (по умолчанию http://localhost:5080)
npm run lint
npm run typecheck
npm test
npm run build      # standalone-вывод для Docker
```

- Тексты интерфейса — только в `messages/ru.json` (ключи типизированы, см. `src/global.d.ts`).
- Деньги и даты форматируются через `src/lib/format.ts` (сом, часовой пояс отеля Asia/Bishkek). Вычисления сумм на клиенте не выполняются.
- Перед изменением кода, использующего API Next.js, сверяйтесь с `node_modules/next/dist/docs/` (см. `AGENTS.md`): версия 16 отличается от прежних.
- npm 11 блокирует install-скрипты пакетов; решения фиксируются в `package.json` → `allowScripts`.
