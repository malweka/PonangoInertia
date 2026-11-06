<script setup>
import { router } from '@inertiajs/vue3'

defineProps({ order: Object, ticker: Number, user: String })

const simulate = (what) => fetch(`/simulate/${what}`, { method: 'POST' })
</script>

<template>
  <main style="font-family: system-ui, sans-serif; max-width: 32rem; margin: 2rem auto">
    <h1>Order #{{ order.id }}</h1>
    <p>Signed in as <strong>{{ user ?? 'nobody' }}</strong> (the private order channel only allows alice).</p>

    <p>Status: <strong data-test="status">{{ order.status }}</strong> (revision <span data-test="revision">{{ order.revision }}</span>)</p>
    <p>Ticker: <strong data-test="ticker">{{ ticker }}</strong></p>

    <button @click="router.post(`/orders/${order.id}/advance`)">Advance from this tab</button>
    <button @click="simulate('order')">Advance from elsewhere (reload)</button>
    <button @click="simulate('ticker')">Tick from elsewhere (value in event)</button>
  </main>
</template>
