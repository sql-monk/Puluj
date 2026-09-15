import { describe, expect, it } from 'vitest'
import { ago, fmtBucketLabel, fmtBytes, fmtDay, fmtDelay, fmtDuration, fmtDurationMs, fmtInt, fmtRate, kindLabel, pct, uptime } from './format'

const NOW = Date.parse('2026-09-15T10:00:00Z')

describe('numbers', () => {
  it('groups thousands with a thin no-break space', () => {
    expect(fmtInt(0)).toBe('0')
    expect(fmtInt(999)).toBe('999')
    expect(fmtInt(1234)).toBe('1 234')
    expect(fmtInt(1234567)).toBe('1 234 567')
    expect(fmtInt(-1500)).toBe('−1 500')
    expect(fmtInt(2.6)).toBe('3')
  })
  it('formats percentages and handles missing values', () => {
    expect(pct(0.256)).toBe('26%')
    expect(pct(0.256, 1)).toBe('25.6%')
    expect(pct(null)).toBe('—')
    expect(pct(undefined)).toBe('—')
  })
  it('formats bytes', () => {
    expect(fmtBytes(512)).toBe('512 Б')
    expect(fmtBytes(20 * 1024)).toBe('20 КБ')
    expect(fmtBytes(1.5 * 1024 * 1024)).toBe('1.5 МБ')
    expect(fmtBytes(3 * 1024 ** 3)).toBe('3.00 ГБ')
    expect(fmtBytes(null)).toBe('—')
  })
})

describe('delays and durations', () => {
  it('picks the unit by magnitude', () => {
    expect(fmtDelay(null)).toBe('—')
    expect(fmtDelay(45)).toBe('45 с')
    expect(fmtDelay(89.6)).toBe('90 с')
    expect(fmtDelay(90)).toBe('2 хв')
    expect(fmtDelay(720)).toBe('12 хв')
    expect(fmtDelay(5400)).toBe('1.5 год')
    expect(fmtDelay(200000)).toBe('2.3 д')
  })
  it('formats durations in ms', () => {
    expect(fmtDurationMs(850)).toBe('850 мс')
    expect(fmtDurationMs(12_500)).toBe('12.5 с')
    expect(fmtDurationMs(18 * 60_000)).toBe('18 хв')
    expect(fmtDurationMs(2.1 * 3_600_000)).toBe('2.1 год')
    expect(fmtDurationMs(-1)).toBe('—')
  })
  it('measures an unfinished run against now', () => {
    expect(fmtDuration('2026-09-15T09:58:00Z', '2026-09-15T09:59:00Z')).toBe('60.0 с')
    expect(fmtDuration('2026-09-15T09:58:00Z', '2026-09-15T09:59:30Z')).toBe('2 хв')
    expect(fmtDuration('2026-09-15T09:30:00Z', null, NOW)).toBe('30 хв')
  })
  it('formats uptime', () => {
    expect(uptime(null)).toBe('—')
    expect(uptime('2026-09-15T09:56:00Z', NOW)).toBe('4 хв')
    expect(uptime('2026-09-15T06:48:00Z', NOW)).toBe('3 год 12 хв')
    expect(uptime('2026-09-13T05:00:00Z', NOW)).toBe('2 д 5 год')
  })
  it('formats "ago"', () => {
    expect(ago(undefined)).toBe('—')
    expect(ago('2026-09-15T09:59:30Z', NOW)).toBe('30 с тому')
    expect(ago('2026-09-15T09:45:00Z', NOW)).toBe('15 хв тому')
    expect(ago('2026-09-15T04:00:00Z', NOW)).toBe('6.0 год тому')
    expect(ago('2026-09-10T10:00:00Z', NOW)).toBe('5 д тому')
  })
  it('computes a throughput per second', () => {
    expect(fmtRate(1500, 60_000)).toBe('25.0/с')
    expect(fmtRate(120_000, 60_000)).toBe('2000/с')
    expect(fmtRate(10, 500)).toBe('—')
    expect(fmtRate(0, 5000)).toBe('0.0/с')
  })
})

describe('labels', () => {
  it('names the delay buckets', () => {
    expect(fmtBucketLabel(0, 60)).toBe('< 1 хв')
    expect(fmtBucketLabel(60, 300)).toBe('1–5 хв')
    expect(fmtBucketLabel(300, 900)).toBe('5–15 хв')
    expect(fmtBucketLabel(900, 3600)).toBe('15 хв–1 год')
    expect(fmtBucketLabel(3600, 21600)).toBe('1–6 год')
    expect(fmtBucketLabel(21600, null)).toBe('> 6 год')
  })
  it('formats days and kinds', () => {
    expect(fmtDay('2026-09-05')).toBe('05.09')
    expect(fmtDay('2026-09-05T00:00:00')).toBe('05.09')
    expect(fmtDay('today')).toBe('today')
    expect(kindLabel('forward')).toBe('пересилання')
    expect(kindLabel('other')).toBe('other')
  })
})
