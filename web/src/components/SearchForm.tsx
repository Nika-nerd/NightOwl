import { useState } from 'react'

const MIN_LENGTH = 3
const MAX_LENGTH = 500

interface Props {
  loading: boolean
  onSubmit: (question: string) => void
  onCancel: () => void
}

export function SearchForm({ loading, onSubmit, onCancel }: Props) {
  const [value, setValue] = useState('')
  const trimmed = value.trim()
  const valid = trimmed.length >= MIN_LENGTH && trimmed.length <= MAX_LENGTH

  return (
    <form
      className="search"
      onSubmit={(event) => {
        event.preventDefault()
        if (valid && !loading) onSubmit(trimmed)
      }}
    >
      <input
        value={value}
        onChange={(event) => setValue(event.target.value)}
        maxLength={MAX_LENGTH}
        placeholder="Например: сколько случаев кори было в Европе в 2025 году?"
        aria-label="Ваш вопрос"
        disabled={loading}
        autoFocus
      />
      {loading ? (
        <button type="button" className="secondary" onClick={onCancel}>
          Остановить
        </button>
      ) : (
        <button type="submit" disabled={!valid}>
          Найти
        </button>
      )}
    </form>
  )
}