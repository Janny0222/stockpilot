/**
 * Cloudflare Pages Function: forwards https://<site>.pages.dev/api/* to the API on Render.
 *
 * Why a proxy instead of calling Render directly from the browser?
 *  - Same origin: the refresh-token cookie stays first-party (Safari blocks third-party cookies),
 *    and no CORS preflight is needed.
 *  - Real visitor IP: we pass CF-Connecting-IP plus a shared secret, so the API's rate limiter
 *    sees individual users instead of a handful of Cloudflare IPs.
 *
 * Configure in Cloudflare Pages → Settings → Variables and Secrets:
 *   API_ORIGIN    e.g. https://stockpilot-api.onrender.com   (no trailing slash)
 *   PROXY_SECRET  same value as ForwardedHeaders__ProxySecret on Render (encrypt it)
 */

interface Env {
  API_ORIGIN: string
  PROXY_SECRET?: string
}

type PagesContext = { request: Request; env: Env }

// Hop-by-hop / identity headers we must not forward as-is.
const STRIP = ['host', 'cf-connecting-ip', 'x-forwarded-for', 'x-real-ip', 'x-client-ip', 'x-proxy-secret']

export const onRequest = async ({ request, env }: PagesContext): Promise<Response> => {
  if (!env.API_ORIGIN) {
    return Response.json({ status: 500, code: 'PROXY_NOT_CONFIGURED', detail: 'API_ORIGIN is not set.' }, { status: 500 })
  }

  const incoming = new URL(request.url)
  const target = new URL(incoming.pathname + incoming.search, env.API_ORIGIN)

  const headers = new Headers(request.headers)
  for (const name of STRIP) headers.delete(name)

  const visitorIp = request.headers.get('cf-connecting-ip')
  if (visitorIp && env.PROXY_SECRET) {
    headers.set('X-Client-IP', visitorIp)
    headers.set('X-Proxy-Secret', env.PROXY_SECRET)
  }
  headers.set('X-Forwarded-Proto', 'https')
  headers.set('X-Forwarded-Host', incoming.host)

  const hasBody = !['GET', 'HEAD'].includes(request.method)

  try {
    // Set-Cookie headers from the API pass through untouched: no Domain attribute, so the browser
    // stores the cookie for this pages.dev host.
    return await fetch(target, {
      method: request.method,
      headers,
      body: hasBody ? request.body : undefined,
      redirect: 'manual',
    })
  } catch {
    return Response.json(
      { status: 502, code: 'API_UNREACHABLE', detail: "Can't reach the server right now. Try again in a moment." },
      { status: 502 },
    )
  }
}
