import type { LiveChannel, LiveChannelTransport, LiveEventHandler, LiveOptions } from '@inertiajs/core'

/**
 * The parts of a `@microsoft/signalr` `HubConnection` the transport uses. Kept structural, so this package does not
 * depend on a SignalR client version.
 */
export interface SignalRConnection {
  /** `HubConnectionState`: `Disconnected`, `Connecting`, `Connected`, `Disconnecting` or `Reconnecting`. */
  readonly state: string
  readonly connectionId: string | null
  start(): Promise<void>
  invoke(methodName: string, ...args: any[]): Promise<any>
  on(methodName: string, newMethod: (...args: any[]) => any): void
  onclose(callback: (error?: Error) => void): void
  onreconnecting(callback: (error?: Error) => void): void
  onreconnected(callback: (connectionId?: string) => void): void
}

export type SignalROptions = {
  /** The hub connection, or a factory called once, the first time the transport needs it. */
  connection: SignalRConnection | (() => SignalRConnection)

  /** Start the connection when a channel is needed and it is disconnected. Defaults to `true`. */
  start?: boolean

  /** The client method the server sends live events to. Defaults to `inertia`, as `Ponango.Inertia.SignalR` does. */
  method?: string
} & Omit<LiveOptions, 'transport'>

/** The hub contract of `Ponango.Inertia.SignalR` (`InertiaLiveProtocol`). Change both packages together. */
export const protocol = {
  clientMethod: 'inertia',
  subscribeMethod: 'Subscribe',
  unsubscribeMethod: 'Unsubscribe',
} as const

/**
 * A retry policy for `withAutomaticReconnect` that never gives up: after 0, 2, 5 and 10 seconds, then every 30
 * seconds. SignalR's default stops after four attempts (about 42 seconds), which a laptop waking from sleep or a long
 * network change outlasts, and then the connection stays closed until something starts it again.
 *
 * ```js
 * new HubConnectionBuilder().withUrl('/inertia/live').withAutomaticReconnect(keepRetrying).build()
 * ```
 */
export const keepRetrying = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }: { previousRetryCount: number }): number =>
    [0, 2000, 5000, 10000][previousRetryCount] ?? 30000,
}

/** How often the transport checks whether a connection it did not start has come up. */
const POLL_INTERVAL = 250

/** How long the transport waits before starting the connection again after a failed start. */
const RETRY_DELAY = 5000

type Entry = {
  key: string
  channel: LiveChannel
  handlers: Set<LiveEventHandler>
  joining: Promise<unknown> | null
  // The connection epoch the last Subscribe was sent in. A drop or close bumps the epoch: the groups are gone then
  joinedIn: number
}

/** The channel's identity and SignalR group: `{type}:{name}`. */
export const channelKey = (channel: LiveChannel): string => `${channel.type}:${channel.name}`

/**
 * The live props transport for `Ponango.Inertia.SignalR`:
 *
 * ```js
 * const connection = new HubConnectionBuilder().withUrl('/inertia/live').withAutomaticReconnect(keepRetrying).build()
 * createInertiaApp({ live: signalR({ connection }), ... })
 * ```
 *
 * Each channel a page's live props list is joined through the hub's `Subscribe(type, name)` and left with
 * `Unsubscribe(type, name)` once no prop listens to it. Events arrive on the `inertia` client method with the
 * channel key, and Inertia reads the event name from the payload. After a reconnect, SignalR has dropped the
 * connection's groups, so every channel is joined again before Inertia is told the connection is back. The same
 * happens when a closed connection is started again, by the app or by the transport for a new channel.
 */
