import type { LiveChannel, LiveChannelTransport } from '@inertiajs/core'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { channelKey, protocol, signalR, type SignalRConnection } from '../src/index'

type Deferred = { resolve: (value?: unknown) => void; reject: (error: unknown) => void }

/** A hub connection whose state, invocations and callbacks the tests drive by hand. */
class FakeConnection implements SignalRConnection {
  state = 'Disconnected'
  connectionId: string | null = null
  invocations: { method: string; args: unknown[]; deferred: Deferred }[] = []
  starts = 0
  autoResolve = true
  failStart = false
  private handlers = new Map<string, ((...args: any[]) => any)[]>()
  private closeCallbacks: ((error?: Error) => void)[] = []
  private reconnectingCallbacks: ((error?: Error) => void)[] = []
  private reconnectedCallbacks: ((id?: string) => void)[] = []

  start(): Promise<void> {
    this.starts++
    if (this.failStart) {
      return Promise.reject(new Error('down'))
    }
    this.state = 'Connected'
    this.connectionId = `conn-${this.starts}`
    return Promise.resolve()
  }

  invoke(method: string, ...args: unknown[]): Promise<unknown> {
    let deferred!: Deferred
    const promise = new Promise((resolve, reject) => (deferred = { resolve, reject }))
    this.invocations.push({ method, args, deferred })
    if (this.autoResolve) {
      deferred.resolve()
    }
    return promise
  }

  on(method: string, handler: (...args: any[]) => any): void {
    this.handlers.set(method, [...(this.handlers.get(method) ?? []), handler])
  }

  onclose(callback: (error?: Error) => void): void {
    this.closeCallbacks.push(callback)
  }

  onreconnecting(callback: (error?: Error) => void): void {
    this.reconnectingCallbacks.push(callback)
  }

  onreconnected(callback: (id?: string) => void): void {
    this.reconnectedCallbacks.push(callback)
  }

  emit(method: string, ...args: unknown[]): void {
    this.handlers.get(method)?.forEach((handler) => handler(...args))
  }

  handlerCount(method: string): number {
    return this.handlers.get(method)?.length ?? 0
  }

  dropAndReconnect(): void {
    this.state = 'Reconnecting'
    this.reconnectingCallbacks.forEach((callback) => callback())
    this.state = 'Connected'
    this.connectionId = 'conn-reconnected'
    this.reconnectedCallbacks.forEach((callback) => callback(this.connectionId!))
  }

  close(): void {
    this.state = 'Disconnected'
    this.closeCallbacks.forEach((callback) => callback())
  }

  calls(method: string): unknown[][] {
    return this.invocations.filter((invocation) => invocation.method === method).map((invocation) => invocation.args)
  }
}

const orders: LiveChannel = { name: 'orders.1', type: 'private' }
const dashboard: LiveChannel = { name: 'dashboard', type: 'public' }

const flush = () => vi.advanceTimersByTimeAsync(0)

let connection: FakeConnection
let transport: LiveChannelTransport

