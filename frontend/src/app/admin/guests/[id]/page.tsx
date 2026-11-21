import { ArrowLeft, Plus } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { BookingStatusBadge } from "@/components/booking-status-badge";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api } from "@/lib/api/server";
import type { ConsentHistoryItem, Guest, GuestBooking } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { formatHotelDateTime, formatHotelLocal, formatHotelTime, formatMoney, formatStayDate } from "@/lib/format";
import { AnonymizeForm, ConsentForm, GuestForm } from "../guest-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Guests"))("title") };
}

const sourceKey = { Portal: "srcPortal", FrontDesk: "srcFrontDesk", Phone: "srcPhone", WhatsApp: "srcWhatsApp", Written: "srcWritten" } as const;
const originKey = { Admin: "sourceAdmin", Booking: "sourceBooking", Portal: "sourcePortal" } as const;

export default async function GuestPage({ params }: PageProps<"/admin/guests/[id]">) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();

  const [t, user, guest, bookings, consents] = await Promise.all([
    getTranslations("Guests"),
    getCurrentUser(),
    api<Guest>(`/guests/${id}`),
    api<GuestBooking[]>(`/guests/${id}/bookings`),
    api<ConsentHistoryItem[]>(`/guests/${id}/consents`),
  ]);
  if (!guest.ok) notFound();
  const g = guest.data;
  const can = (p: string) => user?.permissions.includes(p) ?? false;
  const editable = can("guest.manage") && !g.isAnonymized;

  const stats = [
    { label: t("statBookings"), value: String(g.stats.bookingsCount) },
    { label: t("statVisits"), value: String(g.stats.visitsCount), hint: t("statVisitsHint") },
    { label: t("statAmount"), value: formatMoney(g.stats.bookedAmount), hint: t("statAmountHint") },
    { label: t("statLast"), value: g.stats.lastStayAt ? formatHotelDateTime(g.stats.lastStayAt) : t("never") },
  ];

  return (
    <div className="grid gap-6">
      <div className="grid gap-3">
        <Link href="/admin/guests" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("back")}
        </Link>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="grid gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-3xl">{g.fullName}</h1>
              {g.hasAccount && <Badge variant="outline">{t("account")}</Badge>}
              {g.isAnonymized && <Badge variant="destructive">{t("anonymized")}</Badge>}
            </div>
            <p className="text-sm text-muted-foreground">
              {t(originKey[g.source])} · {t("since", { date: formatHotelDateTime(g.createdAt) })}
            </p>
          </div>
          {can("booking.create") && !g.isAnonymized && (
            <Link href={`/admin/bookings/new?guestId=${g.id}`} className={buttonVariants()}>
              <Plus aria-hidden />
              {t("newBooking")}
            </Link>
          )}
        </div>
      </div>

      <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {stats.map((s) => (
          <div key={s.label} className="grid gap-1 rounded-2xl border bg-card p-4 shadow-xs">
            <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{s.label}</dt>
            <dd className="tabular font-heading text-2xl">{s.value}</dd>
            {s.hint && <dd className="text-xs text-muted-foreground">{s.hint}</dd>}
          </div>
        ))}
      </dl>

      {g.isAnonymized && <p className="rounded-xl bg-secondary px-4 py-3 text-sm text-muted-foreground">{t("anonymizedHint")}</p>}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="grid content-start gap-6">
          {editable && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("details")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <GuestForm guest={g} />
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("bookings")}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              {!bookings.ok || bookings.data.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t("bookingsEmpty")}</p>
              ) : (
                <div className="overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>{t("colBooking")}</TableHead>
                        <TableHead>{t("colTime")}</TableHead>
                        <TableHead>{t("colStatus")}</TableHead>
                        <TableHead className="text-right">{t("colTotal")}</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {bookings.data.map((b) => (
                        <TableRow key={b.bookingId}>
                          <TableCell>
                            <Link href={`/admin/bookings/${b.bookingId}`} className="tabular font-medium underline-offset-4 hover:underline">
                              {b.number}
                            </Link>
                            <span className="block text-xs text-muted-foreground">{t("room", { number: b.roomNumber })}</span>
                          </TableCell>
                          <TableCell className="tabular text-sm whitespace-nowrap">
                            {b.kind === "Hourly"
                              ? `${formatHotelLocal(b.startsAt)}–${formatHotelTime(b.endsAt)}`
                              : `${formatStayDate(b.startsAt.slice(0, 10))} — ${formatStayDate(b.endsAt.slice(0, 10))}`}
                          </TableCell>
                          <TableCell>
                            <BookingStatusBadge status={b.status} />
                          </TableCell>
                          <TableCell className="tabular text-right whitespace-nowrap">{formatMoney(b.totalAmount)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="grid content-start gap-6">
          {!g.isAnonymized && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("consents")}</h2>
                </CardTitle>
                <CardDescription>{t("consentsHint")}</CardDescription>
              </CardHeader>
              <CardContent className="grid gap-3">
                {(["PersonalData", "WhatsApp"] as const).map((type) =>
                  can("guest.manage") ? (
                    <ConsentForm key={type} guestId={g.id} type={type} current={g.consents.find((c) => c.type === type)} />
                  ) : null,
                )}
                <div className="grid gap-2">
                  <h3 className="text-sm font-medium">{t("consentHistory")}</h3>
                  {!consents.ok || consents.data.length === 0 ? (
                    <p className="text-sm text-muted-foreground">{t("historyEmpty")}</p>
                  ) : (
                    <ol className="grid gap-2">
                      {consents.data.map((c) => (
                        <li key={c.id} className="grid gap-0.5 border-l-2 border-primary/30 pl-3 text-sm">
                          <span>
                            {c.type === "PersonalData" ? t("consentPersonalData") : t("consentWhatsApp")}:{" "}
                            <span className="font-medium">{c.granted ? t("granted") : t("revoked")}</span>
                          </span>
                          <span className="text-xs text-muted-foreground">
                            {t(sourceKey[c.source])} · {formatHotelDateTime(c.recordedAt)}
                            {c.recordedByName ? ` · ${c.recordedByName}` : ""}
                          </span>
                        </li>
                      ))}
                    </ol>
                  )}
                </div>
              </CardContent>
            </Card>
          )}

          {can("guest.anonymize") && !g.isAnonymized && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("danger")}</h2>
                </CardTitle>
              </CardHeader>
              <CardContent>
                <AnonymizeForm guest={g} />
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