export const signalR = ({
  connection,
  start = true,
  method = protocol.clientMethod,
  ...options
}: SignalROptions): LiveOptions => {
  const channels = new Map<string, Entry>()
  let resolved: SignalRConnection | null = null
  let starting: Promise<void> | null = null
  let statusCallback: ((connected: boolean) => void) | null = null
  let connected = false
  // Between onreconnecting and the end of the rejoin, so "connected" is only reported once every channel is back
  let reconnecting = false
  // Bumped by every connection change, so a rejoin that a later drop overtook does not report the connection back
  let generation = 0
  // Bumped when the connection's groups are lost (a drop or a close), so each channel is subscribed again after it
  let epoch = 0
  // Closed for good (onclose), so whatever starts the connection again, every channel must be rejoined first
  let closed = false

  const report = (value: boolean): void => {
    connected = value
    statusCallback?.(value)
  }

  const markConnected = (): void => {
    if (closed) {
      closed = false
      rejoin()
      return
    }

    if (!connected && !reconnecting) {
      report(true)
    }
  }

  const isActive = (entry: Entry): boolean => channels.get(entry.key) === entry

  /**
   * Resolved once and lazily: the factory may read the document, and handlers must be registered exactly once.
   */
  const hub = (): SignalRConnection => {
    if (resolved) {
      return resolved
    }

    const instance = typeof connection === 'function' ? connection() : connection
    resolved = instance

    instance.on(method, (key: unknown, payload: unknown) => {
      if (typeof key !== 'string') {
        return
      }

      // Copied, so a handler that unsubscribes while being called does not skip its neighbours
      const handlers = channels.get(key)?.handlers

      handlers && [...handlers].forEach((handler) => handler(payload))
    })

    instance.onreconnecting(() => {
      generation++
      epoch++
      reconnecting = true
      report(false)
    })
    instance.onclose(() => {
      generation++
      epoch++
      reconnecting = false
      closed = true
      report(false)
      watchForRestart()
    })
    instance.onreconnected(() => rejoin())

    return instance
  }

  /** Joins every channel again, then tells Inertia the connection is back, so it refreshes the live props. */
  const rejoin = (): void => {
    const current = ++generation
    const instance = hub()
    reconnecting = true

    void Promise.all([...channels.values()].map((entry) => join(entry))).then(() => {
      // The connection dropped again while rejoining: the next reconnect rejoins and reports
      if (current !== generation || instance.state !== 'Connected') {
        return
      }

      reconnecting = false
      report(true)
    })
  }

  /**
   * After a close, notices the app starting the connection again, which fires no SignalR callback. A channel
   * subscribed in the meantime notices it in its own join, so this stops once no channel is left.
   */
  const watchForRestart = (): void => {
    const check = (): void => {
      if (!closed || channels.size === 0) {
        return
      }

      if (hub().state === 'Connected') {
        markConnected()
        return
      }

      setTimeout(check, POLL_INTERVAL)
    }

    setTimeout(check, POLL_INTERVAL)
  }

  const startConnection = (): Promise<void> => {
    starting ??= hub()
      .start()
      .then(() => markConnected())
      .finally(() => {
        starting = null
      })

    return starting
  }

  /** Resolves `true` once connected, or `false` when the channel was released in the meantime. */
  const whenConnected = (entry: Entry): Promise<boolean> =>
    new Promise((resolve) => {
      const check = (): void => {
        if (!isActive(entry)) {
          resolve(false)
          return
        }

        const state = hub().state

        if (state === 'Connected') {
          markConnected()
          resolve(true)
          return
        }

        if (state === 'Disconnected' && start) {
          startConnection().then(check, () => setTimeout(check, RETRY_DELAY))
          return
        }

        setTimeout(check, POLL_INTERVAL)
      }

      check()
    })

  const join = async (entry: Entry): Promise<void> => {
    let joining: Promise<unknown> | null = null

    try {
      // Checked again after the await: the channel may have been released while the continuation was queued
      if (!(await whenConnected(entry)) || !isActive(entry)) {
        return
      }

      // Already sent on this connection (a rejoin and the channel's own first join can meet here). The join that
      // sent it reports a refusal, so this one waits quietly.
      if (entry.joinedIn === epoch) {
        await entry.joining?.catch(() => {})
        return
      }

      joining = hub().invoke(protocol.subscribeMethod, entry.channel.type, entry.channel.name)
      entry.joining = joining
      entry.joinedIn = epoch
      await joining
    } catch (error) {
      if (isActive(entry)) {
        // A refused channel leaves its props quietly stale, so it is worth a warning
        console.warn(`Inertia could not subscribe to the live channel "${entry.channel.name}".`, error)
      }
    } finally {
      // Only its own: another join of the same channel may be in flight
      if (joining && entry.joining === joining) {
        entry.joining = null
      }
    }
  }

  const leave = async (entry: Entry): Promise<void> => {
    if (entry.joining) {
      try {
        await entry.joining
      } catch {
        return
      }
    }

    // A later subscription to the same channel owns the group now. Checked right before the call: invocations
    // reach the hub in order, so an Unsubscribe sent here can never land after that subscription's Subscribe.
    if (channels.has(entry.key) || hub().state !== 'Connected') {
      return
    }

    try {
      await hub().invoke(protocol.unsubscribeMethod, entry.channel.type, entry.channel.name)
    } catch {
      // Leaving is best effort: the group goes away with the connection anyway
    }
  }

  const transport: LiveChannelTransport = {
    subscribeToChannel(channel, handler) {
      const key = channelKey(channel)
      let entry = channels.get(key)

      // Wrapped, so the same handler subscribed twice is still released twice
      const listener: LiveEventHandler = (payload) => handler(payload)

      if (!entry) {
        entry = { key, channel, handlers: new Set(), joining: null, joinedIn: -1 }
        channels.set(key, entry)
        void join(entry)
      }

      entry.handlers.add(listener)

      const owner = entry

      return () => {
        owner.handlers.delete(listener)

        if (owner.handlers.size === 0 && isActive(owner)) {
          channels.delete(key)
          void leave(owner)
        }
      }
    },

    socketId: () => hub().connectionId ?? null,

    onStatusChange(callback) {
      statusCallback = callback

      if (hub().state === 'Connected') {
        markConnected()
      }

      return () => {
        if (statusCallback === callback) {
          statusCallback = null
        }
      }
    },
  }

  return { transport, ...options }
}
