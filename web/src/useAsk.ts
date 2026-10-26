import { useCallback, useEffect, useRef, useState } from 'react'
import { askStream } from './api'
import type { AnswerDto, PipelineStage } from './types'

export type AskState =
  | { status: 'idle' }
  | { status: 'loading'; stage: PipelineStage | null; message: string }
  | { status: 'done'; answer: AnswerDto }
  | { status: 'error'; message: string }

export function useAsk() {
  const [state, setState] = useState<AskState>({ status: 'idle' })
  const cancelRef = useRef<(() => void) | null>(null)

  const cancel = useCallback(() => {
    cancelRef.current?.()
    cancelRef.current = null
  }, [])

  const ask = useCallback(
    (question: string) => {
      cancel()
      setState({ status: 'loading', stage: null, message: 'Отправляю вопрос...' })

      cancelRef.current = askStream(question, {
        onProgress: (p) => setState({ status: 'loading', stage: p.stage, message: p.message }),
        onResult: (answer) => setState({ status: 'done', answer }),
        onError: (message) => setState({ status: 'error', message }),
      })
    },
    [cancel],
  )

  const stop = useCallback(() => {
    cancel()
    setState({ status: 'idle' })
  }, [cancel])

  useEffect(() => cancel, [cancel]) // отмена при закрытии страницы

  return { state, ask, stop }
}