/** Поддерживаемые языки интерфейса. KY и EN добавляются позже (ТЗ §2) — сначала файлы в messages/. */
export const locales = ["ru"] as const;

export type Locale = (typeof locales)[number];

export const defaultLocale: Locale = "ru";

/** Часовой пояс отеля — все даты в интерфейсе показываются в нём, независимо от устройства. */
export const hotelTimeZone = "Asia/Bishkek";

export const currency = "KGS";
