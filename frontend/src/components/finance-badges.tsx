import type { PaymentStatus, RefundStatus } from "@/lib/api/types";
import { paymentStatusLabel, paymentStatusTone, refundStatusLabel, refundStatusTone } from "@/lib/finance/labels";
import { cn } from "@/lib/utils";

const base = "inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-medium";

export function PaymentStatusBadge({ status, className }: { status: PaymentStatus; className?: string }) {
  return (
    <span className={cn(base, paymentStatusTone[status], className)}>
      <span className="size-1.5 rounded-full bg-current" aria-hidden />
      {paymentStatusLabel[status]}
    </span>
  );
}

export function RefundStatusBadge({ status, className }: { status: RefundStatus; className?: string }) {
  return (
    <span className={cn(base, refundStatusTone[status], className)}>
      <span className="size-1.5 rounded-full bg-current" aria-hidden />
      {refundStatusLabel[status]}
    </span>
  );
}
