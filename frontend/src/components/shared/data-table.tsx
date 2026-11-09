import { ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { cn } from '@/lib/utils'

export type Column<T> = {
  key: string
  header: string
  cell: (row: T) => ReactNode
  /** Server-side sort field name. Omit for unsortable columns. */
  sortField?: string
  className?: string
  /** Hide on small screens to keep the table readable on phones. */
  hideOnMobile?: boolean
}

export type SortState = { field: string; desc: boolean } | null

type DataTableProps<T> = {
  columns: Column<T>[]
  rows: T[] | undefined
  rowKey: (row: T) => string
  isLoading: boolean
  sort: SortState
  onSortChange: (sort: SortState) => void
  onRowClick?: (row: T) => void
  empty: ReactNode
  caption: string
}

/**
 * Presentational table for SERVER-side lists: it renders what the API returned and reports
 * sort/page intents upward. Sorting and paging happen in SQL, not in the browser, because a
 * company can have 50,000 products.
 */
export function DataTable<T>({ columns, rows, rowKey, isLoading, sort, onSortChange, onRowClick, empty, caption }: DataTableProps<T>) {
  // Click cycle per column: ascending â†’ descending â†’ back to the default order.
  const toggleSort = (field: string) => {
    if (!sort || sort.field !== field) onSortChange({ field, desc: false })
    else onSortChange(sort.desc ? null : { field, desc: true })
  }

  return (
    <Table>
      <caption className="sr-only">{caption}</caption>
      <TableHeader>
        <TableRow className="hover:bg-transparent">
          {columns.map((col) => (
            <TableHead
              key={col.key}
              className={cn('h-11 text-muted-foreground', col.hideOnMobile && 'hidden md:table-cell', col.className)}
              aria-sort={sort && sort.field === col.sortField ? (sort.desc ? 'descending' : 'ascending') : undefined}
            >
              {col.sortField ? (
                <button
                  type="button"
                  onClick={() => toggleSort(col.sortField!)}
                  className="-ml-2 inline-flex items-center gap-1.5 rounded-md px-2 py-1 font-semibold hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
                >
                  {col.header}
                  {sort && sort.field === col.sortField ? (
                    sort.desc ? <ArrowDown className="size-3.5" /> : <ArrowUp className="size-3.5" />
                  ) : (
                    <ArrowUpDown className="size-3.5 opacity-50" />
                  )}
                </button>
              ) : (
                <span className="font-semibold">{col.header}</span>
              )}
            </TableHead>
          ))}
        </TableRow>
      </TableHeader>
      <TableBody>
        {isLoading ? (
          Array.from({ length: 5 }, (_, i) => (
            <TableRow key={i}>
              {columns.map((col) => (
                <TableCell key={col.key} className={cn(col.hideOnMobile && 'hidden md:table-cell')}>
                  <Skeleton className="h-4 w-full max-w-40" />
                </TableCell>
              ))}
            </TableRow>
          ))
        ) : rows && rows.length > 0 ? (
          rows.map((row) => (
            <TableRow
              key={rowKey(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={cn(onRowClick && 'cursor-pointer')}
            >
              {columns.map((col) => (
                <TableCell key={col.key} className={cn('py-3', col.hideOnMobile && 'hidden md:table-cell', col.className)}>
                  {col.cell(row)}
                </TableCell>
              ))}
            </TableRow>
          ))
        ) : (
          <TableRow className="hover:bg-transparent">
            <TableCell colSpan={columns.length}>{empty}</TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  )
}

export function Pagination({
  page,
  pageSize,
  totalCount,
  onPageChange,
}: {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, totalCount)

  return (
    <nav aria-label="Pagination" className="flex items-center justify-between gap-4 border-t px-4 py-3 text-sm">
      <p className="text-muted-foreground tabular">
        {from}–{to} of {totalCount}
      </p>
      <div className="flex items-center gap-2">
        <Button variant="outline" size="icon-sm" onClick={() => onPageChange(page - 1)} disabled={page <= 1} aria-label="Previous page">
          <ChevronLeft />
        </Button>
        <span className="tabular min-w-16 text-center text-muted-foreground">
          {page} / {totalPages}
        </span>
        <Button variant="outline" size="icon-sm" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages} aria-label="Next page">
          <ChevronRight />
        </Button>
      </div>
    </nav>
  )
}
