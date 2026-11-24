import { locale, t } from '../i18n'

export function formatNumber(value: number): string {
  return new Intl.NumberFormat(locale.value === 'zh' ? 'zh-CN' : 'en-US').format(value)
}

export function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return '0 B'

  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const exponent = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  const amount = value / 1024 ** exponent
  return `${amount.toFixed(amount >= 10 || exponent === 0 ? 0 : 1)} ${units[exponent]}`
}

export function formatDate(value?: string): string {
  if (!value) return t('common.never')

  return new Date(value).toLocaleString(locale.value === 'zh' ? 'zh-CN' : 'en-GB', {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

export function formatRelative(value?: string): string {
  if (!value) return t('common.never')

  const seconds = Math.round((Date.now() - new Date(value).getTime()) / 1000)

  if (seconds < 10) return t('common.justNow')
  if (seconds < 60) return t('common.secondsAgo', { n: seconds })
  if (seconds < 3600) return t('common.minutesAgo', { n: Math.floor(seconds / 60) })
  if (seconds < 86_400) return t('common.hoursAgo', { n: Math.floor(seconds / 3600) })
  return t('common.daysAgo', { n: Math.floor(seconds / 86_400) })
}

export function formatQos(qos: string): string {
  switch (qos) {
    case 'AtMostOnce':
      return '0'
    case 'AtLeastOnce':
      return '1'
    case 'ExactlyOnce':
      return '2'
    default:
      return qos
  }
}

export function formatRetainHandling(value: string): string {
  switch (value) {
    case 'SendAtSubscribe':
      return '0'
    case 'SendAtSubscribeIfNew':
      return '1'
    case 'DoNotSend':
      return '2'
    default:
      return value
  }
}

export function formatExpiry(seconds: number): string {
  if (seconds === 0) return t('common.never')
  if (seconds === 0xffffffff) return '∞'
  if (seconds < 60) return `${seconds}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`
  if (seconds < 86_400) return `${Math.round(seconds / 3600)}h`
  return `${Math.round(seconds / 86_400)}d`
}

export function percent(value: number, total: number): number {
  return total <= 0 ? 0 : Math.round((value / total) * 100)
}
