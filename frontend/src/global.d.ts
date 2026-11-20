import type messages from "../messages/ru.json";
import type { Locale } from "./i18n/config";

// Типизация ключей сообщений: опечатка в ключе — ошибка компиляции.
declare module "next-intl" {
  interface AppConfig {
    Locale: Locale;
    Messages: typeof messages;
  }
}
