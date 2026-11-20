import { cn } from "@/lib/utils";
import { roomStatusLabel, roomStatusTone, type DisplayStatus } from "@/lib/rooms/status";

export function RoomStatusBadge({ status, className }: { status: DisplayStatus; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-medium",
        roomStatusTone[status],
        className,
      )}
    >
      <span className="size-1.5 rounded-full bg-current" aria-hidden />
      {roomStatusLabel[status]}
    </span>
  );
}
