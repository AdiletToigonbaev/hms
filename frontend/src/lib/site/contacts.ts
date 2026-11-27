/**
 * Ссылки «Написать в WhatsApp» и «Открыть в 2ГИС» на сайте. Значения задаёт владелец в настройках;
 * API уже проверяет их (номер — E.164, 2ГИС — только https-адреса 2ГИС), здесь — повторная защита при выводе.
 */

/** wa.me/<цифры номера>?text=… — открывает чат с гостиницей в WhatsApp с готовым приветствием. null — номера нет. */
export function whatsAppLink(phone: string | null | undefined, greeting: string): string | null {
  const digits = (phone ?? "").replace(/\D/g, "");
  if (digits.length < 8) return null;
  return `https://wa.me/${digits}?text=${encodeURIComponent(greeting)}`;
}

/** Ссылка на карточку в 2ГИС, только https и только домены 2ГИС; иначе null (ссылку не показываем). */
export function twoGisLink(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const u = new URL(url);
    const host = u.hostname.toLowerCase();
    const ok =
      u.protocol === "https:" &&
      !u.username &&
      ["2gis.kg", "2gis.ru", "2gis.kz", "2gis.com"].some((h) => host === h || host.endsWith(`.${h}`));
    return ok ? u.toString() : null;
  } catch {
    return null;
  }
}
