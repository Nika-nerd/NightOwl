import type { PipelineStage } from '../types'

const STEPS: { stage: PipelineStage; label: string }[] = [
  { stage: 'Planning', label: 'Планирую поиск' },
  { stage: 'Searching', label: 'Ищу источники' },
  { stage: 'Downloading', label: 'Читаю страницы' },
  { stage: 'ExtractingClaims', label: 'Извлекаю факты' },
  { stage: 'Verifying', label: 'Сверяю источники' },
  { stage: 'Answering', label: 'Формулирую ответ' },
]

function activeIndex(stage: PipelineStage | null): number {
  if (stage === null) return -1
  if (stage === 'Filtering') return 1 // фильтр относится к этапу поиска
  return STEPS.findIndex((step) => step.stage === stage)
}

interface Props {
  stage: PipelineStage | null
  message: string
}

export function ProgressSteps({ stage, message }: Props) {
  const active = activeIndex(stage)

  return (
    <section className="card" aria-live="polite">
      <ol className="steps">
        {STEPS.map((step, index) => {
          const state = index < active ? 'done' : index === active ? 'active' : 'pending'
          return (
            <li key={step.stage} className={`step ${state}`}>
              <span className="dot" />
              {step.label}
            </li>
          )
        })}
      </ol>
      <p className="muted">{message}</p>
    </section>
  )
}