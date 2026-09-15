import { describe, expect, it } from 'vitest'
import { highlight, sharedShare, sharedWords, tokens } from './text'

describe('tokens', () => {
  it('lower-cases and drops words shorter than four characters', () => {
    expect(tokens('Київщина: БпЛА курсом на Обухів!')).toEqual(['київщина', 'бпла', 'курсом', 'обухів'])
    expect(tokens('ППО у роботі 2026')).toEqual(['роботі', '2026'])
    expect(tokens(null)).toEqual([])
    expect(tokens('')).toEqual([])
  })
})

describe('sharedWords', () => {
  it('is the intersection of the two token sets, case-insensitive', () => {
    const shared = sharedWords('Ракета з Чорного моря курсом на Одесу', 'ОДЕСА: ракета з акваторії Чорного моря')
    expect([...shared].sort()).toEqual(['моря', 'ракета', 'чорного'])
  })
  it('is empty when either text is missing', () => {
    expect(sharedWords(undefined, 'текст тут')).toEqual(new Set())
  })
})

describe('highlight', () => {
  it('keeps separators and marks shared words, merging neighbours of the same kind', () => {
    const shared = new Set(['ракета', 'моря'])
    expect(highlight('Ракета з моря, курсом', shared)).toEqual([
      { text: 'Ракета', shared: true },
      { text: ' з ', shared: false },
      { text: 'моря', shared: true },
      { text: ', курсом', shared: false },
    ])
  })
  it('matches whole tokens only and keeps the separator between two hits', () => {
    expect(highlight('на на', new Set(['на']))).toEqual([
      { text: 'на', shared: true },
      { text: ' ', shared: false },
      { text: 'на', shared: true },
    ])
    expect(highlight('Одеса Одеську', new Set(['одеса']))).toEqual([
      { text: 'Одеса', shared: true },
      { text: ' Одеську', shared: false },
    ])
    expect(highlight('', new Set(['x']))).toEqual([])
  })
  it('round-trips the text', () => {
    const text = 'Увага! БпЛА (шахед) курсом на Київ — 12:40.'
    const joined = highlight(text, sharedWords(text, 'шахед на київ'))
      .map((s) => s.text)
      .join('')
    expect(joined).toBe(text)
  })
})

describe('sharedShare', () => {
  it('is the fraction of tokens that are shared', () => {
    const shared = new Set(['ракета', 'моря'])
    expect(sharedShare('Ракета з моря курсом на Одесу', shared)).toBeCloseTo(2 / 4)
    expect(sharedShare('на', shared)).toBeNull()
  })
})
