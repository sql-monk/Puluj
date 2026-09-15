/** A 12–200 point trend in the de-emphasis grey, with the last point marked. No axes: the table beside it carries the numbers. */
export default function Sparkline({ values, color, width = 110, height = 22 }: { values: number[]; color: string; width?: number; height?: number }) {
  const n = values.length
  if (n === 0) return null
  const max = Math.max(1, ...values)
  const x = (i: number) => (n > 1 ? (i * (width - 4)) / (n - 1) + 2 : width / 2)
  const y = (v: number) => height - 2 - ((height - 4) * v) / max
  const d = values.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ')
  const last = n - 1
  return (
    <svg width={width} height={height} aria-hidden className="shrink-0">
      <path d={d} fill="none" stroke={color} strokeWidth={1.5} strokeLinejoin="round" strokeLinecap="round" />
      <circle cx={x(last)} cy={y(values[last])} r={2.5} fill={color} />
    </svg>
  )
}
