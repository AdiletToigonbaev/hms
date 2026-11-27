import type { Viewport } from "next";

/**
 * Цвет панели браузера на телефоне (meta theme-color) — в тон шапке, чтобы панель и сайт сливались.
 * Цвета — токены globals.css (светлая и тёмная тема по настройке системы); при смене палитры — поменять и здесь.
 */
function themeColor(light: string, dark: string): Viewport {
  return {
    themeColor: [
      { media: "(prefers-color-scheme: light)", color: light },
      { media: "(prefers-color-scheme: dark)", color: dark },
    ],
  };
}

/** Публичный сайт: шапка на фоне страницы (--background). */
export const siteViewport = themeColor("#ffffff", "#1d1a17");

/** Кабинеты (AreaShell): песочная шапка (--sidebar). */
export const areaViewport = themeColor("#f6f1ea", "#221e1a");
