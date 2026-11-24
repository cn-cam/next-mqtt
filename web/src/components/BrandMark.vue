<script setup lang="ts">
withDefaults(defineProps<{ size?: number }>(), { size: 32 })
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 64 64"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    aria-hidden="true"
  >
    <defs>
      <linearGradient id="brand-gradient" x1="8" y1="8" x2="56" y2="56" gradientUnits="userSpaceOnUse">
        <stop stop-color="#22d3ee" />
        <stop offset="1" stop-color="#34d399" />
      </linearGradient>
    </defs>
    <path
      d="M32 6 55 19v26L32 58 9 45V19z"
      stroke="url(#brand-gradient)"
      stroke-width="3.2"
      fill="rgba(34,211,238,0.06)"
    />
    <path
      d="M21 40V26l11 7 11-7v14"
      stroke="url(#brand-gradient)"
      stroke-width="3.6"
      stroke-linecap="round"
      stroke-linejoin="round"
    />
    <circle cx="32" cy="22" r="3.2" fill="#34d399" />
  </svg>
</template>
