import { useMemo } from 'react'
import type { RecentCopyDto } from '../../api/analytics'
import { Badge } from '../../components/settings/fields'
import { fmtDelay, fmtTime, kindLabel, pct } from './format'
import { highlight, sharedShare, sharedWords } from './text'

/** One detected pair: both texts side by side, the words they share highlighted (client-side token intersection). */
export function FindingCard({ item, nameOf, compact = false }: { item: RecentCopyDto; nameOf: (id: number) => string; compact?: boolean }) {
  const shared = useMemo(() => sharedWords(item.originalText, item.copyText), [item.originalText, item.copyText])
  const share = sharedShare(item.copyText, shared)
  return (
    <div className="rounded border border-slate-100 p-2 text-xs dark:border-slate-800">
      <div className="mb-1 flex flex-wrap items-center gap-2 text-slate-500">
        <span>
          <b className="text-slate-700 dark:text-slate-200">{nameOf(item.copierId)}</b> ← {nameOf(item.originalId)}
        </span>
        <Badge ok={item.kind === 'forward' ? true : null} text={kindLabel(item.kind)} />
        <span>через {fmtDelay(item.delaySeconds)}</span>
        <span className="font-mono" title="Жаккар · вкладеність · частка спільних слів у копії">
          J {item.jaccard.toFixed(2)} · C {item.containment.toFixed(2)} · слів {pct(share)}
        </span>
        {!item.isPrimary && <span className="text-slate-400">проміжний оригінал</span>}
        <span className="ml-auto">{fmtTime(item.copyPublishedAt)}</span>
      </div>
      <div className={`grid gap-2 ${compact ? '' : 'sm:grid-cols-2'}`}>
        <Quote label="оригінал" text={item.originalText} url={item.originalUrl} at={item.originalPublishedAt} shared={shared} />
        <Quote label="копія" text={item.copyText} url={item.copyUrl} at={item.copyPublishedAt} shared={shared} />
      </div>
    </div>
  )
}

function Quote({ label, text, url, at, shared }: { label: string; text?: string; url?: string; at: string; shared: Set<string> }) {
  const segments = highlight(text, shared)
  return (
    <div className="rounded bg-slate-50 p-2 dark:bg-slate-800/60">
      <div className="mb-0.5 text-[10px] uppercase tracking-wide text-slate-400">
        {label} · {fmtTime(at)}
        {url && (
          <a className="ml-1 underline" href={url} target="_blank" rel="noreferrer">
            відкрити
          </a>
        )}
      </div>
      <div className="whitespace-pre-wrap break-words text-slate-700 dark:text-slate-200">
        {segments.length === 0 ? '—' : segments.map((s, i) => (s.shared ? <mark key={i} className="rounded bg-amber-200/70 px-0.5 text-inherit dark:bg-amber-500/30">{s.text}</mark> : <span key={i}>{s.text}</span>))}
      </div>
    </div>
  )
}
