import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { Toaster } from '@/components/ui/sonner'
import { TooltipProvider } from '@/components/ui/tooltip'
import { toApiError } from '@/lib/api/errors'

export function createQueryClient() {
  const client = new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000, // data is "fresh" for 30s, so navigating back doesn't refetch instantly
        refetchOnWindowFocus: true,
        // Retry network/server hiccups (Render cold start, Neon waking up), never 4xx: those won't fix themselves.
        retry: (failureCount, error) => {
          const status = toApiError(error).status
          return failureCount < 2 && (status === 0 || status >= 500)
        },
      },
    },
  })

  // Image queries hold blob: URLs (see useProductImage). Free the memory when the cache evicts them.
  client.getQueryCache().subscribe((event) => {
    const data: unknown = event.query.state.data
    if (event.type === 'removed' && typeof data === 'string' && data.startsWith('blob:')) {
      URL.revokeObjectURL(data)
    }
  })

  return client
}

export function Providers({ children }: { children: ReactNode }) {
  const [queryClient] = useState(createQueryClient)
  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider delayDuration={300}>
        {children}
        <Toaster />
      </TooltipProvider>
    </QueryClientProvider>
  )
}
