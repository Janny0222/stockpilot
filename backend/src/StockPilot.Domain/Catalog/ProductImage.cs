using StockPilot.Domain.Common;

namespace StockPilot.Domain.Catalog;

/// <summary>Metadata for an uploaded product photo. The bytes live in file storage under <see cref="StorageKey"/>.</summary>
public sealed class ProductImage : TenantEntity
{
    private ProductImage() { }

    internal ProductImage(Guid tenantId, Guid productId, string storageKey, string contentType, long sizeBytes, int width, int height, bool isPrimary, int sortOrder)
        : base(tenantId)
    {
        ProductId = productId;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        IsPrimary = isPrimary;
        SortOrder = sortOrder;
    }

    public Guid ProductId { get; private set; }

    /// <summary>Random, server-generated key. User-supplied file names are never used as storage paths.</summary>
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsPrimary { get; private set; }
    public int SortOrder { get; private set; }

    internal void MarkPrimary(bool isPrimary) => IsPrimary = isPrimary;
}

/// <summary>Bytes of an uploaded file. Kept in PostgreSQL (see DatabaseFileStorage for why).</summary>
public sealed class StoredFile : ITenantOwned
{
    private StoredFile() { }

    public StoredFile(Guid tenantId, string key, string contentType, byte[] data, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Key = key;
        ContentType = contentType;
        Data = data;
        SizeBytes = data.LongLength;
        CreatedAt = createdAt;
    }

    public string Key { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public byte[] Data { get; private set; } = [];
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
