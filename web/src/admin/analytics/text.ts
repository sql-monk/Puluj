// Shared-word highlighting of a detected pair: purely client-side, so the "Знахідки" tab shows what the two texts
// have in common without another endpoint. Tokens are lower-cased runs of letters/digits of at least MIN_TOKEN
// characters; shorter words ("на", "від", "ppo") are noise for this purpose.

export const MIN_TOKEN = 4

const WORD = /[\p{L}\p{N}]+/gu
const SPLIT = /([\p{L}\p{N}]+)/u

export function tokens(text: string | null | undefined): string[] {
  if (!text) return []
  const out: string[] = []
  for (const m of text.matchAll(WORD)) {
    const t = m[0].toLowerCase()
    if (t.length >= MIN_TOKEN) out.push(t)
  }
  return out
}

/** Distinct tokens present in both texts. */
export function sharedWords(a: string | null | undefined, b: string | null | undefined): Set<string> {
  const ta = new Set(tokens(a))
  const out = new Set<string>()
  for (const t of tokens(b)) if (ta.has(t)) out.add(t)
  return out
}

export interface Segment {
  text: string
  shared: boolean
}

/** Splits a text into runs, marking the words that belong to `shared`; separators are kept so the text renders unchanged. */
export function highlight(text: string | null | undefined, shared: Set<string>): Segment[] {
  if (!text) return []
  const parts = text.split(SPLIT)
  const out: Segment[] = []
  for (const part of parts) {
    if (part === '') continue
    const hit = shared.has(part.toLowerCase())
    const last = out[out.length - 1]
    if (last && last.shared === hit) last.text += part
    else out.push({ text: part, shared: hit })
  }
  return out
}

/** Share of the text's tokens that are shared: 0..1, null when the text has no tokens. */
export function sharedShare(text: string | null | undefined, shared: Set<string>): number | null {
  const ts = tokens(text)
  if (ts.length === 0) return null
  return ts.filter((t) => shared.has(t)).length / ts.length
}
