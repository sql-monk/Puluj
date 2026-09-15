import { describe, expect, it } from 'vitest'
import { compact, hoursText, labelEvery, niceTicks, parsePeriodHash, periodHash, presetPeriod, roundedNow, routeMatrix } from './period'

const NOW = new Date('2026-09-15T10:07:42Z')

describe('period presets', () => {
  it('rounds "now" down to 5 minutes so every viewer shares one cache entry', () => {
    expect(roundedNow(NOW).toISOString()).toBe('2026-09-15T10:05:00.000Z')
  })

  it('builds the preset window backwards from the rounded end', () => {
    const p = presetPeriod('7d', NOW)
    expect(p.to.toISOString()).toBe('2026-09-15T10:05:00.000Z')
    expect(p.from.toISOString()).toBe('2026-09-08T10:05:00.000Z')
    expect(p.preset).toBe('7d')
  })

  it('reads the period from the hash and writes it back the same way', () => {
    expect(parsePeriodHash('#/stats', NOW).preset).toBe('24h')
    expect(parsePeriodHash('#/stats?p=30d', NOW).preset).toBe('30d')
    const custom = parsePeriodHash('#/stats?from=2025-03-01T00%3A00%3A00.000Z&to=2025-04-01T00%3A00%3A00.000Z', NOW)
    expect(custom.preset).toBe('custom')
    expect(custom.from.toISOString()).toBe('2025-03-01T00:00:00.000Z')
    expect(periodHash(custom)).toBe('#/stats?from=2025-03-01T00%3A00%3A00.000Z&to=2025-04-01T00%3A00%3A00.000Z')
    expect(periodHash(presetPeriod('7d', NOW))).toBe('#/stats?p=7d')
    expect(periodHash(presetPeriod('24h', NOW))).toBe('#/stats')
  })

  it('falls back to 24 h on an unreadable or inverted custom range', () => {
    expect(parsePeriodHash('#/stats?from=abc&to=def', NOW).preset).toBe('24h')
    expect(parsePeriodHash('#/stats?from=2025-04-01T00:00:00Z&to=2025-03-01T00:00:00Z', NOW).preset).toBe('24h')
  })
})

describe('formatting', () => {
  it('compacts large figures', () => {
    expect(compact(1284)).toBe((1284).toLocaleString('uk-UA'))
    expect(compact(12900)).toContain('тис.')
    expect(compact(4_200_000)).toContain('млн')
  })

  it('prints hours as minutes, hours or days', () => {
    expect(hoursText(0.5)).toBe('30 хв')
    expect(hoursText(2.5)).toContain('год')
    expect(hoursText(50)).toBe('2 д 2 год')
  })

  it('picks clean axis ticks that cover the maximum', () => {
    expect(niceTicks(0)).toEqual([0])
    const t = niceTicks(87)
    expect(t[0]).toBe(0)
    expect(t[t.length - 1]).toBeGreaterThanOrEqual(87)
    expect(t.length).toBeLessThanOrEqual(6)
  })

  it('labels every k-th bucket', () => {
    expect(labelEvery(24, 12)).toBe(2)
    expect(labelEvery(7, 12)).toBe(1)
  })
})

describe('routeMatrix', () => {
  it('keeps the busiest origins and destinations and fills the cells from the pairs', () => {
    const routes = [
      { fromId: 1, fromName: 'A', toId: 10, toName: 'X', count: 5 },
      { fromId: 2, fromName: 'B', toId: 10, toName: 'X', count: 3 },
      { fromId: 1, fromName: 'A', toId: 11, toName: 'Y', count: 1 },
      { fromId: 3, fromName: 'C', toId: 12, toName: 'Z', count: 1 },
    ]
    const m = routeMatrix(routes, 2)
    expect(m.rows.map((r) => r.name)).toEqual(['A', 'B'])
    expect(m.cols.map((c) => c.name)).toEqual(['X', 'Y'])
    expect(m.values).toEqual([
      [5, 1],
      [3, 0],
    ])
  })
})
