"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

/** Пользователь сейчас что-то вводит или подтверждает — обновление подождёт. */
export function isBusy(doc: Document): boolean {
  const el = doc.activeElement;
  if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement) return true;
  if (el instanceof HTMLElement && el.isContentEditable) return true;
  return doc.querySelector('[role="alertdialog"], [role="dialog"], [data-popup-open]') !== null;
}

/**
 * Тихое обновление данных страницы (серверные компоненты) раз в N секунд: новые брони с сайта, задачи уборки после выезда.
 * Не обновляет, пока вкладка скрыта, пользователь вводит данные или открыто окно; при возврате на вкладку — обновляет сразу.
 * Состояние формы и прокрутка сохраняются (router.refresh не пересоздаёт страницу).
 */
export function AutoRefresh({ seconds = 45 }: { seconds?: number }) {
  const router = useRouter();
  useEffect(() => {
    const tick = () => {
      if (document.visibilityState === "visible" && !isBusy(document)) router.refresh();
    };
    const timer = window.setInterval(tick, seconds * 1000);
    const onVisible = () => {
      if (document.visibilityState === "visible") tick();
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [router, seconds]);
  return null;
}
