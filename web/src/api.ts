import type { AnswerDto, PipelineProgress } from './types'

export interface AskHandlers {
  onProgress: (progress: PipelineProgress) => void
  onResult: (answer: AnswerDto) => void
  onError: (message: string) => void
}

function parse<T>(event: Event): T | null {
  try {
    return JSON.parse((event as MessageEvent<string>).data) as T
  } catch {
    return null
  }
}

/** Открывает поток событий. Возвращает функцию отмены. */
export function askStream(question: string, handlers: AskHandlers): () => void {
  const source = new EventSource(`/api/ask/stream?q=${encodeURIComponent(question)}`)
  let finished = false

  // EventSource сам переподключается после закрытия потока сервером,
  // а это означало бы повторный вопрос. Поэтому закрываем явно.
  const finish = () => {
    finished = true
    source.close()
  }

  source.addEventListener('progress', (event) => {
    const data = parse<PipelineProgress>(event)
    if (data) handlers.onProgress(data)
  })

  source.addEventListener('result', (event) => {
    finish()
    const data = parse<AnswerDto>(event)
    if (data) handlers.onResult(data)
    else handlers.onError('Не удалось прочитать ответ сервера.')
  })

  source.addEventListener('failure', (event) => {
    finish()
    const data = parse<{ message?: string }>(event)
    handlers.onError(data?.message ?? 'Ошибка сервера.')
  })

  source.onerror = () => {
    if (finished) return
    finish()
    handlers.onError('Не удалось получить ответ: сервер недоступен или превышен лимит запросов (5 в минуту).')
  }

  return () => {
    finished = true
    source.close()
  }
}