export const pageSizeOptions = [20, 50, 100, 200, 500]
export function pageWindow(total: number, page: number, size: number) {
  const pageSize = pageSizeOptions.includes(size) ? size : 20
  const count = Math.max(0, Math.floor(Number.isFinite(total) ? total : 0))
  const pages = Math.max(1, Math.ceil(count / pageSize))
  const current = Math.min(pages, Math.max(1, Math.floor(Number.isFinite(page) ? page : 1)))
  const start = (current - 1) * pageSize
  return {
    pageSize,
    pages,
    current,
    start,
    end: Math.min(count, start + pageSize),
    count: Math.max(0, Math.min(pageSize, count - start))
  }
}
