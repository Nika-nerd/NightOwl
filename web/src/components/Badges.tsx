import type { SourceTier } from '../types'

const TIER_LABELS: Record<SourceTier, string> = {
  Official: 'Официальный',
  Media: 'СМИ',
  Reference: 'Справочный',
  Unofficial: 'Неофициальный',
}

export function TierBadge({ tier }: { tier: SourceTier }) {
  return <span className={`tier tier-${tier}`}>{TIER_LABELS[tier]}</span>
}

export function ConfidenceBadge({ percent, large = false }: { percent: number; large?: boolean }) {
  const level = percent >= 75 ? 'high' : percent >= 50 ? 'mid' : 'low'
  return <span className={`confidence ${level}${large ? ' large' : ''}`}>{percent}%</span>
}