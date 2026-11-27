import { Mail, MessageCircle } from "lucide-react";
import type { MessageChannel, MessageStatus } from "@/lib/api/types";
import { channelLabel, messageStatusLabel, messageStatusTone } from "@/lib/messaging/labels";
import { cn } from "@/lib/utils";

const base = "inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-medium";

export function MessageStatusBadge({ status, className }: { status: MessageStatus; className?: string }) {
  return (
    <span className={cn(base, messageStatusTone[status], className)}>
      <span className="size-1.5 rounded-full bg-current" aria-hidden />
      {messageStatusLabel[status]}
    </span>
  );
}

export function ChannelLabel({ channel }: { channel: MessageChannel }) {
  const Icon = channel === "WhatsApp" ? MessageCircle : Mail;
  return (
    <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
      <Icon className="size-3.5" aria-hidden />
      {channelLabel[channel]}
    </span>
  );
}
