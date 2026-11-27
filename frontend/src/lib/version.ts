import "server-only";

/**
 * Версия выпуска (git-метка вида v1.2.0; без метки — хеш коммита). Задаётся deploy/prod.sh через окружение
 * APP_VERSION при выпуске; в разработке — «dev». Показывается персоналу внизу меню кабинета.
 */
export function appVersion(): string {
  return process.env.APP_VERSION?.trim() || "dev";
}
