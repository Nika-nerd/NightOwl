// Ссылки приходят из интернета: разрешаем только http(s), чтобы исключить javascript: и подобное
export function safeUrl(url: string): string | null {
  try {
    const parsed = new URL(url)
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.toString() : null
  } catch {
    return null
  }
}

export function formatDate(iso: string | null): string {
  if (!iso) return 'дата неизвестна'
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? 'дата неизвестна' : date.toLocaleDateString('ru-RU')
}