# Phase 4, Module 1: Products

## What it does
- **Categories**: one level of nesting ("Electrical → Lighting"). Unique names among siblings. Only empty
  categories can be deleted. Filtering by a parent includes its subcategories.
- **Products**: name, description, category, unit, status (Active/Archived). Never hard-deleted: once stock
  moves, history must keep pointing at them.
- **Variants**: the stocked and sold unit. Each has a SKU (unique per company, upper-cased), an optional barcode
  (unique per company), cost and selling price (`numeric(18,4)`), and a reorder level (`numeric(18,3)`).
  A product always keeps at least one active variant.
- **Images**: up to 5 per product, stored as WebP.
- **Search** across name, SKU and barcode; category and status filters; sort by name, price or date; paging.
  Plus an exact SKU/barcode **lookup** endpoint for scanners.

## Design decisions worth explaining in an interview

**1. Variants always exist.** A "simple" product is just a product with one variant. Inventory, purchasing and
sales always reference `ProductVariantId`, with no "does this product have variants?" branches anywhere.

**2. Cost prices are a permission, enforced by the API.** `inventory.view_cost` decides whether `costPrice` is
in the response (it's `null` otherwise). A user who can't see costs also can't overwrite them: the update handler
keeps the stored cost. Hiding the column in the UI alone would leak it through DevTools.

**3. Optimistic concurrency.** Products and variants carry a `version` (PostgreSQL's `xmin`). The client sends back
the version it loaded. If someone saved in between, the database version differs, EF raises a concurrency
exception, and the API returns **409 CONCURRENCY_CONFLICT** instead of silently overwriting the other person's work.

**4. Uniqueness: friendly check plus database guarantee.** The handler checks "is this SKU taken?" to return a
precise message, but two simultaneous requests can both pass that check. The unique index
`(tenant_id, sku)` is what actually guarantees correctness, and its violation is translated to the same 409.

**5. Images are untrusted input.** The pipeline (`SkiaImageProcessor`):
1. 2 MB cap while streaming (Kestrel also caps the request at 3 MB).
2. File signature check (JPEG/PNG/WebP magic bytes). The name and Content-Type are ignored.
3. Pixel-count cap before decoding (decompression bombs).
4. Decode → fix EXIF rotation → resize to ≤1200 px → **re-encode as WebP**. Metadata such as GPS location is gone,
   and so is anything smuggled inside the original file.
5. Server-generated storage key. User file names never become paths.

Images are served only to the same company (tenant query filter) with `Cache-Control: private` so CDNs don't keep
copies. The browser fetches them with the bearer token and shows a `blob:` URL.

**6. Why images live in PostgreSQL (for now).** Render's free disk is wiped on every deploy, and Cloudflare R2 needs a
payment method. Re-encoded WebPs are small, so Neon's free storage holds hundreds. `IFileStorage` isolates the
choice: moving to S3/R2 is one new class.

## Bug caught while building this (good to know for EF Core)
IDs are generated in C# (UUID v7). When a new image was added to an already-loaded product, EF assumed "has a key ⇒
existing row" and tried to UPDATE a row that didn't exist. The tenant-isolation interceptor blocked it first (it saw
`TenantId` "changing"). Fix: `ValueGeneratedNever()` on every entity key, which tells EF that client-generated keys
mean new rows. One line in `AppDbContext`, and a classic EF Core pitfall.

## Tests added
- Domain: SKU normalisation, last-variant rule, archived product rules, image primary/limit rules, category depth.
- Image processor: PNG→WebP, resize, text-renamed-to-png rejected, corrupt file rejected, size cap.
- API (real PostgreSQL): create/search/filter/sort, SKU unique per company only, cross-company 404s (products,
  categories, images), Sales Staff sees no costs and gets 403 on writes, stale edit → 409, archive/restore,
  last-variant 422, category in use 422, image upload and serving and isolation, barcode lookup.
- Frontend: form rules, SKU-conflict error on the right field, create → redirect, role-based "Add product".
