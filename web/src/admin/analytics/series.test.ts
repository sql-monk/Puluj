import { describe, expect, it } from 'vitest'
import type { SourceAnalyticsDto } from '../../api/analytics'
import { bucketDelays, bucketIndex, DELAY_EDGES, histogramBars, niceMax, sumPerDay, ticks, weightedMedianDelay } from './series'

describe('delay buckets', () => {
  it('uses the same edges as the backend, lower edge inclusive', () => {
    expect(DELAY_EDGES).toEqual([60, 300, 900, 3600, 21600])
    expect(bucketIndex(0)).toBe(0)
    expect(bucketIndex(59.9)).toBe(0)
    expect(bucketIndex(60)).toBe(1)
    expect(bucketIndex(299)).toBe(1)
    expect(bucketIndex(300)).toBe(2)
    expect(bucketIndex(3600)).toBe(4)
    expect(bucketIndex(21600)).toBe(5)
    expect(bucketIndex(1e9)).toBe(5)
  })

  it('counts into six buckets, the last open-ended', () => {
    const b = bucketDelays([10, 70, 70, 400, 1000, 5000, 30000])
    expect(b.map((x) => x.count)).toEqual([1, 2, 1, 1, 1, 1])
    expect(b[0]).toEqual({ fromSeconds: 0, toSeconds: 60, count: 1 })
    expect(b[5]).toEqual({ fromSeconds: 21600, toSeconds: null, count: 1 })
    expect(bucketDelays([]).map((x) => x.count)).toEqual([0, 0, 0, 0, 0, 0])
  })

  it('derives shares and relative heights', () => {
    const bars = histogramBars(bucketDelays([10, 70, 70, 400]))
    expect(bars.map((b) => b.count)).toEqual([1, 2, 1, 0, 0, 0])
    expect(bars[1].share).toBeCloseTo(0.5)
    expect(bars[1].height).toBe(1)
    expect(bars[0].height).toBeCloseTo(0.5)
    expect(histogramBars(bucketDelays([])).every((b) => b.share === 0 && b.height === 0)).toBe(true)
  })
})

describe('axis scaling', () => {
  it('rounds the maximum up to a nice number', () => {
    expect(niceMax(0)).toBe(1)
    expect(niceMax(1)).toBe(1)
    expect(niceMax(3)).toBe(5)
    expect(niceMax(7)).toBe(10)
    expect(niceMax(12)).toBe(20)
    expect(niceMax(23)).toBe(25)
    expect(niceMax(26)).toBe(50)
    expect(niceMax(120)).toBe(200)
    expect(niceMax(1000)).toBe(1000)
  })
  it('splits the nice maximum into equal ticks starting at zero', () => {
    expect(ticks(7)).toEqual([0, 2.5, 5, 7.5, 10])
    expect(ticks(120, 2)).toEqual([0, 100, 200])
  })
})

function source(id: number, perDay: [number, number, number][], copies = 0, median: number | null = null): SourceAnalyticsDto {
  return {
    id,
    code: `s${id}`,
    name: `S${id}`,
    enabled: true,
    posts: perDay.reduce((n, d) => n + d[0], 0),
    edits: 0,
    copies,
    copiedBy: 0,
    medianCopyDelaySeconds: median,
    forwardsInternal: 0,
    forwardsExternal: 0,
    perHour: [],
    perDay: perDay.map(([posts, copies, copiedBy], i) => ({ day: `2026-09-0${i + 1}`, posts, copies, copiedBy })),
  }
}

describe('sumPerDay', () => {
  it('adds one field across sources, aligned with the report days', () => {
    const a = source(1, [[3, 1, 0], [2, 0, 1]])
    const b = source(2, [[1, 1, 2], [4, 2, 0]])
    expect(sumPerDay([a, b], 'posts', 2)).toEqual([4, 6])
    expect(sumPerDay([a, b], 'copies', 2)).toEqual([2, 2])
    expect(sumPerDay([a, b], 'copiedBy', 3)).toEqual([2, 1, 0])
    expect(sumPerDay([], 'posts', 2)).toEqual([0, 0])
  })
})

describe('weightedMedianDelay', () => {
  it('weights each source median by its copies and ignores sources without copies', () => {
    const a = source(1, [], 3, 60)
    const b = source(2, [], 1, 300)
    const c = source(3, [], 0, 9999)
    expect(weightedMedianDelay([a, b, c])).toBeCloseTo((60 * 3 + 300) / 4)
    expect(weightedMedianDelay([c])).toBeNull()
  })
})
