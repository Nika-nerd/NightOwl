import type { AnswerDto } from '../types'
import { formatDate, safeUrl } from '../utils'
import { ConfidenceBadge, TierBadge } from './Badges'

// Превращает [1][2] в кликабельные метки; текст рендерится React-ом без HTML, XSS исключён
function CitedText({ text }: { text: string }) {
  const parts = text.split(/(\[\d+\])/g)
  return (
    <p className="summary">
      {parts.map((part, index) => {
        const match = /^\[(\d+)\]$/.exec(part)
        return match ? (
          <a key={index} className="cite" href={`#source-${match[1]}`}>
            {match[1]}
          </a>
        ) : (
          <span key={index}>{part}</span>
        )
      })}
    </p>
  )
}

function Refs({ numbers, label, negative = false }: { numbers: number[]; label: string; negative?: boolean }) {
  if (numbers.length === 0) return null
  return (
    <span className={`refs${negative ? ' negative' : ''}`}>
      {label}:{' '}
      {numbers.map((n) => (
        <a key={n} className="cite" href={`#source-${n}`}>
          {n}
        </a>
      ))}
    </span>
  )
}

export function AnswerView({ answer }: { answer: AnswerDto }) {
  const empty = answer.claims.length === 0

  return (
    <>
      <section className="card answer">
        <div className="answer-head">
          {!empty && <ConfidenceBadge percent={answer.overallConfidencePercent} large />}
          <div>
            <h2>Ответ</h2>
            {answer.fromCache && <span className="chip">из кэша</span>}
          </div>
        </div>
        <CitedText text={answer.summary} />
      </section>

      {answer.warnings.length > 0 && (
        <section className="card warnings">
          {answer.warnings.map((warning) => (
            <p key={warning}>⚠ {warning}</p>
          ))}
        </section>
      )}

      {!empty && (
        <section className="card">
          <h2>Проверенные факты</h2>
          <ul className="claims">
            {answer.claims.map((claim) => (
              <li key={claim.text}>
                <ConfidenceBadge percent={claim.confidencePercent} />
                <div>
                  <p>{claim.text}</p>
                  <Refs numbers={claim.supporting} label="Подтверждают" />
                  <Refs numbers={claim.contradicting} label="Противоречат" negative />
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {answer.sources.length > 0 && (
        <section className="card">
          <h2>Источники</h2>
          <ol className="sources">
            {answer.sources.map((source) => {
              const href = safeUrl(source.url)
              return (
                <li key={source.number} id={`source-${source.number}`}>
                  <span className="source-number">{source.number}</span>
                  <div>
                    <TierBadge tier={source.tier} />{' '}
                    {href ? (
                      <a href={href} target="_blank" rel="noopener noreferrer">
                        {source.title || source.domain}
                      </a>
                    ) : (
                      <span>{source.title || source.domain}</span>
                    )}
                    <p className="muted">
                      {source.domain} · {formatDate(source.publishedAt)}
                    </p>
                  </div>
                </li>
              )
            })}
          </ol>
        </section>
      )}

      <details className="card muted">
        <summary>Как это найдено</summary>
        <p>Язык вопроса: {answer.language}. Нужна свежесть данных: {answer.needsFreshness ? 'да' : 'нет'}.</p>
        <p>Поисковые запросы: {answer.queries.join(' · ')}</p>
      </details>

      <p className="disclaimer">
        Процент уверенности это эвристическая оценка по уровню источников, согласованности, свежести и разнообразию.
        Это не гарантия истинности.
      </p>
    </>
  )
}