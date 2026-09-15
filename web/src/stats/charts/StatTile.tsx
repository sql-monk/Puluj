/** A headline number: label above, the value in the system sans (proportional figures), an optional note below. */
export default function StatTile({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="flex min-w-0 flex-col rounded-xl bg-white px-3 py-2.5 shadow-sm dark:bg-slate-900">
      <span className="truncate text-xs text-slate-500 dark:text-slate-400">{label}</span>
      <span className="text-2xl font-semibold leading-tight">{value}</span>
      {note && <span className="truncate text-[11px] text-slate-500 dark:text-slate-400">{note}</span>}
    </div>
  )
}
