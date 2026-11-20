import { getRequestConfig } from "next-intl/server";
import { defaultLocale, hotelTimeZone } from "./config";

// Пока один язык и без префикса в URL. Маршрутизация по языкам (/ky, /en) — при добавлении локалей.
export default getRequestConfig(async () => {
  const locale = defaultLocale;
  return {
    locale,
    timeZone: hotelTimeZone,
    messages: (await import(`../../messages/${locale}.json`)).default,
  };
});
