import { Construction } from 'lucide-react'
import { Link } from 'react-router'
import { EmptyState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'

/**
 * Honest placeholder for modules on the roadmap. It says what's coming instead of showing fake
 * data or dead buttons. Each one is replaced by the real page in Phase 4.
 */
export function ComingSoonPage({ title, description }: { title: string; description: string }) {
  return (
    <>
      <PageHeader title={title} />
      <Card className="shadow-card">
        <EmptyState
          icon={Construction}
          title={`${title} isn't available yet`}
          description={description}
          action={
            <Button asChild variant="outline">
              <Link to="/">Back to dashboard</Link>
            </Button>
          }
        />
      </Card>
    </>
  )
}

export function NotFoundPage() {
  return (
    <div className="grid min-h-[60svh] place-items-center">
      <EmptyState
        icon={Construction}
        title="Page not found"
        description="The address may be mistyped, or the page has moved."
        action={
          <Button asChild>
            <Link to="/">Go to dashboard</Link>
          </Button>
        }
      />
    </div>
  )
}
