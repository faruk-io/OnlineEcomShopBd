using TechBazar.Domain.Enums;

namespace TechBazar.Infrastructure.Seeding;

internal sealed record CategorySeed(string Name, string? Parent, string Description);
internal sealed record BrandSeed(string Name, string Description);

/// <param name="Specs">One group per line: <c>Group|Key=Value;Key=Value</c>.</param>
internal sealed record ProductSeed(
    string Category, string Brand, string Name, decimal Price, decimal? Sale, int WarrantyMonths,
    string Features, string Specs, StockStatus Stock = StockStatus.InStock, bool Featured = false);

/// <summary>
/// Illustrative catalog for development. Prices are plausible BDT street prices, NOT live market data.
/// Product/brand names are real manufacturer product names used only to make the data realistic; no third-party text or imagery.
/// </summary>
internal static class CatalogSeedData
{
    public static readonly CategorySeed[] Categories =
    [
        new("Desktop", null, "Branded and custom-built desktop PCs"),
        new("Gaming PC", "Desktop", "Pre-built gaming desktops"),
        new("Office PC", "Desktop", "Desktops for office, study and everyday use"),
        new("Laptop", null, "Laptops for every budget"),
        new("Gaming Laptop", "Laptop", "High-refresh laptops with dedicated graphics"),
        new("Everyday Laptop", "Laptop", "Student and office laptops"),
        new("Component", null, "PC parts for custom builds and upgrades"),
        new("Processor", "Component", "Intel and AMD desktop CPUs"),
        new("Motherboard", "Component", "Motherboards for Intel LGA1700 and AMD AM4/AM5"),
        new("RAM", "Component", "Desktop and laptop memory modules"),
        new("SSD", "Component", "NVMe and SATA solid state drives"),
        new("Graphics Card", "Component", "NVIDIA GeForce and AMD Radeon graphics cards"),
        new("Power Supply", "Component", "80 PLUS certified PSUs"),
        new("Casing", "Component", "PC cases and chassis"),
        new("CPU Cooler", "Component", "Air coolers and all-in-one liquid coolers"),
        new("Monitor", null, "Office and gaming monitors"),
        new("UPS", null, "Uninterruptible power supplies"),
        new("Accessories", null, "Peripherals and add-ons"),
        new("Keyboard", "Accessories", "Mechanical and membrane keyboards"),
        new("Mouse", "Accessories", "Gaming and office mice"),
        new("Headphone", "Accessories", "Headsets and headphones"),
        new("Webcam", "Accessories", "Webcams for streaming and meetings"),
    ];

    public static readonly BrandSeed[] Brands =
    [
        new("Intel", "Processors and platform technology"),
        new("AMD", "Ryzen processors and Radeon graphics"),
        new("ASUS", "Motherboards, graphics cards, laptops and monitors"),
        new("MSI", "Gaming motherboards, graphics cards and laptops"),
        new("Gigabyte", "Motherboards, graphics cards, PSUs and cases"),
        new("Corsair", "Memory, power supplies, cases and peripherals"),
        new("Kingston", "Memory and storage"),
        new("Samsung", "SSDs and monitors"),
        new("Western Digital", "Storage"),
        new("Crucial", "Memory and SSDs"),
        new("Cooler Master", "Cases, power supplies and cooling"),
        new("Antec", "Cases and power supplies"),
        new("Lenovo", "Laptops and desktops"),
        new("HP", "Laptops and desktops"),
        new("Dell", "Laptops and monitors"),
        new("Acer", "Laptops and monitors"),
        new("LG", "Monitors"),
        new("APC", "UPS and power protection"),
        new("Logitech", "Mice, keyboards, webcams and headsets"),
        new("Redragon", "Gaming peripherals"),
        new("TechBazar", "TechBazar BD in-house custom-built PCs"),
        new("Deepcool", "CPU air and liquid coolers, cases"),
        new("ID-Cooling", "CPU coolers"),
    ];

