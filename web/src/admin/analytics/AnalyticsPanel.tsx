import { useEffect, useRef, useState } from 'react'
import { analytics } from '../../api/analytics'
import { Section } from '../../components/settings/fields'
import { useLoad } from './data'
import { FindingsTab } from './FindingsTab'
import { FirstsTab } from './FirstsTab'
import { OverviewTab } from './OverviewTab'
import { PairsTab, type PairFilter } from './PairsTab'
import { ServiceTab } from './ServiceTab'
import { SourcesTab } from './SourcesTab'

type Tab = 'overview' | 'sources' | 'pairs' | 'firsts' | 'findings' | 'service'

const TABS: { id: Tab; label: string }[] = [
  { id: 'overview', label: 'Огляд' },
  { id: 'sources', label: 'Джерела' },
  { id: 'pairs', label: 'Пари' },
  { id: 'firsts', label: 'Першоджерела' },
  { id: 'findings', label: 'Знахідки' },
  { id: 'service', label: 'Сервіс' },
]

const PERIODS = [7, 14, 30]

/** The menu has two entries that both render this panel: `#/analytics` (content) and `#/analytics-service` (the service tab). */
const SERVICE_HASH = '#/analytics-service'
const CONTENT_HASH = '#/analytics'

function tabFromHash(): Tab | null {
  return window.location.hash.replace(/\?.*$/, '') === SERVICE_HASH ? 'service' : null
}

// Remembered across mounts: the admin shell re-mounts the panel when the hash flips between the two menu entries.
let lastContentTab: Tab = 'overview'
let lastDays = 14

/**
 * The source analytics page: what the Puluj.Analytics service found in the message texts (who copies whom, how fast,
 * how literally; forwards; activity by hour; who reports tracks first) and how the service itself is doing. Distinct
 * from "Рейтинг джерел", which counts repeated *facts* from the parser: this one compares texts, parser or no parser.
 */
export default function AnalyticsPanel() {
  const [tab, setTabState] = useState<Tab>(() => tabFromHash() ?? lastContentTab)
  const [days, setDaysState] = useState(lastDays)
  const [pairFilter, setPairFilter] = useState<PairFilter>({})
  const [selectedPair, setSelectedPair] = useState<PairFilter | null>(null)
  const tabRef = useRef(tab)
  tabRef.current = tab

  const setTab = (t: Tab) => {
    if (t !== 'service') lastContentTab = t
    setTabState(t)
    const want = t === 'service' ? SERVICE_HASH : CONTENT_HASH
    if (window.location.hash.replace(/\?.*$/, '') !== want) window.location.assign(want)
  }
  const setDays = (d: number) => {
    lastDays = d
    setDaysState(d)
  }
  useEffect(() => {
    const onHash = () => {
      const fromHash = tabFromHash()
      if (fromHash === 'service') setTabState('service')
      else if (tabRef.current === 'service') setTabState(lastContentTab)
    }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  const status = useLoad('status', () => analytics.status(), tab === 'service' ? 10_000 : 60_000)
  const report = useLoad(`report:${days}`, () => analytics.report(days), 60_000)

  const notInitialized = status.data?.initialized === false || report.error?.status === 404
  const usesPeriod = tab === 'overview' || tab === 'sources' || tab === 'pairs' || tab === 'firsts'

  const openPair = (copierId: number, originalId: number) => {
    setPairFilter({ copierId, originalId })
    setSelectedPair({ copierId, originalId })
    setTab('pairs')
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-1 rounded-lg border border-slate-200 bg-white p-1 text-sm dark:border-slate-700 dark:bg-slate-900">
        {TABS.map((t) => (
          <button key={t.id} className={`rounded px-3 py-1 ${tab === t.id ? 'bg-slate-200 font-medium dark:bg-slate-700' : 'hover:bg-slate-100 dark:hover:bg-slate-800'}`} onClick={() => setTab(t.id)}>
            {t.label}
          </button>
        ))}
        {usesPeriod && (
          <span className="ml-auto flex items-center gap-1 text-xs">
            <span className="text-slate-500">період:</span>
            {PERIODS.map((d) => (
              <button key={d} className={`rounded px-2 py-0.5 ${days === d ? 'bg-slate-200 font-medium dark:bg-slate-700' : 'hover:bg-slate-100 dark:hover:bg-slate-800'}`} onClick={() => setDays(d)}>
                {d} дн.
              </button>
            ))}
          </span>
        )}
      </div>

      {tab === 'service' ? (
        <ServiceTab status={status.data} error={status.error?.message ?? null} fetchedAt={status.at} reload={status.reload} />
      ) : notInitialized ? (
        <NotInitialized onService={() => setTab('service')} />
      ) : (
        <>
          {report.error && report.error.status !== 404 && <div className="text-xs text-red-600">{report.error.message}</div>}
          {!report.data && !report.error && <div className="text-xs text-slate-500">Завантаження…</div>}
          {tab === 'findings' && <FindingsTab report={report.data} />}
          {report.data && tab === 'overview' && <OverviewTab report={report.data} onPair={openPair} />}
          {report.data && tab === 'sources' && <SourcesTab report={report.data} />}
          {report.data && tab === 'pairs' && <PairsTab report={report.data} days={days} filter={pairFilter} setFilter={setPairFilter} selected={selectedPair} setSelected={setSelectedPair} />}
          {report.data && tab === 'firsts' && <FirstsTab report={report.data} />}
        </>
      )}
    </>
  )
}

function NotInitialized({ onService }: { onService: () => void }) {
  return (
    <Section title="Аналітика ще не зібрана">
      <p className="text-xs text-slate-600 dark:text-slate-300">
        Сервіс аналітики ще не запускався: схеми <code>analytics</code> у базі немає, тож нема ані джерел, ані пар, ані першоджерел. Запустіть контейнер <code>analytics</code> (локально — <code>Puluj.Analytics.Worker</code>, його піднімає <code>scripts/dev-run.ps1</code>); перший прогін над усією історією триває 10–20 хвилин, після нього ця сторінка заповниться сама.
      </p>
      <button className="rounded border border-slate-300 px-3 py-1 text-xs dark:border-slate-600" onClick={onService}>
        Стан сервісу
      </button>
    </Section>
  )
}
