import { RouterProvider } from 'react-router'
import { FullPageLoader } from '@/components/shared/states'
import { useAuthStore } from '@/stores/auth-store'
import { router } from './router'

export function App() {
  // Until we know whether the refresh cookie still holds a session, show a loader rather than
  // flashing the login page at someone who is actually signed in.
  const status = useAuthStore((s) => s.status)
  return status === 'loading' ? <FullPageLoader /> : <RouterProvider router={router} />
}
