import type { BookingStatus } from "@/lib/api/types";
import { bookingStatusLabel, bookingStatusTone } from "@/lib/bookings/status";
import { cn } from "@/lib/utils";

export function BookingStatusBadge({ status, className }: { status: BookingStatus; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 whitespace-nowrap rounded-full border px-2 py-0.5 text-xs font-medium",
        bookingStatusTone[status],
        className,
      )}
    >
      <span className="size-1.5 rounded-full bg-current" aria-hidden />
      {bookingStatusLabel[status]}
    </span>
  );
}
