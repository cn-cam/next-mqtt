import { onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * Runs an async loader immediately, then on an interval while the tab is
 * visible. Overlapping executions are skipped.
 */
export function useAutoRefresh(load: () => Promise<void>, intervalMs = 3000) {
  const refreshing = ref(false)
  const lastUpdated = ref<Date | null>(null)
  const error = ref('')
  let timer: number | undefined

  async function refresh(): Promise<void> {
    if (refreshing.value) return
    refreshing.value = true

    try {
      await load()
      lastUpdated.value = new Date()
      error.value = ''
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : String(cause)
    } finally {
      refreshing.value = false
    }
  }

  onMounted(() => {
    void refresh()
    timer = window.setInterval(() => {
      if (document.visibilityState === 'visible') void refresh()
    }, intervalMs)
  })

  onBeforeUnmount(() => {
    if (timer !== undefined) window.clearInterval(timer)
  })

  return { refresh, refreshing, lastUpdated, error }
}
