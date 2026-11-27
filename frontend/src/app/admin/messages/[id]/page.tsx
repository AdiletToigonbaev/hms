import { ArrowLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { AutoRefresh } from "@/components/auto-refresh";
import { MessageTimeline } from "@/components/messages/message-timeline";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api/server";
import type { Conversation, Message, Paged, SendOptions } from "@/lib/api/types";
import { formatPhone, windowLeft } from "@/lib/messaging/labels";
import { requestNow } from "@/lib/time/request-now";
import { ResolveConversationButton, SendMessageForm } from "../message-forms";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("Messages"))("conversation") };
}

/** Диалог WhatsApp с номером: переписка, автоответы, ответ администратора, закрытие обращения. */
export default async function ConversationPage({ params }: PageProps<"/admin/messages/[id]">) {
  const now = await requestNow();
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();

  const [t, conversation, messages, options] = await Promise.all([
    getTranslations("Messages"),
    api<Conversation>(`/whatsapp/conversations/${id}`),
    api<Paged<Message>>(`/messages?conversationId=${id}&pageSize=100`),
    api<SendOptions>(`/messages/send-options?conversationId=${id}`),
  ]);
  if (!conversation.ok) notFound();
  const c = conversation.data;
  const left = c.windowOpen ? windowLeft(c.windowClosesAt, now) : null;

  return (
    <div className="grid gap-6">
      <AutoRefresh seconds={15} />
      <div className="grid gap-3">
        <Link href="/admin/messages" className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden />
          {t("back")}
        </Link>
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-3xl">{c.guestName ?? c.profileName ?? t("unknownGuest")}</h1>
          {c.needsOperator && <Badge variant="destructive">{t("needsOperatorBadge")}</Badge>}
        </div>
        <p className="tabular text-sm text-muted-foreground">
          {formatPhone(c.phone)}
          {c.guestId && (
            <>
              {" · "}
              <Link href={`/admin/guests/${c.guestId}`} className="underline-offset-4 hover:underline">
                {t("openGuest")}
              </Link>
            </>
          )}
          {" · "}
          {left ? t("windowLeft", { left }) : t("windowClosed")}
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-xl">{t("history")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {!messages.ok || messages.data.items.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("noMessages")}</p>
            ) : (
              <MessageTimeline messages={messages.data.items} canRetry />
            )}
          </CardContent>
        </Card>

        <div className="grid content-start gap-6">
          <Card>
            <CardHeader>
              <CardTitle>
                <h2 className="text-xl">{t("reply")}</h2>
              </CardTitle>
              <CardDescription>{t("replyHint")}</CardDescription>
            </CardHeader>
            <CardContent>
              {options.ok ? (
                <SendMessageForm options={options.data} conversationId={c.id} guestId={c.guestId} whatsAppOnly />
              ) : (
                <p className="text-sm text-muted-foreground">{t("cannotSend")}</p>
              )}
            </CardContent>
          </Card>

          {c.needsOperator && (
            <Card>
              <CardHeader>
                <CardTitle>
                  <h2 className="text-xl">{t("operatorTitle")}</h2>
                </CardTitle>
                <CardDescription>{t("operatorHint")}</CardDescription>
              </CardHeader>
              <CardContent>
                <ResolveConversationButton id={c.id} />
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
