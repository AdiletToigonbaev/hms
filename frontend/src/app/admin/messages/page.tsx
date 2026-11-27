import { MessagesSquare } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { AutoRefresh } from "@/components/auto-refresh";
import { LiveSearchInput } from "@/components/forms/live-search-input";
import { PageHeader } from "@/components/layout/page-header";
import { ChannelLabel, MessageStatusBadge } from "@/components/message-badges";
import { EmptyState, ErrorState } from "@/components/states/states";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api } from "@/lib/api/server";
import type { Conversation, Message, MessagingSettings, Paged } from "@/lib/api/types";
import { getCurrentUser } from "@/lib/auth/session";
import { formatHotelDateTime } from "@/lib/format";
import { formatPhone, messageKindLabel, messageStatusLabel, windowLeft } from "@/lib/messaging/labels";
import { cn } from "@/lib/utils";
import { RetryMessageButton } from "./message-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Messages"))("title") };
}

const views = ["conversations", "history"] as const;
const channels = ["", "Email", "WhatsApp"] as const;
const statuses = ["", "Failed", "Queued", "Sent", "Delivered", "Read", "Skipped"] as const;
const PAGE_SIZE = 30;

/** Сообщения гостям: диалоги WhatsApp (входящие, автоответы, передача администратору) и история всех отправок. */
export default async function MessagesPage({ searchParams }: PageProps<"/admin/messages">) {
  const params = await searchParams;
  const str = (v: string | string[] | undefined) => (typeof v === "string" ? v : "");
  const view = (views as readonly string[]).includes(str(params.view)) ? (str(params.view) as (typeof views)[number]) : "conversations";
  const channel = (channels as readonly string[]).includes(str(params.channel)) ? str(params.channel) : "";
  const status = (statuses as readonly string[]).includes(str(params.status)) ? str(params.status) : "";
  const search = str(params.search).slice(0, 40);
  const operator = str(params.operator) === "1";
  const page = Math.max(1, Number(str(params.page)) || 1);

  const [t, user] = await Promise.all([getTranslations("Messages"), getCurrentUser()]);
  const isOwner = user?.roles.includes("Owner") ?? false;
  const settings = isOwner ? await api<MessagingSettings>("/messaging/settings") : null;
  const link = (next: Record<string, string>) => {
    const q = new URLSearchParams({ view, ...(channel ? { channel } : {}), ...(status ? { status } : {}), ...(search ? { search } : {}), ...(operator ? { operator: "1" } : {}), ...next });
    for (const [k, v] of [...q.entries()]) if (!v) q.delete(k);
    return `/admin/messages?${q}`;
  };
  const chip = (active: boolean) =>
    cn(
      "rounded-full border px-3 py-1 text-sm font-medium transition",
      active ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card hover:border-primary/40 hover:text-primary",
    );

  return (
    <div className="grid gap-6">
      <AutoRefresh seconds={30} />
      <PageHeader title={t("title")} description={t("description")} />

      {settings?.ok && !settings.data.whatsApp.available && (
        <p className="rounded-xl bg-secondary px-4 py-3 text-sm text-muted-foreground">
          {t("whatsAppOff")}{" "}
          <Link href="/owner/integrations" className="font-medium text-foreground underline-offset-4 hover:underline">
            {t("toIntegrations")}
          </Link>
        </p>
      )}

      <nav aria-label={t("title")} className="flex flex-wrap gap-1.5">
        {views.map((v) => (
          <Link key={v} href={`/admin/messages?view=${v}`} aria-current={v === view ? "page" : undefined} className={chip(v === view)}>
            {t(v === "conversations" ? "viewConversations" : "viewHistory")}
          </Link>
        ))}
      </nav>

      {view === "conversations" ? (
        <ConversationsView search={search} operator={operator} page={page} link={link} chip={chip} />
      ) : (
        <HistoryView channel={channel} status={status} page={page} link={link} chip={chip} />
      )}
    </div>
  );
}

interface ViewProps {
  page: number;
  link: (next: Record<string, string>) => string;
  chip: (active: boolean) => string;
}

async function ConversationsView({ search, operator, page, link, chip }: ViewProps & { search: string; operator: boolean }) {
  const [t, errors] = await Promise.all([getTranslations("Messages"), getTranslations("Errors")]);
  const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
  if (search) query.set("search", search);
  if (operator) query.set("needsOperator", "true");
  const result = await api<Paged<Conversation>>(`/whatsapp/conversations?${query}`);
  return (
    <section className="grid gap-4" aria-label={t("viewConversations")}>
      <div className="flex flex-wrap items-center gap-2">
        <form method="get" role="search" className="w-full sm:max-w-xs">
          <input type="hidden" name="view" value="conversations" />
          {operator && <input type="hidden" name="operator" value="1" />}
          <LiveSearchInput defaultValue={search} placeholder={t("searchPhone")} label={t("searchPhone")} />
        </form>
        <Link href={link({ operator: operator ? "" : "1", page: "" })} aria-current={operator ? "page" : undefined} className={chip(operator)}>
          {t("needsOperator")}
        </Link>
      </div>
      {!result.ok ? (
        <ErrorState title={errors("unknown")} />
      ) : result.data.items.length === 0 ? (
        <EmptyState icon={MessagesSquare} title={t("noConversations")} description={t("noConversationsHint")} />
      ) : (
        <>
          <ul className="grid gap-2">
            {result.data.items.map((c) => {
              const left = c.windowOpen ? windowLeft(c.windowClosesAt) : null;
              return (
                <li key={c.id}>
                  <Link
                    href={`/admin/messages/${c.id}`}
                    className={cn(
                      "grid gap-1 rounded-2xl border bg-card p-4 shadow-xs transition hover:border-primary/40",
                      c.needsOperator && "border-status-reserved/50",
                    )}
                  >
                    <span className="flex flex-wrap items-center justify-between gap-2">
                      <span className="flex flex-wrap items-center gap-2">
                        <span className="font-medium">{c.guestName ?? c.profileName ?? t("unknownGuest")}</span>
                        <span className="tabular text-sm text-muted-foreground">{formatPhone(c.phone)}</span>
                        {c.needsOperator && <Badge variant="destructive">{t("needsOperatorBadge")}</Badge>}
                        {c.unreadCount > 0 && <Badge>{t("unread", { count: c.unreadCount })}</Badge>}
                      </span>
                      <span className="text-xs text-muted-foreground">{formatHotelDateTime(c.lastMessageAt)}</span>
                    </span>
                    {c.lastMessage && <span className="line-clamp-1 text-sm text-muted-foreground">{c.lastMessage}</span>}
                    <span className="text-xs text-muted-foreground">{left ? t("windowLeft", { left }) : t("windowClosed")}</span>
                  </Link>
                </li>
              );
            })}
          </ul>
          <Pager page={page} pages={result.data.totalPages} link={link} labels={{ nav: t("pages"), prev: t("prev"), next: t("next"), of: t("pageOf", { page, pages: result.data.totalPages }) }} />
        </>
      )}
    </section>
  );
}

