using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockPilot.Domain.Catalog;

namespace StockPilot.Infrastructure.Persistence.Seed;

/// <summary>
/// Realistic catalogue for the demo company "Northwind Supply Co." (a Philippine hardware and electrical
/// distributor). Prices in PHP. Runs only when the demo company has no products yet, so it's safe on
/// every start and fills in companies seeded before this module existed.
/// </summary>
internal static class DemoCatalog
{
    private sealed record Item(string Category, string Name, string Unit, (string Sku, string? Variant, string? Barcode, decimal Cost, decimal Price, decimal Reorder)[] Variants);

    private static readonly (string Parent, string[] Children)[] Categories =
    [
        ("Electrical", ["Wires & Cables", "Lighting", "Switches & Outlets", "Circuit Protection"]),
        ("Plumbing", ["Pipes & Fittings", "Faucets & Valves"]),
        ("Tools", ["Hand Tools", "Power Tools"]),
        ("Paint & Sundries", []),
        ("Fasteners", []),
    ];

    private static readonly Item[] Items =
    [
        new("Wires & Cables", "THHN Stranded Copper Wire 3.5mm²", "roll",
        [
            ("THHN-35-RED", "Red, 150 m", "4806512340011", 2380m, 2950m, 5),
            ("THHN-35-BLK", "Black, 150 m", "4806512340028", 2380m, 2950m, 5),
            ("THHN-35-WHT", "White, 150 m", "4806512340035", 2380m, 2950m, 5),
        ]),
        new("Wires & Cables", "THHN Stranded Copper Wire 2.0mm²", "roll",
        [
            ("THHN-20-RED", "Red, 150 m", "4806512340103", 1450m, 1820m, 5),
            ("THHN-20-BLK", "Black, 150 m", "4806512340110", 1450m, 1820m, 5),
        ]),
        new("Wires & Cables", "Duplex Flat Cord #16 AWG", "roll", [("PDX-16-75", "75 m", "4806512340202", 980m, 1250m, 8)]),
        new("Wires & Cables", "UTP Cat6 Network Cable", "box", [("UTP-C6-305", "305 m box", "4806512340219", 3650m, 4590m, 3)]),
        new("Lighting", "LED Bulb E27 9W Daylight", "pcs", [("LED-E27-9W", null, "4806512341001", 62m, 95m, 60)]),
        new("Lighting", "LED Bulb E27 12W Daylight", "pcs", [("LED-E27-12W", null, "4806512341018", 78m, 125m, 60)]),
        new("Lighting", "LED Tube T8 18W 4ft", "pcs",
        [
            ("LED-T8-18-DL", "Daylight", "4806512341025", 145m, 210m, 40),
            ("LED-T8-18-WW", "Warm white", "4806512341032", 145m, 210m, 20),
        ]),
        new("Lighting", "LED Floodlight 50W IP65", "pcs", [("LED-FL-50W", null, "4806512341049", 520m, 780m, 10)]),
        new("Lighting", "Emergency Light Twin Head", "pcs", [("EML-TWIN", null, "4806512341056", 890m, 1290m, 6)]),
        new("Switches & Outlets", "1-Gang Wall Switch", "pcs", [("SW-1G-WH", null, "4806512342008", 38m, 65m, 50)]),
        new("Switches & Outlets", "2-Gang Wall Switch", "pcs", [("SW-2G-WH", null, "4806512342015", 52m, 85m, 40)]),
        new("Switches & Outlets", "Duplex Universal Outlet", "pcs", [("OUT-DPX-UNI", null, "4806512342022", 48m, 79m, 50)]),
        new("Switches & Outlets", "Aircon Outlet Tandem 3-Prong", "pcs", [("OUT-AC-3P", null, "4806512342039", 68m, 115m, 25)]),
        new("Switches & Outlets", "Utility Box PVC 2×4", "pcs", [("UBOX-PVC-24", null, "4806512342046", 9m, 18m, 200)]),
        new("Circuit Protection", "Plug-in Circuit Breaker 1-Pole", "pcs",
        [
            ("CB-1P-20A", "20 A", "4806512343005", 210m, 320m, 20),
            ("CB-1P-30A", "30 A", "4806512343012", 210m, 320m, 20),
        ]),
        new("Circuit Protection", "Plug-in Circuit Breaker 2-Pole 60A", "pcs", [("CB-2P-60A", null, "4806512343029", 520m, 760m, 10)]),
        new("Circuit Protection", "Panel Board 6-Branch", "pcs", [("PB-6BR", null, "4806512343036", 980m, 1450m, 5)]),
        new("Pipes & Fittings", "PVC Pipe Blue Orange Series", "pcs",
        [
            ("PVC-BL-12", "½\" × 3 m", "4806512344002", 74m, 115m, 40),
            ("PVC-BL-34", "¾\" × 3 m", "4806512344019", 98m, 150m, 40),
            ("PVC-OR-4", "4\" × 3 m (sanitary)", "4806512344026", 385m, 560m, 15),
        ]),
        new("Pipes & Fittings", "PVC Elbow 90° ½\"", "pcs", [("PVC-ELB-12", null, "4806512344033", 6m, 12m, 200)]),
        new("Pipes & Fittings", "PVC Tee ½\"", "pcs", [("PVC-TEE-12", null, "4806512344040", 8m, 15m, 150)]),
        new("Pipes & Fittings", "PVC Solvent Cement 100cc", "pcs", [("PVC-CEM-100", null, "4806512344057", 42m, 68m, 40)]),
        new("Pipes & Fittings", "Teflon Tape ½\"", "pcs", [("TFL-12", null, "4806512344064", 9m, 18m, 150)]),
        new("Faucets & Valves", "Chrome Lavatory Faucet", "pcs", [("FCT-LAV-CHR", null, "4806512345009", 340m, 520m, 10)]),
        new("Faucets & Valves", "Brass Gate Valve ½\"", "pcs", [("GV-BR-12", null, "4806512345016", 165m, 255m, 20)]),
        new("Faucets & Valves", "Angle Valve with Hose", "set", [("ANV-HOSE", null, "4806512345023", 185m, 290m, 15)]),
        new("Hand Tools", "Claw Hammer 16 oz", "pcs", [("HAM-16OZ", null, "4806512346006", 210m, 345m, 12)]),
        new("Hand Tools", "Screwdriver Set 6 pcs", "set", [("SD-SET-6", null, "4806512346013", 245m, 395m, 10)]),
        new("Hand Tools", "Combination Pliers 8\"", "pcs", [("PLR-COMB-8", null, "4806512346020", 165m, 275m, 12)]),
        new("Hand Tools", "Measuring Tape", "pcs",
        [
            ("MT-5M", "5 m", "4806512346037", 85m, 145m, 20),
            ("MT-7.5M", "7.5 m", "4806512346044", 125m, 210m, 15),
        ]),
        new("Hand Tools", "Hacksaw Frame with Blade", "pcs", [("HKS-FRAME", null, "4806512346051", 175m, 285m, 8)]),
        new("Power Tools", "Cordless Drill Driver 12V", "set", [("PT-DRL-12V", null, "4806512347003", 2150m, 3290m, 4)]),
        new("Power Tools", "Angle Grinder 4\" 710W", "pcs", [("PT-AG-4", null, "4806512347010", 1380m, 2190m, 4)]),
        new("Power Tools", "Masonry Drill Bit Set 5 pcs", "set", [("DB-MAS-5", null, "4806512347027", 210m, 345m, 10)]),
        new("Paint & Sundries", "Latex Paint Flat White", "pcs",
        [
            ("PNT-LTX-WHT-1G", "1 gallon", "4806512348000", 520m, 745m, 12),
            ("PNT-LTX-WHT-4L", "4 L pail", "4806512348017", 1890m, 2650m, 4),
        ]),
        new("Paint & Sundries", "Quick-Dry Enamel Black 1L", "pcs", [("PNT-QDE-BLK-1L", null, "4806512348024", 245m, 365m, 12)]),
        new("Paint & Sundries", "Paint Brush 2\"", "pcs", [("BRS-2IN", null, "4806512348031", 32m, 55m, 40)]),
        new("Paint & Sundries", "Paint Roller 7\" with Tray", "set", [("RLR-7-TRAY", null, "4806512348048", 135m, 220m, 15)]),
        new("Paint & Sundries", "Masking Tape 1\"", "roll", [("MSK-1IN", null, "4806512348055", 28m, 48m, 50)]),
        new("Fasteners", "Concrete Nail", "kg",
        [
            ("NL-CON-2IN", "2\"", null, 92m, 135m, 25),
            ("NL-CON-3IN", "3\"", null, 92m, 135m, 25),
        ]),
        new("Fasteners", "Tekscrew 10×1\"", "box", [("TEK-10-1", "Box of 100", "4806512349007", 155m, 245m, 15)]),
        new("Fasteners", "Expansion Bolt ⅜\" × 3\"", "pcs", [("EXB-38-3", null, "4806512349014", 14m, 25m, 100)]),
        new("Fasteners", "Plastic Wall Plug #6", "pack", [("WPLUG-6", "Pack of 100", "4806512349021", 45m, 75m, 30)]),
    ];

    public static async Task SeedAsync(AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId, ct))
        {
            return;
        }

        var categories = new Dictionary<string, ProductCategory>();
        foreach (var (parentName, children) in Categories)
        {
            var parent = new ProductCategory(tenantId, parentName, null, null);
            categories[parentName] = parent;
            db.ProductCategories.Add(parent);
            foreach (var child in children)
            {
                var category = new ProductCategory(tenantId, child, null, parent);
                categories[child] = category;
                db.ProductCategories.Add(category);
            }
        }

        foreach (var item in Items)
        {
            var product = new Product(tenantId, item.Name, null, categories[item.Category].Id, item.Unit);
            foreach (var v in item.Variants)
            {
                product.AddVariant(v.Sku, v.Variant, v.Barcode, v.Cost, v.Price, v.Reorder);
            }

            db.Products.Add(product);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo catalogue: {Products} products in {Categories} categories", Items.Length, categories.Count);
    }
}
