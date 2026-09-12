import { useStore, type Theme } from '../store/useStore'

export type Page = 'ukraine' | 'kyiv'

const themeOrder: Theme[] = ['system', 'light', 'dark']
const themeIcon: Record<Theme, string> = { system: '◐', light: '☀', dark: '☾' }

export default function TopBar({ page, onPage, onToggleMenu, onOpenSettings, onReplay, replay, setupHint }: { page: Page; onPage: (p: Page) => void; onToggleMenu: () => void; onOpenSettings: () => void; onReplay: () => void; replay: boolean; setupHint: boolean }) {
  const connection = useStore((s) => s.connection)
  const mode = useStore((s) => s.mode)
  const at = useStore((s) => s.at)
  const theme = useStore((s) => s.theme)
  const setTheme = useStore((s) => s.setTheme)
  const error = useStore((s) => s.error)

  const dot = connection === 'connected' ? 'bg-emerald-500' : connection === 'reconnecting' ? 'bg-amber-500 animate-pulse' : 'bg-red-500'
  const status = connection === 'connected' ? 'онлайн' : connection === 'reconnecting' ? 'перепідключення…' : 'офлайн'

  return (
    <header className="pointer-events-auto absolute left-0 right-0 top-0 z-20 flex items-center gap-3 bg-white/90 px-3 py-2 text-sm shadow backdrop-blur dark:bg-slate-900/90 dark:text-slate-100">
      <button className="rounded px-2 py-1 hover:bg-slate-200 md:hidden dark:hover:bg-slate-700" onClick={onToggleMenu} aria-label="Меню">
        ☰
      </button>
      <span className="font-semibold tracking-wide">Puluj</span>
      <span className="hidden text-slate-500 lg:inline dark:text-slate-400">ситуаційне оповіщення · OSINT</span>
      <span className="ml-2 inline-flex overflow-hidden rounded-md border border-slate-300 text-xs dark:border-slate-600" role="tablist" aria-label="Карта">
        {(['ukraine', 'kyiv'] as Page[]).map((v) => (
          <button
            key={v}
            role="tab"
            aria-selected={page === v}
            className={`px-2.5 py-1 ${page === v ? 'bg-blue-600 text-white' : 'hover:bg-slate-200 dark:hover:bg-slate-700'}`}
            onClick={() => onPage(v)}
          >
            {v === 'ukraine' ? 'Україна' : 'Київ'}
          </button>
        ))}
      </span>
      <span className="ml-auto flex items-center gap-2">
        {mode === 'history' && at && (
          <span className="rounded bg-indigo-600 px-2 py-0.5 text-xs font-medium text-white">
            історія · {at.toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}
          </span>
        )}
        <button className={`rounded px-2 py-1 text-xs hover:bg-slate-200 dark:hover:bg-slate-700 ${replay ? 'bg-indigo-100 text-indigo-800 dark:bg-indigo-900 dark:text-indigo-100' : ''}`} title="Відтворення історії" onClick={onReplay}>
          ⏱ <span className="hidden sm:inline">Історія</span>
        </button>
        {error && <span className="rounded bg-red-600 px-2 py-0.5 text-xs text-white">{error}</span>}
        {setupHint && (
          <button className="rounded bg-amber-500 px-2 py-0.5 text-xs font-medium text-white hover:bg-amber-600" onClick={onOpenSettings}>
            Джерела не налаштовані →
          </button>
        )}
        <span className={`inline-block h-2.5 w-2.5 rounded-full ${dot}`} title={status} />
        <span className="hidden text-xs text-slate-500 sm:inline dark:text-slate-400">{status}</span>
        <button className="rounded px-2 py-1 hover:bg-slate-200 dark:hover:bg-slate-700" title="Налаштування" onClick={onOpenSettings} aria-label="Налаштування">
          ⚙
        </button>
        <button
          className="rounded px-2 py-1 hover:bg-slate-200 dark:hover:bg-slate-700"
          title={`Тема: ${theme}`}
          onClick={() => setTheme(themeOrder[(themeOrder.indexOf(theme) + 1) % themeOrder.length])}
        >
          {themeIcon[theme]}
        </button>
      </span>
    </header>
  )
}
