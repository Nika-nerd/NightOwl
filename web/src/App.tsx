import { AnswerView } from './components/AnswerView'
import { ProgressSteps } from './components/ProgressSteps'
import { SearchForm } from './components/SearchForm'
import { useAsk } from './useAsk'

export default function App() {
  const { state, ask, stop } = useAsk()

  return (
    <div className="app">
      <header>
        <h1>🦉 NightOwl</h1>
        <p>Достоверная информация из интернета: официальные источники, СМИ, сверка и процент уверенности</p>
      </header>

      <SearchForm loading={state.status === 'loading'} onSubmit={ask} onCancel={stop} />

      {state.status === 'loading' && <ProgressSteps stage={state.stage} message={state.message} />}
      {state.status === 'error' && <section className="card error">{state.message}</section>}
      {state.status === 'done' && <AnswerView answer={state.answer} />}
    </div>
  )
}