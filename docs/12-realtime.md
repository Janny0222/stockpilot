# M11, step 1: Live notifications (SignalR)

Notifications from Module 6b now arrive the moment they're committed. The bell updates, a toast appears with a
**View** button, and no page refresh is needed. Polling stays as the fallback.

## How a notification travels
```
use case adds Notification rows ──► SaveChanges ──► COMMIT ──► NotificationDispatchInterceptor
                                                                        │
                                              IRealtimeNotifier (Application port)
                                                                        │
                                  SignalRRealtimeNotifier (API) ──► hub ──► recipient's open tabs
```
- **Pushed only after commit.** The interceptor watches the unit of work:
  - SaveChanges outside a transaction pushes right away.
  - Inside `ExecuteInTransactionAsync`, the rows are held until **COMMIT** and dropped on **ROLLBACK**.
  - A retried transaction starts clean.

  So nobody is told about stock that "ran out" in a transaction that then failed. An integration test saves a
  notification inside a transaction, throws, and checks that nothing arrives, and that a committed one does.
- **Best effort.** If a push fails it's logged, not thrown, because the row is already in the database. The bell's
  list and count are always read from there, so a missed push costs nothing.
- **Per user.** Pushes go to `Clients.User(recipientId)` (every tab that person has open), routed by our own `sub`
  claim. Nobody else's connection receives them.
- **Same shape as `GET /notifications`.** The client can treat a push exactly like a list item.

## The hub: `/api/v1/hubs/notifications`
- **Under `/api`,** so the Cloudflare Pages proxy and the Vite dev proxy (`ws: true`) forward it like any API call. No
  new route or CORS rule is needed.
- **`[Authorize]`** runs the same JWT validation, session check (deactivated user, suspended company) and fallback
  policy as the REST API.
- **The token comes from the query string on the hub path only.** Browsers can't set headers on a WebSocket, so the
  client sends `?access_token=`. `OnMessageReceived` accepts it **only** for the hub path. A test checks that
  `/auth/me?access_token=…` stays 401. Request logging writes the path, never the query string.
- **`CloseOnAuthenticationExpiration`:** the server closes the socket when the token it was opened with expires. The
  client reconnects with a fresh token, so an open tab can't outlive a deactivated session by more than one token
  lifetime (15 minutes).
- **No backplane:** there's a single Render instance. With more instances, add Redis or Azure SignalR, so that a push
  from instance A reaches a browser connected to instance B.

## Client (`features/notifications/live.tsx`)
- **One connection per signed-in company user**, mounted in the app shell. Platform admins have no inbox, so they
  don't connect.
- **On a push:** refresh the bell queries and show a toast; "View" opens the record.
- **On reconnect:** refresh as well, in case something arrived while disconnected.
- **Token before every (re)connect:** the current one, or a refreshed one if it has less than 30 s left.
- **Retries:** 0 s, 2 s, 5 s, 10 s, then every 30 s, for the first start too. The free-tier API sleeps and takes a
  while to wake.
- **Polling is the fallback:** while the connection is up, the bell **stops polling**. While it's down (server
  asleep, offline, or a proxy that won't pass WebSockets), it polls every 60 s as before.
- **Transport:** SignalR picks WebSockets, and falls back to Server-Sent Events, then long polling, by itself. Locally
  the browser uses WebSockets: only the `negotiate` call shows as an HTTP request.
- **`@microsoft/signalr` 8.0.7:** 11.0.0 was published the day before this was written, so it's too new to trust yet.
  The wire protocol is the same.

## After deploying
- **Check the transport:** open DevTools → Network → WS on the live site, and there should be one socket to
  `/api/v1/hubs/notifications`.
- **If you see repeated `GET …/hubs/notifications?id=` requests instead,** the Pages Function isn't passing the
  upgrade through and the client is long polling. It still works, just less efficiently, and each poll counts as a
  Function invocation.

## Tests
- **Integration (3):**
  - a committed notification reaches its recipient live with the stored ID, and the person who acted gets nothing
  - nothing is pushed for a rolled-back transaction
  - the hub rejects anonymous callers, and query-string tokens work only on the hub
- **Frontend (3), with a fake SignalR connection:**
  - a push updates the bell, shows the toast, and View navigates
  - an almost-expired token is refreshed before connecting
  - no connection without a company, and it disconnects on sign-out
