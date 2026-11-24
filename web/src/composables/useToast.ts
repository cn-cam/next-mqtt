import { reactive } from 'vue'

export interface Toast {
  id: number
  message: string
  tone: 'success' | 'error' | 'info'
}

const state = reactive<{ items: Toast[] }>({ items: [] })
let nextId = 1

function push(message: string, tone: Toast['tone'], timeout = 4000): void {
  const id = nextId++
  state.items.push({ id, message, tone })

  window.setTimeout(() => {
    dismiss(id)
  }, timeout)
}

export function dismiss(id: number): void {
  const index = state.items.findIndex((item) => item.id === id)
  if (index >= 0) state.items.splice(index, 1)
}

export function useToast() {
  return {
    items: state.items,
    success: (message: string) => push(message, 'success'),
    error: (message: string) => push(message, 'error', 6000),
    info: (message: string) => push(message, 'info'),
    dismiss,
  }
}
