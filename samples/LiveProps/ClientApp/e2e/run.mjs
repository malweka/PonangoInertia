// End-to-end check of live props over SignalR: the sample server, the real Inertia client, the
// @ponango/inertia-signalr transport, and a headless Microsoft Edge (or Chrome with BROWSER_CHANNEL=chrome).
//
//   npm run build && npm run e2e
//
// Starts the sample with `dotnet run`, so the .NET SDK must be installed.
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { chromium } from 'playwright-core'

const here = path.dirname(fileURLToPath(import.meta.url))
const project = path.resolve(here, '../../LiveProps.csproj')
const port = process.env.PORT ?? '5099'
const base = `http://127.0.0.1:${port}`
const channel = process.env.BROWSER_CHANNEL ?? 'msedge'

let server
const results = []

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

async function startServer() {
  server = spawn('dotnet', ['run', '--no-build', '--project', project, '--urls', base], {
    stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' },
  })
  server.stderr.on('data', (data) => process.stderr.write(data))

  for (let i = 0; i < 120; i++) {
    try {
      await fetch(`${base}/orders/1`)
      return
    } catch {
      await sleep(250)
    }
  }
  throw new Error('The sample server did not start')
}

async function stopServer() {
  if (!server) return
  const exited = new Promise((resolve) => server.once('exit', resolve))
  server.kill()
  await exited
  server = null
}

async function check(name, run) {
  try {
    await run()
    results.push([true, name])
    console.log(`  ok   ${name}`)
  } catch (error) {
    results.push([false, name])
    console.log(`  FAIL ${name}\n       ${error.message.split('\n')[0]}`)
  }
}

const text = (page, test) => page.textContent(`[data-test="${test}"]`)

const waitForText = (page, test, expected, timeout = 10000) =>
  page.waitForFunction(
    ([selector, value]) => document.querySelector(selector)?.textContent === value,
    [`[data-test="${test}"]`, String(expected)],
    { timeout },
  )

/** A tab signed in as `user`, recording its partial reloads and console warnings. */
async function openTab(browser, user) {
  const context = await browser.newContext()
  await context.addCookies([{ name: 'user', value: user, url: base }])
  const page = await context.newPage()
  page.reloads = []
  page.warnings = []
  page.on('request', (request) => {
    const only = request.headers()['x-inertia-partial-data']
    if (only) page.reloads.push({ only, socketId: request.headers()['x-socket-id'] })
  })
  page.on('console', (message) => {
    if (message.type() === 'warning') page.warnings.push(message.text())
  })
  await page.goto(`${base}/orders/1`)
  await page.waitForFunction(() => window.liveConnection?.state === 'Connected')
  // Let the transport join its channels.
  await sleep(300)
  return page
}

async function main() {
  await startServer()
  const browser = await chromium.launch({ channel, headless: true })

  try {
    const alice = await openTab(browser, 'alice')
    const other = await openTab(browser, 'alice')
    const bob = await openTab(browser, 'bob')

    await check('the page renders the live props', async () => {
      if ((await text(alice, 'revision')) !== '0') throw new Error('expected revision 0')
      if ((await text(alice, 'ticker')) !== '0') throw new Error('expected ticker 0')
    })

    await check('an event reloads the live prop that listens to it', async () => {
      await fetch(`${base}/simulate/order`, { method: 'POST' })
      await waitForText(alice, 'revision', 1)
      await waitForText(other, 'revision', 1)
      if (!alice.reloads.some((reload) => reload.only.split(',').includes('order')))
        throw new Error('no partial reload for order')
    })

    await check('a value sent in the event is applied without a request', async () => {
      const before = alice.reloads.length
      await fetch(`${base}/simulate/ticker`, { method: 'POST' })
      await waitForText(alice, 'ticker', 1)
      await sleep(300)
      if (alice.reloads.slice(before).some((reload) => reload.only.includes('ticker')))
        throw new Error('the ticker was reloaded instead of taken from the event')
    })

    await check('the private channel refuses a user its rule does not allow', async () => {
      if (!bob.warnings.some((warning) => warning.includes('could not subscribe to the live channel "orders.1"')))
        throw new Error(`no refusal warning (warnings: ${JSON.stringify(bob.warnings)})`)
      if ((await text(bob, 'revision')) !== '0') throw new Error('bob received the private order update')
      await waitForText(bob, 'ticker', 1)
    })

    await check('the tab that made a change sends its socket id and is skipped by the broadcast', async () => {
      const socketId = await alice.evaluate(() => window.liveConnection.connectionId)
      const posted = alice.waitForRequest((request) => request.method() === 'POST' && request.url().endsWith('/advance'))
      const before = alice.reloads.length
      await alice.click('text=Advance from this tab')
      const request = await posted
      if (request.headers()['x-socket-id'] !== socketId)
        throw new Error(`X-Socket-Id was ${request.headers()['x-socket-id']}, expected ${socketId}`)

      await waitForText(alice, 'revision', 2)   // through the redirect back
      await waitForText(other, 'revision', 2)   // through the broadcast
      await sleep(500)
      if (alice.reloads.slice(before).some((reload) => reload.only.split(',').includes('order')))
        throw new Error('the sender reloaded the prop for its own change')
    })

    await check('after the server restarts, the transport rejoins and Inertia refreshes the live props', async () => {
      await stopServer()
      await startServer()   // the in-memory order starts over at revision 0

      await waitForText(alice, 'revision', 0, 45000)
      await fetch(`${base}/simulate/order`, { method: 'POST' })
      await waitForText(alice, 'revision', 1)
    })

    // A closed connection fires no reconnect callback when it is started again; the transport must notice and rejoin.
    await check('after the app stops and starts the connection, the transport rejoins its channels', async () => {
      const before = Number(await text(alice, 'revision'))
      await alice.evaluate(async () => {
        await window.liveConnection.stop()
        await window.liveConnection.start()
      })
      // The transport polls a closed connection every 250 ms, then joins its channels again.
      await sleep(1000)

      await fetch(`${base}/simulate/order`, { method: 'POST' })
      await waitForText(alice, 'revision', before + 1)
    })
  } finally {
    await browser.close()
    await stopServer()
  }

  const failed = results.filter(([ok]) => !ok).length
  console.log(`\n${results.length - failed} passed, ${failed} failed`)
  process.exit(failed ? 1 : 0)
}

main().catch(async (error) => {
  console.error(error)
  await stopServer()
  process.exit(1)
})
