<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    values: number[]
    height?: number
    accent?: string
  }>(),
  { height: 96, accent: '#22d3ee' },
)

const gradientId = `spark-${Math.random().toString(36).slice(2, 10)}`

const points = computed(() => {
  const values = props.values.length > 0 ? props.values : [0]
  const max = Math.max(1, ...values)
  const count = Math.max(values.length, 2)

  return values.map((value, index) => ({
    x: (index / (count - 1)) * 100,
    y: 100 - (Math.max(0, value) / max) * 92 - 4,
  }))
})

const line = computed(() =>
  points.value.map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(' '),
)

const area = computed(() => `${line.value} L100 100 L0 100 Z`)

const lastPoint = computed(() => points.value[points.value.length - 1])

const grid = [25, 50, 75]
</script>

<template>
  <svg
    class="sparkline"
    :style="{ height: `${height}px` }"
    viewBox="0 0 100 100"
    preserveAspectRatio="none"
    role="img"
    aria-hidden="true"
  >
    <defs>
      <linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" :stop-color="accent" stop-opacity="0.32" />
        <stop offset="100%" :stop-color="accent" stop-opacity="0" />
      </linearGradient>
    </defs>
    <line v-for="value in grid" :key="value" x1="0" :y1="value" x2="100" :y2="value" class="spark-grid" />
    <path :d="area" :fill="`url(#${gradientId})`" />
    <path :d="line" :stroke="accent" fill="none" stroke-width="2" vector-effect="non-scaling-stroke" stroke-linejoin="round" />
    <circle v-if="lastPoint" :cx="lastPoint.x" :cy="lastPoint.y" r="2.4" :fill="accent" vector-effect="non-scaling-stroke" />
  </svg>
</template>
