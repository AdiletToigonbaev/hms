import Link from "next/link";
import { RetryMessageButton } from "@/app/admin/messages/message-forms";
import { ChannelLabel, MessageStatusBadge } from "@/components/message-badges";
import type { Message } from "@/lib/api/types";
import { formatHotelDateTime } from "@/lib/format";
import { messageKindLabel } from "@/lib/messaging/labels";
import { cn } from "@/lib/utils";

/**
 * Лента сообщений: входящие слева, исходящие справа (как в мессенджере), со статусом доставки,
 * каналом и автором. Сообщения приходят от новых к старым — показываем по времени.
 */
export function MessageTimeline({ messages, canRetry, showGuest = false }: { messages: Message[]; canRetry: boolean; showGuest?: boolean }) {
  const ordered = [...messages].reverse();
  return (
    <ol className="grid gap-3" aria-label="Сообщения">
      {ordered.map((m) => {
        const incoming = m.direction === "In";
        return (
          <li key={m.id} className={cn("grid max-w-[min(36rem,92%)] gap-1", incoming ? "justify-self-start" : "justify-self-end")}>
            <div className={cn("rounded-2xl border px-3.5 py-2.5 text-sm shadow-xs", incoming ? "rounded-bl-md bg-card" : "rounded-br-md bg-primary/8")}>
              {m.subject && <p className="mb-1 font-medium">{m.subject}</p>}
              {m.status === "Skipped" ? (
                <p className="text-muted-foreground">{m.templateTitle ?? messageKindLabel[m.kind]}</p>
              ) : (
                <p className="break-words whitespace-pre-line">{m.body}</p>
              )}
            </div>
            <div className={cn("flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground", !incoming && "justify-end")}>
              <span>{formatHotelDateTime(m.createdAt)}</span>
              <ChannelLabel channel={m.channel} />
              <span>{m.kind === "Manual" && m.sentByName ? m.sentByName : messageKindLabel[m.kind]}</span>
              {m.templateTitle && m.kind !== "Manual" && <span>· {m.templateTitle}</span>}
              {showGuest && m.guestId && m.guestName && (
                <Link href={`/admin/guests/${m.guestId}`} className="underline-offset-4 hover:underline">
                  {m.guestName}
                </Link>
              )}
              {m.bookingId && m.bookingNumber && (
                <Link href={`/admin/bookings/${m.bookingId}`} className="tabular underline-offset-4 hover:underline">
                  {m.bookingNumber}
                </Link>
              )}
              {!incoming && <MessageStatusBadge status={m.status} />}
              {m.fallbackFor && <span>· письмо вместо WhatsApp</span>}
            </div>
            {m.error && (m.status === "Failed" || m.status === "Skipped" || m.status === "Queued") && (
              <p className={cn("text-xs", m.status === "Failed" ? "text-destructive" : "text-muted-foreground", !incoming && "text-right")}>{m.error}</p>
            )}
            {canRetry && m.canRetry && (
              <div className="justify-self-end">
                <RetryMessageButton id={m.id} />
              </div>
            )}
          </li>
        );
      })}
    </ol>
  );
}
