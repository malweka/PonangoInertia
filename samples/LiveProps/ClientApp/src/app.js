import { createApp, h } from 'vue'
import { createInertiaApp } from '@inertiajs/vue3'
import { HubConnectionBuilder } from '@microsoft/signalr'
import { keepRetrying, signalR } from '@ponango/inertia-signalr'

const connection = new HubConnectionBuilder().withUrl('/inertia/live').withAutomaticReconnect(keepRetrying).build()

// Exposed for the e2e check, so it can drop the connection and watch the transport rejoin.
window.liveConnection = connection

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob('./Pages/**/*.vue', { eager: true })
    return pages[`./Pages/${name}.vue`]
  },
  setup({ el, App, props, plugin }) {
    createApp({ render: () => h(App, props) }).use(plugin).mount(el)
  },
  live: signalR({ connection, throttle: 0 }),
})