async function HistoryView({ channel, status, page, link, chip }: ViewProps & { channel: string; status: string }) {
  const [t, errors] = await Promise.all([getTranslations("Messages"), getTranslations("Errors")]);
  const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
  if (channel) query.set("channel", channel);
  if (status) query.set("status", status);
  const result = await api<Paged<Message>>(`/messages?${query}`);
  return (
    <section className="grid gap-4" aria-label={t("viewHistory")}>
      <div className="grid gap-2">
        <nav aria-label={t("channel")} className="flex flex-wrap gap-1.5">
          {channels.map((c) => (
            <Link key={c || "all"} href={link({ channel: c, page: "" })} aria-current={c === channel ? "page" : undefined} className={chip(c === channel)}>
              {c || t("allChannels")}
            </Link>
          ))}
        </nav>
        <nav aria-label={t("status")} className="flex flex-wrap gap-1.5">
          {statuses.map((s) => (
            <Link key={s || "all"} href={link({ status: s, page: "" })} aria-current={s === status ? "page" : undefined} className={chip(s === status)}>
              {s ? messageStatusLabel[s] : t("allStatuses")}
            </Link>
          ))}
        </nav>
      </div>
      {!result.ok ? (
        <ErrorState title={errors("unknown")} />
      ) : result.data.items.length === 0 ? (
        <EmptyState icon={MessagesSquare} title={t("noMessages")} description={t("noMessagesHint")} />
      ) : (
        <>
          <div className="overflow-x-auto rounded-2xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("colTime")}</TableHead>
                  <TableHead>{t("colGuest")}</TableHead>
                  <TableHead>{t("colMessage")}</TableHead>
                  <TableHead>{t("colStatus")}</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.data.items.map((m) => (
                  <TableRow key={m.id}>
                    <TableCell className="text-sm whitespace-nowrap">{formatHotelDateTime(m.createdAt)}</TableCell>
                    <TableCell className="text-sm">
                      {m.guestId && m.guestName ? (
                        <Link href={`/admin/guests/${m.guestId}`} className="font-medium underline-offset-4 hover:underline">
                          {m.guestName}
                        </Link>
                      ) : (
                        <span className="text-muted-foreground">{m.recipient ? formatPhone(m.recipient) : "—"}</span>
                      )}
                      {m.bookingId && m.bookingNumber && (
                        <Link href={`/admin/bookings/${m.bookingId}`} className="tabular block text-xs text-muted-foreground underline-offset-4 hover:underline">
                          {m.bookingNumber}
                        </Link>
                      )}
                    </TableCell>
                    <TableCell className="max-w-md text-sm">
                      <span className="flex flex-wrap items-center gap-2">
                        <ChannelLabel channel={m.channel} />
                        <span className="font-medium">{m.templateTitle ?? m.subject ?? messageKindLabel[m.kind]}</span>
                      </span>
                      {m.status !== "Skipped" && <span className="line-clamp-2 text-muted-foreground">{m.body}</span>}
                      {m.error && <span className={cn("block text-xs", m.status === "Failed" ? "text-destructive" : "text-muted-foreground")}>{m.error}</span>}
                    </TableCell>
                    <TableCell>{m.direction === "Out" ? <MessageStatusBadge status={m.status} /> : <span className="text-xs text-muted-foreground">{t("incoming")}</span>}</TableCell>
                    <TableCell>{m.canRetry && <RetryMessageButton id={m.id} />}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <Pager page={page} pages={result.data.totalPages} link={link} labels={{ nav: t("pages"), prev: t("prev"), next: t("next"), of: t("pageOf", { page, pages: result.data.totalPages }) }} />
        </>
      )}
    </section>
  );
}

function Pager({ page, pages, link, labels }: { page: number; pages: number; link: ViewProps["link"]; labels: { nav: string; prev: string; next: string; of: string } }) {
  if (pages <= 1) return null;
  return (
    <nav className="flex items-center gap-3 text-sm" aria-label={labels.nav}>
      {page > 1 && <Link href={link({ page: String(page - 1) })}>{labels.prev}</Link>}
      <span>{labels.of}</span>
      {page < pages && <Link href={link({ page: String(page + 1) })}>{labels.next}</Link>}
    </nav>
  );
}
