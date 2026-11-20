"use client";

import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * Меню разделов настроек: подсвечивает раздел, который сейчас на экране,
 * а по клику плавно прокручивает к разделу и на мгновение выделяет его рамкой.
 */
export function SettingsNav({ label, sections }: { label: string; sections: { id: string; title: string }[] }) {
  const [active, setActive] = useState(sections[0]?.id);
  const listRef = useRef<HTMLUListElement>(null);
  // Пока пользователь работает с полем раздела, прокрутка не перебивает подсветку.
  const editing = useRef<string | null>(null);

  useEffect(() => {
    // Активный — верхний из разделов, попавших в верхнюю половину экрана.
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((e) => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
        if (visible[0] && !editing.current) setActive(visible[0].target.id);
      },
      { rootMargin: "0px 0px -55% 0px", threshold: 0 },
    );
    for (const { id } of sections) {
      const el = document.getElementById(id);
      if (el) observer.observe(el);
    }
    // Зеркально: фокус или нажатие внутри раздела подсвечивает его пункт в меню.
    const sectionOf = (target: EventTarget | null) =>
      target instanceof Element ? sections.find(({ id }) => target.closest(`#${CSS.escape(id)}`))?.id : undefined;
    const onFocusIn = (e: FocusEvent) => {
      const id = sectionOf(e.target);
      editing.current = id ?? null;
      if (id) setActive(id);
    };
    const onPointerDown = (e: PointerEvent) => {
      const id = sectionOf(e.target);
      if (id) setActive(id);
    };
    const onFocusOut = () => {
      editing.current = null;
    };
    document.addEventListener("focusin", onFocusIn);
    document.addEventListener("focusout", onFocusOut);
    document.addEventListener("pointerdown", onPointerDown);
    return () => {
      observer.disconnect();
      document.removeEventListener("focusin", onFocusIn);
      document.removeEventListener("focusout", onFocusOut);
      document.removeEventListener("pointerdown", onPointerDown);
    };
  }, [sections]);

  // На телефоне меню — полоса с прокруткой: активный пункт должен быть виден.
  useEffect(() => {
    const list = listRef.current;
    const item = list?.querySelector<HTMLElement>("[aria-current]");
    if (!list || !item || list.scrollWidth <= list.clientWidth) return;
    const left = item.offsetLeft - (list.clientWidth - item.offsetWidth) / 2;
    list.scrollTo({ left: Math.max(0, left), behavior: "smooth" });
  }, [active]);

  function go(id: string, event: React.MouseEvent) {
    const el = document.getElementById(id);
    if (!el) return;
    event.preventDefault();
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    el.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "start" });
    history.replaceState(null, "", `#${id}`);
    setActive(id);
    // Короткая подсветка раздела, чтобы сразу было видно, куда перешли.
    el.removeAttribute("data-flash");
    void el.offsetWidth;
    el.setAttribute("data-flash", "");
    window.setTimeout(() => el.removeAttribute("data-flash"), 1600);
    // Фокус — на заголовок раздела (для клавиатуры и экранных читалок), без повторной прокрутки.
    // (после обработки клика — иначе фокус остаётся на ссылке).
    window.setTimeout(() => el.querySelector<HTMLElement>("h2")?.focus({ preventScroll: true }), 0);
  }

  return (
    <nav
      aria-label={label}
      // Телефон: полоса прилипает под шапкой кабинета (h-14 + граница); ПК: меню слева прилипает к верху.
      className="sticky top-[57px] z-10 -mx-4 min-w-0 border-b bg-background/95 px-4 py-2 backdrop-blur sm:-mx-6 sm:px-6 lg:top-6 lg:mx-0 lg:self-start lg:border-0 lg:bg-transparent lg:p-0 lg:backdrop-blur-none"
    >
      <ul ref={listRef} className="flex gap-1.5 overflow-x-auto pb-1 lg:grid lg:gap-1 lg:overflow-visible">
        {sections.map((section) => {
          const isActive = section.id === active;
          return (
            <li key={section.id} className="shrink-0">
              <a
                href={`#${section.id}`}
                onClick={(e) => go(section.id, e)}
                aria-current={isActive ? "location" : undefined}
                className={cn(
                  "relative block rounded-lg border px-3 py-1.5 text-sm whitespace-nowrap transition",
                  "lg:border-transparent lg:pl-4",
                  isActive
                    ? "border-primary/40 bg-primary/10 font-medium text-primary lg:bg-sidebar-accent lg:before:absolute lg:before:inset-y-1.5 lg:before:left-1 lg:before:w-1 lg:before:rounded-full lg:before:bg-primary"
                    : "bg-card hover:border-primary/40 hover:text-primary lg:bg-transparent lg:hover:bg-sidebar-accent/60",
                )}
              >
                {section.title}
              </a>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
