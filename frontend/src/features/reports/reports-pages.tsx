import { ArrowLeft, ArrowRight, Download, FileChartColumn, LoaderCircle } from 'lucide-react'
import { Link, useParams, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { MOVEMENT_LABELS } from '@/features/inventory/api'
import { WarehouseSelect } from '@/features/inventory/components/inventory-parts'
import { NotFoundPage } from '@/app/coming-soon-page'
import { toApiError } from '@/lib/api/errors'
import type { InventoryMovementType, ReportColumnDto } from '@/lib/api/types'
import { formatDate, formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { isReportKey, REPORTS, useDownloadReport, useReport, type ReportKey, type ReportParams } from './api'

export function ReportsPage() {
  return (
    <>
      <PageHeader
        title="Reports"
        description="Read-only views of your stock, sales and purchases. Every figure comes straight from the ledger and documents."
      />
      <ul className="grid max-w-5xl gap-4 sm:grid-cols-2">
        {(Object.keys(REPORTS) as ReportKey[]).map((key) => (
          <li key={key}>
            <Link
              to={`/reports/${key}`}
              className="group flex h-full items-start gap-4 rounded-xl border bg-card p-5 shadow-card transition-colors hover:border-primary/40 focus-visible:outline-2 focus-visible:outline-ring"
            >
              <span className="flex-1">
                <span className="block font-semibold">{REPORTS[key].title}</span>
                <span className="mt-1 block text-sm text-muted-foreground">{REPORTS[key].summary}</span>
              </span>
              <ArrowRight className="mt-0.5 size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
            </Link>
          </li>
        ))}
      </ul>
    </>
  )
}

const dateTime = new Intl.DateTimeFormat('en-PH', { dateStyle: 'medium', timeStyle: 'short' })

function formatCell(value: unknown, column: ReportColumnDto, currency: string) {
  if (value === null || value === undefined || value === '') return '—'
  switch (column.kind) {
    case 'Money':
      return formatMoney(Number(value), currency)
    case 'Quantity':
      return formatQuantity(Number(value))
    case 'Percent':
      return `${Number(value).toFixed(1)}%`
    case 'Date':
      return formatDate(String(value))
    case 'DateTime':
      return dateTime.format(new Date(String(value)))
    default:
      return String(value)
  }
}

const isNumeric = (c: ReportColumnDto) => c.kind === 'Money' || c.kind === 'Quantity' || c.kind === 'Percent'

export function ReportPage() {
  const { key } = useParams()
  if (!isReportKey(key)) return <NotFoundPage />
  return <Report reportKey={key} />
}

function Report({ reportKey }: { reportKey: ReportKey }) {
  const meta = REPORTS[reportKey]
  const currency = useCurrency()
  const canExport = useHasPermission(P.Reports.Export)
  const download = useDownloadReport()
  const [search, setSearch] = useSearchParams()

  const params: ReportParams = {
    from: meta.dated ? (search.get('from') ?? undefined) : undefined,
    to: meta.dated ? (search.get('to') ?? undefined) : undefined,
    warehouseId: search.get('warehouse') ?? undefined,
    groupBy: meta.groupBy ? (search.get('groupBy') ?? meta.groupBy[0]!.value) : undefined,
    type: reportKey === 'stock-movements' ? ((search.get('type') as InventoryMovementType | null) ?? undefined) : undefined,
  }
  const query = useReport(reportKey, params)

  const update = (changes: Record<string, string | null | undefined>) =>
    setSearch(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(changes)) {
          if (v) next.set(k, v)
          else next.delete(k)
        }
        return next
      },
      { replace: true },
    )

  const report = query.data
  const exportCsv = () =>
    download.mutate(
      { key: reportKey, params },
      { onSuccess: (name) => toast.success(`${decodeURIComponent(name)} downloaded.`), onError: (e) => toast.error(toApiError(e).message) },
    )

  return (
    <>
      <PageHeader
        title={meta.title}
        description={report?.description ?? meta.summary}
        actions={
          <>
            <Button asChild variant="outline">
              <Link to="/reports">
                <ArrowLeft /> All reports
              </Link>
            </Button>
            {canExport && (
              <Button onClick={exportCsv} disabled={download.isPending || !report}>
                {download.isPending ? <LoaderCircle className="animate-spin" aria-hidden="true" /> : <Download />} Export CSV
              </Button>
            )}
          </>
        }
      />

      <Card className="mb-6 gap-0 py-0 shadow-card">
        <CardContent className="flex flex-wrap items-end gap-4 p-4">
          {meta.dated && (
            <>
              <div className="grid gap-1.5">
                <Label htmlFor="report-from">From</Label>
                <Input
                  id="report-from"
                  type="date"
                  className="h-10 w-40 bg-card"
                  value={params.from ?? report?.from ?? ''}
                  max={params.to ?? report?.to ?? undefined}
                  onChange={(e) => update({ from: e.target.value })}
                />
              </div>
              <div className="grid gap-1.5">
                <Label htmlFor="report-to">To</Label>
                <Input
                  id="report-to"
                  type="date"
                  className="h-10 w-40 bg-card"
                  value={params.to ?? report?.to ?? ''}
                  min={params.from ?? report?.from ?? undefined}
                  onChange={(e) => update({ to: e.target.value })}
                />
              </div>
            </>
          )}
          <WarehouseSelect
            id="report-warehouse"
            label="Warehouse"
            value={params.warehouseId}
            onChange={(v) => update({ warehouse: v })}
            allowAll
            className="w-52"
          />
          {meta.groupBy && (
            <div className="grid gap-1.5">
              <Label htmlFor="report-group">Group</Label>
              <Select value={params.groupBy} onValueChange={(v) => update({ groupBy: v === meta.groupBy![0]!.value ? null : v })}>
                <SelectTrigger id="report-group" className="h-10 w-40 bg-card">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {meta.groupBy.map((g) => (
                    <SelectItem key={g.value} value={g.value}>
                      {g.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
          {reportKey === 'stock-movements' && (
            <div className="grid gap-1.5">
              <Label htmlFor="report-type">Movement</Label>
              <Select value={params.type ?? 'all'} onValueChange={(v) => update({ type: v === 'all' ? null : v })}>
                <SelectTrigger id="report-type" className="h-10 w-44 bg-card">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All movements</SelectItem>
                  {(Object.keys(MOVEMENT_LABELS) as InventoryMovementType[]).map((t) => (
                    <SelectItem key={t} value={t}>
                      {MOVEMENT_LABELS[t]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
        </CardContent>
      </Card>

      {query.isError ? (
        <Card className="shadow-card">
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        </Card>
      ) : !report ? (
        <Skeleton className="h-96" />
      ) : (
        <Card className={cn('gap-0 overflow-hidden py-0 shadow-card transition-opacity', query.isFetching && 'opacity-70')}>
          {report.truncated && (
            <p className="border-b bg-warning/10 px-6 py-3 text-sm text-warning" role="status">
              Showing the first {report.rows.length.toLocaleString()} rows. Narrow the period or warehouse to see the rest.
            </p>
          )}
          {report.rows.length === 0 ? (
            <EmptyState
              icon={FileChartColumn}
              title="Nothing to report"
              description={meta.dated ? 'No activity matches these filters. Try a wider period.' : 'No data matches these filters.'}
            />
          ) : (
            <Table>
              <caption className="sr-only">{report.title}</caption>
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  {report.columns.map((c, i) => (
                    <TableHead key={c.key} className={cn(isNumeric(c) && 'text-right', i === 0 && 'pl-6', i === report.columns.length - 1 && 'pr-6')}>
                      {c.label}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {report.rows.map((row, r) => (
                  <TableRow key={r}>
                    {report.columns.map((c, i) => (
                      <TableCell
                        key={c.key}
                        className={cn(
                          isNumeric(c) && 'text-right tabular',
                          c.kind === 'DateTime' && 'whitespace-nowrap',
                          i === 0 && 'pl-6 font-medium',
                          i === report.columns.length - 1 && 'pr-6',
                        )}
                      >
                        {formatCell(row[i], c, currency)}
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
              {report.totals && (
                <TableFooter>
                  <TableRow className="hover:bg-transparent">
                    {report.columns.map((c, i) => (
                      <TableCell
                        key={c.key}
                        className={cn('font-semibold', isNumeric(c) && 'text-right tabular', i === 0 && 'pl-6', i === report.columns.length - 1 && 'pr-6')}
                      >
                        {report.totals![i] === null ? '' : formatCell(report.totals![i], c, currency)}
                      </TableCell>
                    ))}
                  </TableRow>
                </TableFooter>
              )}
            </Table>
          )}
        </Card>
      )}
    </>
  )
}
