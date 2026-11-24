import { ref } from 'vue'

export type Theme = 'dark' | 'light'

const stored = localStorage.getItem('nextmqtt.theme')
export const theme = ref<Theme>(stored === 'light' ? 'light' : 'dark')

export function applyTheme(): void {
  document.documentElement.dataset.theme = theme.value
  document
    .querySelector('meta[name="theme-color"]')
    ?.setAttribute('content', theme.value === 'light' ? '#f4f7fb' : '#070b14')
}

export function setTheme(value: Theme): void {
  theme.value = value
  localStorage.setItem('nextmqtt.theme', value)
  applyTheme()
}

export function toggleTheme(): void {
  setTheme(theme.value === 'dark' ? 'light' : 'dark')
}