beforeEach(() => {
  vi.useFakeTimers()
  connection = new FakeConnection()
  transport = signalR({ connection }).transport as LiveChannelTransport
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('subscribing', () => {
  it('starts the connection and joins the channel through the hub', async () => {
    transport.subscribeToChannel(orders, () => {})
    await flush()

    expect(connection.starts).toBe(1)
    expect(connection.calls('Subscribe')).toEqual([['private', 'orders.1']])
  })

  it('joins a channel once however many handlers listen to it', async () => {
    transport.subscribeToChannel(orders, () => {})
    transport.subscribeToChannel(orders, () => {})
    await flush()

    expect(connection.calls('Subscribe')).toHaveLength(1)
  })

  it('leaves only when the last handler is released', async () => {
    const first = transport.subscribeToChannel(orders, () => {})
    const second = transport.subscribeToChannel(orders, () => {})
    await flush()

    first()
    await flush()
    expect(connection.calls('Unsubscribe')).toHaveLength(0)

    second()
    await flush()
    expect(connection.calls('Unsubscribe')).toEqual([['private', 'orders.1']])
  })

  it('releasing the same unsubscribe function twice is harmless', async () => {
    const release = transport.subscribeToChannel(orders, () => {})
    await flush()

    release()
    release()
    await flush()

    expect(connection.calls('Unsubscribe')).toHaveLength(1)
  })

  it('leaves after a join that was still in flight settles', async () => {
    connection.autoResolve = false
    const release = transport.subscribeToChannel(orders, () => {})
    await flush()

    release()
    await flush()
    expect(connection.calls('Unsubscribe')).toHaveLength(0)

    connection.invocations[0].deferred.resolve()
    await flush()
    expect(connection.calls('Unsubscribe')).toEqual([['private', 'orders.1']])
  })

  it('does not leave a channel that was subscribed again before the leave ran', async () => {
    connection.autoResolve = false
    const release = transport.subscribeToChannel(orders, () => {})
    await flush()
    release()
    transport.subscribeToChannel(orders, () => {})
    await flush()

    connection.invocations.forEach((invocation) => invocation.deferred.resolve())
    await flush()

    expect(connection.calls('Unsubscribe')).toHaveLength(0)
    expect(connection.calls('Subscribe')).toHaveLength(2)
  })

  it('warns, without throwing, when the hub refuses a channel', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    connection.autoResolve = false
    transport.subscribeToChannel(orders, () => {})
    await flush()

    connection.invocations[0].deferred.reject(new Error('Unauthorized'))
    await flush()

    expect(warn).toHaveBeenCalledWith('Inertia could not subscribe to the live channel "orders.1".', expect.any(Error))
  })

  it('does not warn about a refusal for a channel already released', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    connection.autoResolve = false
    const release = transport.subscribeToChannel(orders, () => {})
    await flush()
    release()

    connection.invocations[0].deferred.reject(new Error('Unauthorized'))
    await flush()

    expect(warn).not.toHaveBeenCalled()
  })
})

describe('connection state', () => {
  it('waits for a connection the app is starting itself', async () => {
    transport = signalR({ connection, start: false }).transport as LiveChannelTransport
    connection.state = 'Connecting'
    transport.subscribeToChannel(orders, () => {})
    await flush()
    expect(connection.calls('Subscribe')).toHaveLength(0)

    connection.state = 'Connected'
    await vi.advanceTimersByTimeAsync(300)

    expect(connection.starts).toBe(0)
    expect(connection.calls('Subscribe')).toHaveLength(1)
  })

  it('stops waiting for a channel released before the connection came up', async () => {
    transport = signalR({ connection, start: false }).transport as LiveChannelTransport
    const release = transport.subscribeToChannel(orders, () => {})
    release()

    connection.state = 'Connected'
    await vi.advanceTimersByTimeAsync(1000)

    expect(connection.calls('Subscribe')).toHaveLength(0)
  })

  it('retries a failed start after a delay', async () => {
    connection.failStart = true
    transport.subscribeToChannel(orders, () => {})
    await flush()
    expect(connection.starts).toBe(1)

    connection.failStart = false
    await vi.advanceTimersByTimeAsync(5000)

    expect(connection.starts).toBe(2)
    expect(connection.calls('Subscribe')).toHaveLength(1)
  })

  it('starts the connection once for several channels', async () => {
    transport.subscribeToChannel(orders, () => {})
    transport.subscribeToChannel(dashboard, () => {})
    await flush()

    expect(connection.starts).toBe(1)
    expect(connection.calls('Subscribe')).toHaveLength(2)
    expect(connection.calls('Subscribe')).toEqual(
      expect.arrayContaining([
        ['private', 'orders.1'],
        ['public', 'dashboard'],
      ]),
    )
  })

  it('reports the first connection, so Inertia can tell a later reconnect', async () => {
    const statuses: boolean[] = []
    transport.onStatusChange!((connected) => statuses.push(connected))

    transport.subscribeToChannel(orders, () => {})
    await flush()

    expect(statuses).toEqual([true])
  })

  it('reports an already connected connection when Inertia registers', () => {
    connection.state = 'Connected'
    const statuses: boolean[] = []

    transport.onStatusChange!((connected) => statuses.push(connected))

    expect(statuses).toEqual([true])
  })

  it('rejoins every channel after a reconnect before reporting it', async () => {
    const statuses: boolean[] = []
    transport.onStatusChange!((connected) => statuses.push(connected))
    transport.subscribeToChannel(orders, () => {})
    transport.subscribeToChannel(dashboard, () => {})
    await flush()

    connection.autoResolve = false
    connection.dropAndReconnect()
    await flush()

    expect(statuses).toEqual([true, false])
    expect(connection.calls('Subscribe')).toHaveLength(4)

    connection.invocations.slice(2).forEach((invocation) => invocation.deferred.resolve())
    await flush()

    expect(statuses).toEqual([true, false, true])
  })

  it('reports a closed connection', async () => {
    const statuses: boolean[] = []
    transport.onStatusChange!((connected) => statuses.push(connected))
    transport.subscribeToChannel(orders, () => {})
    await flush()

    connection.close()

    expect(statuses).toEqual([true, false])
  })

  it('stops reporting once Inertia unregisters', async () => {
    const statuses: boolean[] = []
    const off = transport.onStatusChange!((connected) => statuses.push(connected))
    off()

    transport.subscribeToChannel(orders, () => {})
    await flush()

    expect(statuses).toEqual([])
  })
})