    public static readonly ProductSeed[] Products =
    [
        // ---------------- Processors ----------------
        new("Processor", "Intel", "Intel Core i3-12100F 12th Gen Processor", 8200, null, 36,
            "4 cores / 8 threads|Up to 4.3 GHz turbo|Great entry-level gaming CPU|Cooler included in box",
            """
            General|Series=Core i3;Generation=12th Gen (Alder Lake);Socket=LGA1700
            Cores & Threads|Cores=4;Threads=8;Base Clock=3.3 GHz;Max Turbo=4.3 GHz
            Cache|L3 Cache=12 MB
            Memory|Memory Support=DDR4-3200 / DDR5-4800;Integrated Graphics=None (F-series)
            Power|TDP=58 W
            """),
        new("Processor", "Intel", "Intel Core i5-12400F 12th Gen Processor", 14500, 13800, 36,
            "6 cores / 12 threads|Up to 4.4 GHz turbo|Best value gaming CPU|Cooler included in box",
            """
            General|Series=Core i5;Generation=12th Gen (Alder Lake);Socket=LGA1700
            Cores & Threads|Cores=6;Threads=12;Base Clock=2.5 GHz;Max Turbo=4.4 GHz
            Cache|L3 Cache=18 MB
            Memory|Memory Support=DDR4-3200 / DDR5-4800;Integrated Graphics=None (F-series)
            Power|TDP=65 W
            """, Featured: true),
        new("Processor", "Intel", "Intel Core i5-14600K 14th Gen Processor", 38500, null, 36,
            "14 cores / 20 threads|Up to 5.3 GHz turbo|Unlocked for overclocking|Integrated UHD 770 graphics",
            """
            General|Series=Core i5;Generation=14th Gen (Raptor Lake Refresh);Socket=LGA1700
            Cores & Threads|Cores=14;Threads=20;Base Clock=3.5 GHz;Max Turbo=5.3 GHz
            Cache|L3 Cache=24 MB
            Memory|Memory Support=DDR4-3200 / DDR5-5600;Integrated Graphics=Intel UHD 770
            Power|TDP=125 W
            """),
        new("Processor", "Intel", "Intel Core i7-14700K 14th Gen Processor", 56000, 54500, 36,
            "20 cores / 28 threads|Up to 5.6 GHz turbo|Unlocked multiplier|Ideal for streaming and editing",
            """
            General|Series=Core i7;Generation=14th Gen (Raptor Lake Refresh);Socket=LGA1700
            Cores & Threads|Cores=20;Threads=28;Base Clock=3.4 GHz;Max Turbo=5.6 GHz
            Cache|L3 Cache=33 MB
            Memory|Memory Support=DDR4-3200 / DDR5-5600;Integrated Graphics=Intel UHD 770
            Power|TDP=125 W
            """, Featured: true),
        new("Processor", "AMD", "AMD Ryzen 5 5600 Processor", 12800, 11900, 36,
            "6 cores / 12 threads|Up to 4.4 GHz boost|AM4 platform, DDR4|Wraith Stealth cooler included",
            """
            General|Series=Ryzen 5;Generation=5000 Series (Zen 3);Socket=AM4
            Cores & Threads|Cores=6;Threads=12;Base Clock=3.5 GHz;Max Boost=4.4 GHz
            Cache|L3 Cache=32 MB
            Memory|Memory Support=DDR4-3200;Integrated Graphics=None
            Power|TDP=65 W
            """, Featured: true),
        new("Processor", "AMD", "AMD Ryzen 5 7600 Processor", 25500, null, 36,
            "6 cores / 12 threads|Up to 5.1 GHz boost|AM5 platform, DDR5|Integrated Radeon graphics",
            """
            General|Series=Ryzen 5;Generation=7000 Series (Zen 4);Socket=AM5
            Cores & Threads|Cores=6;Threads=12;Base Clock=3.8 GHz;Max Boost=5.1 GHz
            Cache|L3 Cache=32 MB
            Memory|Memory Support=DDR5-5200;Integrated Graphics=AMD Radeon Graphics
            Power|TDP=65 W
            """),
        new("Processor", "AMD", "AMD Ryzen 7 7800X3D Processor", 58000, null, 36,
            "8 cores / 16 threads|96 MB 3D V-Cache|Top-tier gaming performance|AM5 platform, DDR5",
            """
            General|Series=Ryzen 7;Generation=7000 Series (Zen 4 3D V-Cache);Socket=AM5
            Cores & Threads|Cores=8;Threads=16;Base Clock=4.2 GHz;Max Boost=5.0 GHz
            Cache|L3 Cache=96 MB
            Memory|Memory Support=DDR5-5200;Integrated Graphics=AMD Radeon Graphics
            Power|TDP=120 W
            """, StockStatus.PreOrder),

        // ---------------- Motherboards ----------------
        new("Motherboard", "MSI", "MSI PRO B660M-A DDR4 Motherboard", 14800, null, 36,
            "LGA1700 for 12th/13th/14th Gen|DDR4 up to 4800 MHz (OC)|PCIe 4.0 x16 and M.2 slot|Micro-ATX",
            """
            General|Chipset=Intel B660;Socket=LGA1700;Form Factor=Micro-ATX
            Memory|RAM Type=DDR4;Memory Slots=4;Max Memory=128 GB
            Storage|M.2 Slots=2;SATA Ports=4
            Expansion|PCIe x16=PCIe 4.0
            """),
        new("Motherboard", "ASUS", "ASUS PRIME B760M-K D4 Motherboard", 15500, 14900, 36,
            "LGA1700 for 12th/13th/14th Gen|DDR4 support|PCIe 4.0 and dual M.2|Micro-ATX",
            """
            General|Chipset=Intel B760;Socket=LGA1700;Form Factor=Micro-ATX
            Memory|RAM Type=DDR4;Memory Slots=2;Max Memory=64 GB
            Storage|M.2 Slots=2;SATA Ports=4
            Expansion|PCIe x16=PCIe 4.0
            """),
        new("Motherboard", "Gigabyte", "Gigabyte B760M GAMING X AX DDR5 Motherboard", 21500, null, 36,
            "LGA1700 with DDR5 support|Wi-Fi 6E onboard|PCIe 4.0 x16|Micro-ATX",
            """
            General|Chipset=Intel B760;Socket=LGA1700;Form Factor=Micro-ATX
            Memory|RAM Type=DDR5;Memory Slots=4;Max Memory=192 GB
            Storage|M.2 Slots=3;SATA Ports=4
            Network|Wi-Fi=Wi-Fi 6E;LAN=2.5 GbE
            """, Featured: true),
        new("Motherboard", "Gigabyte", "Gigabyte Z790 AORUS ELITE AX Motherboard", 38500, null, 36,
            "Overclocking-ready Z790|DDR5 up to 7600 MHz (OC)|PCIe 5.0 x16|ATX with Wi-Fi 6E",
            """
            General|Chipset=Intel Z790;Socket=LGA1700;Form Factor=ATX
            Memory|RAM Type=DDR5;Memory Slots=4;Max Memory=192 GB
            Storage|M.2 Slots=4;SATA Ports=6
            Network|Wi-Fi=Wi-Fi 6E;LAN=2.5 GbE
            """),
        new("Motherboard", "Gigabyte", "Gigabyte B550M DS3H AM4 Motherboard", 11900, null, 36,
            "AM4 for Ryzen 3000/5000|DDR4 up to 4733 MHz (OC)|PCIe 4.0 x16|Micro-ATX",
            """
            General|Chipset=AMD B550;Socket=AM4;Form Factor=Micro-ATX
            Memory|RAM Type=DDR4;Memory Slots=4;Max Memory=128 GB
            Storage|M.2 Slots=2;SATA Ports=4
            """),
        new("Motherboard", "MSI", "MSI PRO B650M-P AM5 Motherboard", 16900, null, 36,
            "AM5 for Ryzen 7000 series|DDR5 memory|PCIe 4.0 x16|Micro-ATX",
            """
            General|Chipset=AMD B650;Socket=AM5;Form Factor=Micro-ATX
            Memory|RAM Type=DDR5;Memory Slots=4;Max Memory=192 GB
            Storage|M.2 Slots=2;SATA Ports=4
            """),
        new("Motherboard", "ASUS", "ASUS TUF GAMING B650-PLUS WIFI Motherboard", 29500, 28500, 36,
            "AM5 with Wi-Fi 6|DDR5 up to 6400 MHz (OC)|PCIe 5.0 M.2 support|ATX",
            """
            General|Chipset=AMD B650;Socket=AM5;Form Factor=ATX
            Memory|RAM Type=DDR5;Memory Slots=4;Max Memory=192 GB
            Storage|M.2 Slots=3;SATA Ports=4
            Network|Wi-Fi=Wi-Fi 6;LAN=2.5 GbE
            """),

        // ---------------- RAM ----------------
        new("RAM", "Kingston", "Kingston FURY Beast 8GB DDR4 3200MHz Desktop RAM", 2600, null, 120,
            "8GB single module|3200 MHz CL16|Plug N Play XMP ready|Lifetime warranty",
            """
            Memory|RAM Type=DDR4;Capacity=8 GB;Speed=3200 MHz;Form Factor=DIMM (Desktop);Modules=1 x 8 GB
            Timing|CAS Latency=CL16;Voltage=1.35 V
            """),
        new("RAM", "Corsair", "Corsair Vengeance LPX 16GB (2x8GB) DDR4 3200MHz Desktop RAM", 5200, null, 120,
            "Dual-channel 2x8GB kit|Low-profile heatspreader|XMP 2.0|Lifetime warranty",
            """
            Memory|RAM Type=DDR4;Capacity=16 GB;Speed=3200 MHz;Form Factor=DIMM (Desktop);Modules=2 x 8 GB
            Timing|CAS Latency=CL16;Voltage=1.35 V
            """, Featured: true),
        new("RAM", "Kingston", "Kingston FURY Beast 16GB DDR5 5600MHz Desktop RAM", 6300, 5900, 120,
            "DDR5 5600 MHz|Intel XMP 3.0 and AMD EXPO ready|Low-profile heat spreader|Lifetime warranty",
            """
            Memory|RAM Type=DDR5;Capacity=16 GB;Speed=5600 MHz;Form Factor=DIMM (Desktop);Modules=1 x 16 GB
            Timing|CAS Latency=CL40;Voltage=1.25 V
            """),
        new("RAM", "Corsair", "Corsair Vengeance 32GB (2x16GB) DDR5 6000MHz Desktop RAM", 12800, null, 120,
            "32GB dual-channel kit|6000 MHz CL36|Optimised for Intel and AMD DDR5 platforms|Lifetime warranty",
            """
            Memory|RAM Type=DDR5;Capacity=32 GB;Speed=6000 MHz;Form Factor=DIMM (Desktop);Modules=2 x 16 GB
            Timing|CAS Latency=CL36;Voltage=1.35 V
            """, Featured: true),
        new("RAM", "Crucial", "Crucial 16GB DDR4 3200MHz Laptop RAM", 4500, null, 120,
            "SO-DIMM for laptops|3200 MHz CL22|Easy plug-in upgrade|Lifetime warranty",
            """
            Memory|RAM Type=DDR4;Capacity=16 GB;Speed=3200 MHz;Form Factor=SO-DIMM (Laptop);Modules=1 x 16 GB
            Timing|CAS Latency=CL22;Voltage=1.2 V
            """),

        // ---------------- SSD ----------------
        new("SSD", "Samsung", "Samsung 980 500GB M.2 NVMe SSD", 5300, null, 60,
            "Up to 3,100 MB/s read|PCIe 3.0 x4 NVMe 1.4|Reliable Samsung controller|5 years warranty",
            """
            General|Capacity=500 GB;Interface=NVMe PCIe 3.0 x4;Form Factor=M.2 2280
            Performance|Read Speed=3100 MB/s;Write Speed=2600 MB/s
            """),
        new("SSD", "Western Digital", "WD Blue SN580 1TB M.2 NVMe Gen4 SSD", 7600, null, 60,
            "Up to 4,150 MB/s read|PCIe 4.0 NVMe|Great price per GB|5 years warranty",
            """
            General|Capacity=1 TB;Interface=NVMe PCIe 4.0 x4;Form Factor=M.2 2280
            Performance|Read Speed=4150 MB/s;Write Speed=4150 MB/s
            """, Featured: true),
        new("SSD", "Kingston", "Kingston NV2 1TB M.2 NVMe SSD", 6600, 6200, 36,
            "Up to 3,500 MB/s read|PCIe 4.0 x4 NVMe|Slim M.2 2280|3 years warranty",
            """
            General|Capacity=1 TB;Interface=NVMe PCIe 4.0 x4;Form Factor=M.2 2280
            Performance|Read Speed=3500 MB/s;Write Speed=2100 MB/s
            """),
        new("SSD", "Samsung", "Samsung 990 PRO 2TB M.2 NVMe Gen4 SSD", 21500, null, 60,
            "Up to 7,450 MB/s read|PCIe 4.0 flagship|Excellent for gaming and editing|5 years warranty",
            """
            General|Capacity=2 TB;Interface=NVMe PCIe 4.0 x4;Form Factor=M.2 2280
            Performance|Read Speed=7450 MB/s;Write Speed=6900 MB/s
            """, StockStatus.UpComing),
        new("SSD", "Crucial", "Crucial MX500 500GB 2.5\" SATA SSD", 5000, null, 60,
            "Up to 560 MB/s read|Reliable SATA III SSD|Ideal laptop/desktop upgrade|5 years warranty",
            """
            General|Capacity=500 GB;Interface=SATA III;Form Factor=2.5 inch
            Performance|Read Speed=560 MB/s;Write Speed=510 MB/s
            """),

        // ---------------- Graphics cards ----------------
        new("Graphics Card", "Gigabyte", "Gigabyte GeForce GTX 1650 D6 OC 4GB Graphics Card", 18500, null, 36,
            "4GB GDDR6|Low 75 W power draw, no extra power cable|Great for esports titles|Compact design",
            """
            GPU|GPU Chipset=NVIDIA GeForce GTX 1650;Video Memory=4 GB;Memory Type=GDDR6
            Power|TDP=75 W;Recommended PSU=300 W;Power Connector=None (PCIe slot)
            Dimensions|Length=170 mm
            Outputs|Ports=HDMI, DisplayPort, DVI-D
            """),
        new("Graphics Card", "ASUS", "ASUS Dual GeForce RTX 4060 OC Edition 8GB Graphics Card", 42500, null, 36,
            "8GB GDDR6|DLSS 3 and ray tracing|Axial-tech fans|Excellent 1080p performance",
            """
            GPU|GPU Chipset=NVIDIA GeForce RTX 4060;Video Memory=8 GB;Memory Type=GDDR6
            Power|TDP=115 W;Recommended PSU=550 W;Power Connector=1 x 8-pin
            Dimensions|Length=227 mm
            Outputs|Ports=HDMI 2.1, 3 x DisplayPort 1.4a
            """, Featured: true),
        new("Graphics Card", "MSI", "MSI GeForce RTX 4060 Ti VENTUS 2X BLACK 8GB Graphics Card", 55000, null, 36,
            "8GB GDDR6|DLSS 3 frame generation|Dual-fan thermal design|Strong 1080p/1440p card",
            """
            GPU|GPU Chipset=NVIDIA GeForce RTX 4060 Ti;Video Memory=8 GB;Memory Type=GDDR6
            Power|TDP=160 W;Recommended PSU=550 W;Power Connector=1 x 8-pin
            Dimensions|Length=199 mm
            Outputs|Ports=HDMI 2.1, 3 x DisplayPort 1.4a
            """),
        new("Graphics Card", "Gigabyte", "Gigabyte GeForce RTX 4070 SUPER WINDFORCE OC 12GB Graphics Card", 88500, 86500, 36,
            "12GB GDDR6X|DLSS 3 and ray tracing|WINDFORCE 3X cooling|Ideal for 1440p high refresh",
            """
            GPU|GPU Chipset=NVIDIA GeForce RTX 4070 SUPER;Video Memory=12 GB;Memory Type=GDDR6X
            Power|TDP=220 W;Recommended PSU=650 W;Power Connector=1 x 16-pin (adapter included)
            Dimensions|Length=261 mm
            Outputs|Ports=HDMI 2.1, 3 x DisplayPort 1.4a
            """, Featured: true),
        new("Graphics Card", "MSI", "MSI Radeon RX 7600 MECH 2X CLASSIC 8GB Graphics Card", 33500, null, 36,
            "8GB GDDR6|AMD FSR 3 support|Dual-fan cooling|Solid 1080p gaming",
            """
            GPU|GPU Chipset=AMD Radeon RX 7600;Video Memory=8 GB;Memory Type=GDDR6
            Power|TDP=165 W;Recommended PSU=550 W;Power Connector=1 x 8-pin
            Dimensions|Length=235 mm
            Outputs|Ports=HDMI 2.1, 3 x DisplayPort 1.4a
            """),
        new("Graphics Card", "ASUS", "ASUS TUF Gaming Radeon RX 7800 XT OC 16GB Graphics Card", 78500, null, 36,
            "16GB GDDR6|Excellent 1440p performance|Triple-fan TUF cooling|AMD FSR 3 support",
            """
            GPU|GPU Chipset=AMD Radeon RX 7800 XT;Video Memory=16 GB;Memory Type=GDDR6
            Power|TDP=263 W;Recommended PSU=700 W;Power Connector=2 x 8-pin
            Dimensions|Length=305 mm
            Outputs|Ports=HDMI 2.1, 3 x DisplayPort 2.1
            """, StockStatus.OutOfStock),

        // ---------------- Power supplies ----------------
        new("Power Supply", "Corsair", "Corsair CV550 550W 80 Plus Bronze Power Supply", 5800, null, 36,
            "550W continuous output|80 PLUS Bronze|Compact design|Quiet 120 mm fan",
            """
            General|Wattage=550 W;Efficiency=80 PLUS Bronze;Modular=Non-modular;Form Factor=ATX
            Connectors|PCIe Connectors=2 x 6+2-pin;SATA Connectors=6
            """),
        new("Power Supply", "Cooler Master", "Cooler Master MWE 650 Bronze V2 Power Supply", 7900, null, 60,
            "650W|80 PLUS Bronze|120 mm silent fan|Wide compatibility",
            """
            General|Wattage=650 W;Efficiency=80 PLUS Bronze;Modular=Non-modular;Form Factor=ATX
            Connectors|PCIe Connectors=2 x 6+2-pin;SATA Connectors=6
            """, Featured: true),
        new("Power Supply", "Gigabyte", "Gigabyte P750GM 750W 80 Plus Gold Modular Power Supply", 11900, 11500, 60,
            "750W|80 PLUS Gold|Fully modular cables|Ready for RTX 40-series with 12VHPWR adapter",
            """
            General|Wattage=750 W;Efficiency=80 PLUS Gold;Modular=Fully modular;Form Factor=ATX
            Connectors|PCIe Connectors=4 x 6+2-pin;SATA Connectors=8
            """),
        new("Power Supply", "Corsair", "Corsair RM850e 850W 80 Plus Gold Fully Modular Power Supply", 17800, null, 84,
            "850W|80 PLUS Gold|ATX 3.0 and PCIe 5.0 ready|Zero-RPM fan mode",
            """
            General|Wattage=850 W;Efficiency=80 PLUS Gold;Modular=Fully modular;Form Factor=ATX
            Connectors|PCIe Connectors=4 x 6+2-pin;SATA Connectors=10
            """),

        // ---------------- Casing ----------------
        new("Casing", "Cooler Master", "Cooler Master MasterBox Q300L Micro-ATX Casing", 4500, null, 12,
            "Compact Micro-ATX tower|Magnetic dust filters|Acrylic side panel|Supports 360 mm GPU",
            """
            General|Form Factor=Micro-ATX Tower;Supported Motherboards=Micro-ATX, Mini-ITX;Side Panel=Acrylic
            Clearance|Max GPU Length=360 mm;Max CPU Cooler Height=159 mm
            """),
        new("Casing", "Corsair", "Corsair 4000D Airflow Mid-Tower ATX Casing", 11500, null, 24,
            "High-airflow front panel|Tempered glass side panel|Two 120 mm fans included|Easy cable management",
            """
            General|Form Factor=ATX Mid Tower;Supported Motherboards=ATX, Micro-ATX, Mini-ITX;Side Panel=Tempered glass
            Clearance|Max GPU Length=360 mm;Max CPU Cooler Height=170 mm
            """, Featured: true),
        new("Casing", "Antec", "Antec NX410 ARGB ATX Gaming Casing", 6800, null, 12,
            "Tempered glass front and side|Pre-installed ARGB fans|ATX mid tower|Great airflow",
            """
            General|Form Factor=ATX Mid Tower;Supported Motherboards=ATX, Micro-ATX, Mini-ITX;Side Panel=Tempered glass
            Clearance|Max GPU Length=330 mm;Max CPU Cooler Height=160 mm
            """),
        new("Casing", "Gigabyte", "Gigabyte C200 Glass ATX Casing", 6200, 5900, 12,
            "Tempered glass side panel|Supports up to 6 fans|ATX mid tower|Front I/O with USB 3.0",
            """
            General|Form Factor=ATX Mid Tower;Supported Motherboards=ATX, Micro-ATX, Mini-ITX;Side Panel=Tempered glass
            Clearance|Max GPU Length=350 mm;Max CPU Cooler Height=165 mm
            """),

        // ---------------- Monitors ----------------
        new("Monitor", "LG", "LG 24MP400-B 23.8\" Full HD IPS Monitor", 13500, null, 36,
            "Full HD IPS panel|75 Hz refresh rate|AMD FreeSync|3-side borderless",
            """
            Display|Screen Size=24 inch;Resolution=1920 x 1080 (FHD);Panel Type=IPS;Refresh Rate=75 Hz;Response Time=5 ms
            Connectivity|Ports=1 x HDMI, 1 x D-Sub
            """, Featured: true),
        new("Monitor", "Samsung", "Samsung Odyssey G3 27\" 165Hz Full HD Gaming Monitor", 24800, 23500, 36,
            "165 Hz refresh rate|1 ms response|VA panel with FreeSync Premium|Height adjustable stand",
            """
            Display|Screen Size=27 inch;Resolution=1920 x 1080 (FHD);Panel Type=VA;Refresh Rate=165 Hz;Response Time=1 ms
            Connectivity|Ports=1 x HDMI, 1 x DisplayPort
            """),
        new("Monitor", "ASUS", "ASUS TUF Gaming VG27AQ 27\" QHD 165Hz IPS Monitor", 36500, null, 36,
            "2560 x 1440 IPS|165 Hz with ELMB Sync|G-SYNC Compatible|HDR10",
            """
            Display|Screen Size=27 inch;Resolution=2560 x 1440 (QHD);Panel Type=IPS;Refresh Rate=165 Hz;Response Time=1 ms
            Connectivity|Ports=2 x HDMI, 1 x DisplayPort
            """, Featured: true),
        new("Monitor", "Dell", "Dell P2422H 23.8\" Full HD IPS Professional Monitor", 17800, null, 36,
            "Full HD IPS|Height, tilt, swivel and pivot stand|Built-in USB hub|Comfortable for office work",
            """
            Display|Screen Size=24 inch;Resolution=1920 x 1080 (FHD);Panel Type=IPS;Refresh Rate=60 Hz;Response Time=5 ms
            Connectivity|Ports=1 x HDMI, 1 x DisplayPort, 1 x VGA
            """),
        new("Monitor", "Acer", "Acer Nitro VG240Y S3 23.8\" 180Hz IPS Gaming Monitor", 14500, null, 36,
            "180 Hz IPS|1 ms VRB response|FreeSync Premium|Zero-frame design",
            """
            Display|Screen Size=24 inch;Resolution=1920 x 1080 (FHD);Panel Type=IPS;Refresh Rate=180 Hz;Response Time=1 ms
            Connectivity|Ports=2 x HDMI, 1 x DisplayPort
            """),

        // ---------------- UPS ----------------
        new("UPS", "APC", "APC Back-UPS BX650LI-MS 650VA UPS", 5600, null, 24,
            "650VA / 325W|Automatic voltage regulation|Suitable for a basic desktop|USB-less compact design",
            """
            Power|Capacity=650 VA;Output Power=325 W;Input Voltage=230 V
            General|Outlets=4;Battery Type=Sealed lead-acid;Form Factor=Tower
            """),
        new("UPS", "APC", "APC Back-UPS BX1100LI-MS 1100VA UPS", 11500, null, 24,
            "1100VA / 550W|AVR for unstable mains|6 outlets|Ideal for gaming PCs",
            """
            Power|Capacity=1100 VA;Output Power=550 W;Input Voltage=230 V
            General|Outlets=6;Battery Type=Sealed lead-acid;Form Factor=Tower
            """, Featured: true),
        new("UPS", "APC", "APC Back-UPS Pro BR1600SI 1600VA UPS", 28900, null, 24,
            "1600VA / 960W|Pure-sine-wave friendly AVR|LCD status display|Power-hungry workstations",
            """
            Power|Capacity=1600 VA;Output Power=960 W;Input Voltage=230 V
            General|Outlets=8;Battery Type=Sealed lead-acid;Form Factor=Tower
            """),

        // ---------------- Gaming laptops ----------------
        new("Gaming Laptop", "Acer", "Acer Nitro V 15 ANV15-51 Core i5-13420H RTX 4050 Gaming Laptop", 118000, null, 24,
            "Core i5-13420H, 16GB DDR5|NVIDIA RTX 4050 6GB|15.6\" FHD 144Hz IPS|512GB NVMe SSD",
            """
            Processor|Series=Intel Core i5;Model=Core i5-13420H;Cores=8
            Memory|RAM Type=DDR5;Capacity=16 GB
            Storage|Storage Capacity=512 GB;Storage Type=NVMe SSD
            Graphics|GPU Chipset=NVIDIA GeForce RTX 4050;Video Memory=6 GB
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=144 Hz;Panel Type=IPS
            Power|TDP=95 W
            """, Featured: true),
        new("Gaming Laptop", "MSI", "MSI Thin GF63 12UCX Core i5-12450H RTX 2050 Gaming Laptop", 85500, 83900, 24,
            "Core i5-12450H, 8GB DDR4|NVIDIA RTX 2050 4GB|15.6\" FHD 144Hz|512GB NVMe SSD",
            """
            Processor|Series=Intel Core i5;Model=Core i5-12450H;Cores=8
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=512 GB;Storage Type=NVMe SSD
            Graphics|GPU Chipset=NVIDIA GeForce RTX 2050;Video Memory=4 GB
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=144 Hz;Panel Type=IPS
            Power|TDP=75 W
            """),
        new("Gaming Laptop", "ASUS", "ASUS TUF Gaming A15 Ryzen 5 7535HS RTX 2050 Gaming Laptop", 92000, null, 24,
            "Ryzen 5 7535HS, 16GB DDR5|NVIDIA RTX 2050 4GB|15.6\" FHD 144Hz|512GB NVMe SSD",
            """
            Processor|Series=AMD Ryzen 5;Model=Ryzen 5 7535HS;Cores=6
            Memory|RAM Type=DDR5;Capacity=16 GB
            Storage|Storage Capacity=512 GB;Storage Type=NVMe SSD
            Graphics|GPU Chipset=NVIDIA GeForce RTX 2050;Video Memory=4 GB
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=144 Hz;Panel Type=IPS
            Power|TDP=80 W
            """),

        // ---------------- Everyday laptops ----------------
        new("Everyday Laptop", "Lenovo", "Lenovo IdeaPad Slim 3 15IAH8 Core i5-12450H Laptop", 71000, null, 24,
            "Core i5-12450H, 8GB DDR4|15.6\" FHD IPS|512GB NVMe SSD|Slim and light",
            """
            Processor|Series=Intel Core i5;Model=Core i5-12450H;Cores=8
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=512 GB;Storage Type=NVMe SSD
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=60 Hz;Panel Type=IPS
            """, Featured: true),
        new("Everyday Laptop", "HP", "HP 15-fd Core i5-1335U 15.6\" Laptop", 68500, null, 24,
            "Core i5-1335U, 8GB DDR4|15.6\" FHD anti-glare|512GB NVMe SSD|Windows 11",
            """
            Processor|Series=Intel Core i5;Model=Core i5-1335U;Cores=10
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=512 GB;Storage Type=NVMe SSD
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=60 Hz;Panel Type=IPS
            """),
        new("Everyday Laptop", "ASUS", "ASUS Vivobook 15 X1504 Core i3-1215U Laptop", 49500, 47900, 24,
            "Core i3-1215U, 8GB DDR4|15.6\" FHD|256GB NVMe SSD|Fingerprint-free lightweight body",
            """
            Processor|Series=Intel Core i3;Model=Core i3-1215U;Cores=6
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=256 GB;Storage Type=NVMe SSD
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=60 Hz;Panel Type=IPS
            """),
        new("Everyday Laptop", "Dell", "Dell Inspiron 15 3520 Core i5-1235U Laptop", 72500, null, 24,
            "Core i5-1235U, 8GB DDR4|15.6\" FHD 120Hz|512GB NVMe SSD|Backlit keyboard",
            """
            Processor|Series=Intel Core i5;Model=Core i5-1235U;Cores=10
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=512 GB;Storage Type=NVMe SSD
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=120 Hz;Panel Type=WVA
            """),
        new("Everyday Laptop", "Acer", "Acer Aspire 5 A515-57 Core i5-12450H Laptop", 66500, null, 24,
            "Core i5-12450H, 8GB DDR4|15.6\" FHD IPS|512GB NVMe SSD|Metal-finish lid",
            """
            Processor|Series=Intel Core i5;Model=Core i5-12450H;Cores=8
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=512 GB;Storage Type=NVMe SSD
            Display|Screen Size=15.6 inch;Resolution=1920 x 1080 (FHD);Refresh Rate=60 Hz;Panel Type=IPS
            """),

        // ---------------- Desktops ----------------
        new("Gaming PC", "TechBazar", "TechBazar Gaming PC - Ryzen 5 5600 / RTX 4060 / 16GB / 1TB", 92500, 89900, 24,
            "Ryzen 5 5600 + RTX 4060 8GB|16GB DDR4 + 1TB NVMe|650W 80+ Bronze PSU|Tested and assembled by TechBazar BD",
            """
            Processor|Model=AMD Ryzen 5 5600;Socket=AM4;Cores=6
            Memory|RAM Type=DDR4;Capacity=16 GB
            Storage|Storage Capacity=1 TB;Storage Type=NVMe SSD
            Graphics|GPU Chipset=NVIDIA GeForce RTX 4060;Video Memory=8 GB
            Power|Wattage=650 W;Efficiency=80 PLUS Bronze
            """, Featured: true),
        new("Gaming PC", "TechBazar", "TechBazar Gaming PC - Core i5-14600K / RTX 4070 SUPER / 32GB / 1TB", 195000, null, 24,
            "Core i5-14600K + RTX 4070 SUPER 12GB|32GB DDR5 + 1TB NVMe Gen4|750W 80+ Gold PSU|Tempered glass airflow case",
            """
            Processor|Model=Intel Core i5-14600K;Socket=LGA1700;Cores=14
            Memory|RAM Type=DDR5;Capacity=32 GB
            Storage|Storage Capacity=1 TB;Storage Type=NVMe SSD
            Graphics|GPU Chipset=NVIDIA GeForce RTX 4070 SUPER;Video Memory=12 GB
            Power|Wattage=750 W;Efficiency=80 PLUS Gold
            """),
        new("Office PC", "HP", "HP ProDesk 400 G9 SFF Core i5-12500 Desktop", 78500, null, 36,
            "Core i5-12500, 8GB DDR4|256GB NVMe SSD|Small form factor|Wired keyboard and mouse included",
            """
            Processor|Model=Intel Core i5-12500;Socket=LGA1700;Cores=6
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=256 GB;Storage Type=NVMe SSD
            Power|Wattage=180 W;Efficiency=80 PLUS Platinum
            """),
        new("Office PC", "TechBazar", "TechBazar Office PC - Core i3-12100 / 8GB / 256GB", 38500, null, 24,
            "Core i3-12100, 8GB DDR4|256GB NVMe SSD|Quiet and energy efficient|Ready with Windows-compatible drivers",
            """
            Processor|Model=Intel Core i3-12100;Socket=LGA1700;Cores=4
            Memory|RAM Type=DDR4;Capacity=8 GB
            Storage|Storage Capacity=256 GB;Storage Type=NVMe SSD
            Power|Wattage=450 W;Efficiency=80 PLUS Bronze
            """),

        // ---------------- Accessories ----------------
        new("Keyboard", "Redragon", "Redragon K552 Kumara RGB Mechanical Gaming Keyboard", 3400, null, 12,
            "Tenkeyless layout|Outemu blue mechanical switches|RGB backlight|Metal-reinforced body",
            """
            General|Layout=Tenkeyless (87 keys);Switch Type=Mechanical (Outemu Blue);Connectivity=Wired USB
            Features|Backlight=RGB;Anti-ghosting=N-key rollover
            """),
        new("Keyboard", "Logitech", "Logitech MK270 Wireless Keyboard and Mouse Combo", 2950, null, 36,
            "2.4 GHz wireless|Long battery life|Spill-resistant keyboard|Plug-and-play receiver",
            """
            General|Layout=Full size;Switch Type=Membrane;Connectivity=Wireless 2.4 GHz
            Features|Includes=Wireless mouse;Battery Life=36 months (keyboard)
            """),
        new("Mouse", "Logitech", "Logitech G102 LIGHTSYNC RGB Gaming Mouse", 1750, null, 24,
            "8,000 DPI sensor|LIGHTSYNC RGB|6 programmable buttons|Lightweight design",
            """
            General|Sensor=Optical (Mercury);Max DPI=8000;Buttons=6;Connectivity=Wired USB
            Features|Backlight=RGB LIGHTSYNC
            """, Featured: true),
        new("Mouse", "Logitech", "Logitech MX Master 3S Wireless Performance Mouse", 11500, null, 12,
            "8,000 DPI Darkfield sensor|Quiet clicks|MagSpeed scroll wheel|USB-C quick charge",
            """
            General|Sensor=Optical (Darkfield);Max DPI=8000;Buttons=7;Connectivity=Bluetooth / Logi Bolt
            Features|Battery Life=70 days
            """),
        new("Headphone", "Redragon", "Redragon H510 Zeus X RGB Wired Gaming Headset", 4200, null, 12,
            "7.1 virtual surround|53 mm drivers|Detachable microphone|RGB lighting",
            """
            General|Type=Over-ear headset;Connectivity=Wired USB;Microphone=Detachable
            Audio|Driver Size=53 mm;Surround=Virtual 7.1
            """),
        new("Headphone", "Logitech", "Logitech G435 LIGHTSPEED Wireless Gaming Headset", 7800, 7400, 24,
            "Ultra-light 165 g|LIGHTSPEED and Bluetooth|Dual beamforming mics|Up to 18 hours battery",
            """
            General|Type=Over-ear headset;Connectivity=Wireless 2.4 GHz / Bluetooth;Microphone=Built-in dual beamforming
            Audio|Driver Size=40 mm;Battery Life=18 hours
            """),
        new("Webcam", "Logitech", "Logitech C920 HD Pro Webcam", 7500, null, 24,
            "Full HD 1080p at 30 fps|Dual stereo microphones|Autofocus|Works with all major apps",
            """
            General|Resolution=1920 x 1080 (FHD);Frame Rate=30 fps;Field of View=78 degrees
            Features|Microphone=Dual stereo;Connectivity=Wired USB
            """),
        new("Webcam", "Logitech", "Logitech C270 HD Webcam", 2950, null, 24,
            "HD 720p video calls|Built-in noise-reducing mic|Light correction|Plug-and-play",
            """
            General|Resolution=1280 x 720 (HD);Frame Rate=30 fps;Field of View=60 degrees
            Features|Microphone=Mono;Connectivity=Wired USB
            """),

        // ---------------- CPU coolers (appended last so earlier products keep their seeded SKUs / popularity) ----------------
        new("CPU Cooler", "Cooler Master", "Cooler Master Hyper 212 Black Edition CPU Air Cooler", 3900, null, 24,
            "Single tower, 4 heat pipes|120 mm PWM fan|Fits Intel LGA1700 and AMD AM4/AM5|Proven budget cooler",
            """
            General|Cooler Type=Air Cooler;Supported Sockets=LGA1700, LGA1200, AM4, AM5;Fan Size=120 mm
            Thermal|TDP Rating=150 W;Height=158 mm
            """),
        new("CPU Cooler", "Deepcool", "Deepcool AK400 CPU Air Cooler", 3500, 3300, 24,
            "4 copper heat pipes|120 mm FDB fan|Strong cooling for 65–125 W CPUs|LGA1700 and AM5 mounting included",
            """
            General|Cooler Type=Air Cooler;Supported Sockets=LGA1700, LGA1200, AM4, AM5;Fan Size=120 mm
            Thermal|TDP Rating=220 W;Height=155 mm
            """, Featured: true),
        new("CPU Cooler", "Deepcool", "Deepcool AK620 Dual-Tower CPU Air Cooler", 6800, null, 24,
            "Dual tower, 6 heat pipes|Two 120 mm fans|Handles high-end CPUs quietly|LGA1700 and AM5 ready",
            """
            General|Cooler Type=Air Cooler;Supported Sockets=LGA1700, LGA1200, AM4, AM5;Fan Size=2 x 120 mm
            Thermal|TDP Rating=260 W;Height=160 mm
            """),
        new("CPU Cooler", "ID-Cooling", "ID-Cooling SE-214-XT CPU Air Cooler", 2800, null, 12,
            "4 heat pipes|120 mm PWM fan|Compact and quiet|Low-cost option for mid-range CPUs",
            """
            General|Cooler Type=Air Cooler;Supported Sockets=LGA1700, LGA1200, AM4, AM5;Fan Size=120 mm
            Thermal|TDP Rating=180 W;Height=154 mm
            """),
        new("CPU Cooler", "Cooler Master", "Cooler Master MasterLiquid 240L Core ARGB AIO Liquid Cooler", 9800, 9400, 24,
            "240 mm radiator|Two ARGB fans|Low-noise pump|LGA1700, AM4 and AM5 brackets",
            """
            General|Cooler Type=Liquid Cooler (240 mm AIO);Supported Sockets=LGA1700, LGA1200, AM4, AM5;Fan Size=2 x 120 mm
            Thermal|TDP Rating=250 W;Height=52 mm
            """),
    ];

    public static readonly (string Code, string Description, DiscountType Type, decimal Value, decimal? Min, decimal? Max, int? Limit, int ExpiresInDays)[] Coupons =
    [
        ("WELCOME10", "10% off your first order (max ৳1,000)", DiscountType.Percentage, 10, 2000, 1000, 5000, 365),
        ("EID500", "৳500 off orders above ৳10,000", DiscountType.FixedAmount, 500, 10000, null, 1000, 90),
    ];
}
