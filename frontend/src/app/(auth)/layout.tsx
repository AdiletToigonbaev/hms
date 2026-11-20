import Link from "next/link";
import { Suspense } from "react";
import { Brand } from "@/components/brand";
import { getHotelInfo } from "@/lib/hotel";

/** Название гостиницы из настроек (сетевой запрос — внутри Suspense). */
async function HotelBrand() {
  const hotel = await getHotelInfo();
  return <Brand name={hotel.name} />;
}

async function HotelWelcome() {
  const hotel = await getHotelInfo();
  return (
    <div className="grid gap-4">
      <p className="font-heading text-4xl leading-tight xl:text-5xl">{hotel.name}</p>
      <p className="max-w-sm text-lg text-white/85">
        {hotel.description ?? "Уютные номера, внимательный сервис и тёплый приём — добро пожаловать."}
      </p>
      <p className="text-sm text-white/70">
        Заезд с {hotel.checkInTime} · выезд до {hotel.checkOutTime}
      </p>
    </div>
  );
}

export default function AuthLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="grid min-h-dvh lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      {/* Тёплая панель приветствия — только на больших экранах */}
      <aside
        className="relative hidden overflow-hidden bg-[linear-gradient(145deg,#8f3f1f_0%,#b5562f_45%,#d08a64_100%)] p-12 text-white lg:flex lg:flex-col lg:justify-between"
        aria-hidden
      >
        <div className="absolute -top-24 -right-24 size-96 rounded-full bg-white/10 blur-2xl" />
        <div className="absolute -bottom-32 -left-16 size-96 rounded-full bg-[#6b7a4b]/30 blur-3xl" />
        <span className="relative grid size-14 place-items-center rounded-2xl bg-white/15 font-heading text-2xl backdrop-blur">
          ✦
        </span>
        <div className="relative">
          <Suspense fallback={<p className="font-heading text-4xl">Добро пожаловать</p>}>
            <HotelWelcome />
          </Suspense>
        </div>
      </aside>

      <div className="flex flex-col items-center justify-center gap-8 bg-background px-4 py-10">
        <Link href="/" className="rounded-xl lg:hidden">
          <Suspense fallback={<Brand name="Гостиница" />}>
            <HotelBrand />
          </Suspense>
        </Link>
        <main className="w-full max-w-md">{children}</main>
      </div>
    </div>
  );
}