describe('events', () => {
  it('routes each event to the handlers of its channel only', async () => {
    const ordersPayloads: unknown[] = []
    const dashboardPayloads: unknown[] = []
    transport.subscribeToChannel(orders, (payload) => ordersPayloads.push(payload))
    transport.subscribeToChannel(dashboard, (payload) => dashboardPayloads.push(payload))
    await flush()

    const payload = { __inertia: { event: 'order.updated', props: { order: { id: 1 } } } }
    connection.emit('inertia', 'private:orders.1', payload)

    expect(ordersPayloads).toEqual([payload])
    expect(dashboardPayloads).toEqual([])
  })

  it('delivers to every handler of a channel, even one that unsubscribes while called', async () => {
    const received: string[] = []
    let releaseFirst = () => {}
    releaseFirst = transport.subscribeToChannel(orders, () => {
      received.push('first')
      releaseFirst()
    })
    transport.subscribeToChannel(orders, () => received.push('second'))
    await flush()

    connection.emit('inertia', 'private:orders.1', {})

    expect(received).toEqual(['first', 'second'])
  })

  it('ignores events for released channels and malformed messages', async () => {
    const received: unknown[] = []
    const release = transport.subscribeToChannel(orders, (payload) => received.push(payload))
    await flush()
    release()

    connection.emit('inertia', 'private:orders.1', {})
    connection.emit('inertia', 42, {})

    expect(received).toEqual([])
  })

  it('listens on a custom client method', async () => {
    transport = signalR({ connection, method: 'live' }).transport as LiveChannelTransport
    const received: unknown[] = []
    transport.subscribeToChannel(orders, (payload) => received.push(payload))
    await flush()

    connection.emit('inertia', 'private:orders.1', {})
    connection.emit('live', 'private:orders.1', { ok: true })

    expect(received).toEqual([{ ok: true }])
  })
})

describe('options', () => {
  it('reads the socket id from the current connection id', async () => {
    expect(transport.socketId!()).toBeNull()

    transport.subscribeToChannel(orders, () => {})
    await flush()
    expect(transport.socketId!()).toBe('conn-1')

    connection.dropAndReconnect()
    expect(transport.socketId!()).toBe('conn-reconnected')
  })

  it('resolves a connection factory once, lazily', async () => {
    const factory = vi.fn(() => connection)
    const options = signalR({ connection: factory })
    expect(factory).not.toHaveBeenCalled()

    const lazy = options.transport as LiveChannelTransport
    lazy.subscribeToChannel(orders, () => {})
    lazy.subscribeToChannel(dashboard, () => {})
    lazy.socketId!()
    await flush()

    expect(factory).toHaveBeenCalledTimes(1)
    expect(connection.handlerCount('inertia')).toBe(1)
  })

  it('passes Inertia options through', () => {
    const options = signalR({ connection, throttle: 1000, pauseWhenHidden: false })

    expect(options.throttle).toBe(1000)
    expect(options.pauseWhenHidden).toBe(false)
  })

  it('uses the hub contract of Ponango.Inertia.SignalR', () => {
    expect(protocol).toEqual({ clientMethod: 'inertia', subscribeMethod: 'Subscribe', unsubscribeMethod: 'Unsubscribe' })
    expect(channelKey(orders)).toBe('private:orders.1')
  })
})
