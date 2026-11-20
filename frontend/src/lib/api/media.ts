/** Адрес файла для браузера: API недоступен напрямую, файлы отдаёт маршрут Next.js /media/{id}. */
export function mediaUrl(fileId: string): string {
  return `/media/${fileId}`;
}
