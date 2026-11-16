// The demo company: every department and every employee the seeder inserts
// into an empty database. This file is pure DATA - no database code lives
// here at all, that is DatabaseSeeder.cs's job. Keeping the two apart means
// you can edit the company roster below without touching any EF Core code.
//
// The org chart is modelled on a large tech company (think Google): a handful
// of product areas that build things, a set of central corporate functions,
// and the campus operations crew. The point of all this for IncidentFlow is
// routing and escalation:
//   - WHO owns an incident? When the production database falls over, that is
//     the backend engineers and SREs in Cloud / Core Systems - not the
//     accountants in Finance, even though both are "employees".
//   - WHO do you escalate it to? Every department has people at LEAD level
//     and above (tech leads, managers, directors, VPs and the C-suite), so
//     a serious outage always has a senior owner to hand it to.
using IncidentFlow.Api.Entities;

namespace IncidentFlow.Api.Data;

// Small throwaway "shapes" for the rows below. A record with no body is just
// a compact way to declare a read-only data holder.
public record DepartmentSeed(string Code, string Name, DepartmentKind Kind, string Description);

public record EmployeeSeed(
    string Name,
    string Email,
    string JobTitle,
    Seniority Seniority,
    EmploymentType EmploymentType,
    string DepartmentCode
);

public static class SeedData
{
    // Every seeded employee gets this same password, so you can log in as
    // any of them while developing: e.g. marcus.webb@example.com with the
    // password below. That is fine for a throwaway demo database and a
    // terrible idea in production - which is exactly why employee seeding
    // can be switched off with Seed__DemoEmployees=false (see Program.cs).
    public const string DemoPassword = "incidentflow123";

    // ---- The 14 departments --------------------------------------------
    public static readonly DepartmentSeed[] Departments =
    [
        // Product areas: the teams that actually build and run the products.
        new("KI", "Knowledge & Information", DepartmentKind.PRODUCT_AREA,
            "Search, Maps, News, Assistant and Geo products."),
        new("ADS", "Ads & Commerce", DepartmentKind.PRODUCT_AREA,
            "Google Ads, AdSense, AdMob, Shopping and enterprise advertising platforms."),
        new("CLOUD", "Google Cloud", DepartmentKind.PRODUCT_AREA,
            "GCP infrastructure, Workspace, enterprise AI and data analytics."),
        new("PLATFORMS", "Platforms & Devices", DepartmentKind.PRODUCT_AREA,
            "Android, Chrome, ChromeOS, Play, Pixel, Nest and Fitbit."),
        new("DEEPMIND", "Google DeepMind & Research", DepartmentKind.PRODUCT_AREA,
            "Foundation models, machine learning research, quantum computing and science AI."),
        new("YOUTUBE", "YouTube", DepartmentKind.PRODUCT_AREA,
            "Video streaming, YouTube Music, YouTube TV and creator monetization."),
        new("CORE", "Core Systems & Experiences", DepartmentKind.PRODUCT_AREA,
            "Developer infrastructure, internal tools and the shared foundations every other team runs on."),

        // Corporate functions: the central teams supporting the product areas.
        new("LEGAL", "Global Affairs & Legal", DepartmentKind.CORPORATE_FUNCTION,
            "Corporate law, IP, regulatory compliance, government relations and public policy."),
        new("FINANCE", "Finance & Corporate Development", DepartmentKind.CORPORATE_FUNCTION,
            "Accounting, FP&A, investor relations, tax, treasury and M&A."),
        new("PEOPLEOPS", "People Operations", DepartmentKind.CORPORATE_FUNCTION,
            "Recruiting, compensation and benefits, employee relations and learning & development."),
        new("MARKETING", "Global Marketing", DepartmentKind.CORPORATE_FUNCTION,
            "Brand strategy, product marketing, creative direction and consumer insights."),
        new("GBO", "Global Business Organization", DepartmentKind.CORPORATE_FUNCTION,
            "Global sales, strategic partnerships, customer support and business operations."),
        new("REWS", "Real Estate & Workplace Services", DepartmentKind.CORPORATE_FUNCTION,
            "Campus architecture, sustainability, transport, physical security and on-site amenities."),

        // Campus operations: the contracted crew who keep the buildings running.
        new("CAMPUS", "Campus Operations & Hospitality", DepartmentKind.CAMPUS_OPERATIONS,
            "Culinary, facilities, transport and on-site health teams staffing the campus day to day."),
    ];

    // ---- The 75 employees ------------------------------------------------
    // Grouped by department, and within each department listed from the most
    // senior person down, so the file reads like an org chart. Deliberately
    // lopsided towards engineering, and towards CLOUD and CORE in particular:
    // those are the teams an infrastructure incident actually lands on, so
    // there have to be enough of them - at every level - to hand work to.
    //
    // Job titles and seniority levels are kept consistent on purpose: if the
    // title says "Senior" the level is SENIOR, a "Tech Lead" is LEAD, and
    // the plain titles ("Backend Software Engineer") are MID. Note that
    // "Product Manager" and "Technical Program Manager" are NOT management
    // levels despite the word in the title - they are individual
    // contributors, so they sit at MID/SENIOR, not MANAGER.
    //
    // Each row links to its department by Code, and the seeder refuses to
    // start if a code here does not match a department above.
    public static readonly EmployeeSeed[] Employees =
    [
        // ---- Google Cloud (10) - the usual owners of a database/infra
        // incident, so this is the deepest bench in the company.
        new("Catherine Lemaire", "catherine.lemaire@example.com", "VP of Engineering, Google Cloud", Seniority.VP, EmploymentType.FTE, "CLOUD"),
        new("Olivia Brennan", "olivia.brennan@example.com", "Engineering Manager, Cloud Infrastructure", Seniority.MANAGER, EmploymentType.FTE, "CLOUD"),
        new("Evelyn Shaw", "evelyn.shaw@example.com", "Tech Lead, Cloud Databases", Seniority.LEAD, EmploymentType.FTE, "CLOUD"),
        new("Kenji Mori", "kenji.mori@example.com", "Senior Software Engineer", Seniority.SENIOR, EmploymentType.FTE, "CLOUD"),
        new("Daniel Okonkwo", "daniel.okonkwo@example.com", "Database Reliability Engineer", Seniority.MID, EmploymentType.FTE, "CLOUD"),
        new("Priya Raghavan", "priya.raghavan@example.com", "Site Reliability Engineer", Seniority.MID, EmploymentType.FTE, "CLOUD"),
        new("Marcus Webb", "marcus.webb@example.com", "Backend Software Engineer", Seniority.MID, EmploymentType.FTE, "CLOUD"),
        new("Aisha Bello", "aisha.bello@example.com", "Technical Program Manager", Seniority.MID, EmploymentType.FTE, "CLOUD"),
        new("Tomas Rivera", "tomas.rivera@example.com", "Product Manager", Seniority.MID, EmploymentType.FTE, "CLOUD"),
        new("Lena Fischer", "lena.fischer@example.com", "Cloud Sales Engineer", Seniority.MID, EmploymentType.FTE, "CLOUD"),

        // ---- Core Systems & Experiences (11) - shared infrastructure and
        // internal tools, and where the CTO sits.
        new("Sanjay Iyer", "sanjay.iyer@example.com", "Chief Technology Officer", Seniority.EXECUTIVE, EmploymentType.FTE, "CORE"),
        new("Charles Nwosu", "charles.nwosu@example.com", "Director of Engineering, Core Systems", Seniority.DIRECTOR, EmploymentType.FTE, "CORE"),
        new("Sophie Marchand", "sophie.marchand@example.com", "Engineering Manager, Developer Infrastructure", Seniority.MANAGER, EmploymentType.FTE, "CORE"),
        new("Abigail Turner", "abigail.turner@example.com", "Tech Lead, Storage Infrastructure", Seniority.LEAD, EmploymentType.FTE, "CORE"),
        new("Hiroshi Tanaka", "hiroshi.tanaka@example.com", "Staff Software Engineer, Distributed Storage", Seniority.STAFF, EmploymentType.FTE, "CORE"),
        new("Mateo Herrera", "mateo.herrera@example.com", "Senior Site Reliability Engineer", Seniority.SENIOR, EmploymentType.FTE, "CORE"),
        new("Elena Petrova", "elena.petrova@example.com", "Site Reliability Engineer", Seniority.MID, EmploymentType.FTE, "CORE"),
        new("Samuel Adeyemi", "samuel.adeyemi@example.com", "Backend Software Engineer", Seniority.MID, EmploymentType.FTE, "CORE"),
        new("Nora Lindqvist", "nora.lindqvist@example.com", "Developer Infrastructure Engineer", Seniority.MID, EmploymentType.FTE, "CORE"),
        new("Victor Chang", "victor.chang@example.com", "Security & Privacy Engineer", Seniority.MID, EmploymentType.FTE, "CORE"),
        new("Dev Patel", "dev.patel@example.com", "IT Helpdesk Technician", Seniority.JUNIOR, EmploymentType.TVC, "CORE"),

        // ---- Knowledge & Information (5) - Search, Maps, News, Assistant.
        new("Helena Voss", "helena.voss@example.com", "Engineering Manager, Search", Seniority.MANAGER, EmploymentType.FTE, "KI"),
        new("Omar Haddad", "omar.haddad@example.com", "Search Backend Engineer", Seniority.MID, EmploymentType.FTE, "KI"),
        new("Yuki Nakamura", "yuki.nakamura@example.com", "Data Scientist", Seniority.MID, EmploymentType.FTE, "KI"),
        new("Raj Malhotra", "raj.malhotra@example.com", "Product Manager, Maps", Seniority.MID, EmploymentType.FTE, "KI"),
        new("Clara Dubois", "clara.dubois@example.com", "UX Researcher", Seniority.MID, EmploymentType.FTE, "KI"),

        // ---- Ads & Commerce (6).
        new("Gabriel Santos", "gabriel.santos@example.com", "Director, Ads & Commerce", Seniority.DIRECTOR, EmploymentType.FTE, "ADS"),
        new("Fatima Al-Mansouri", "fatima.almansouri@example.com", "Tech Lead, Ads Serving", Seniority.LEAD, EmploymentType.FTE, "ADS"),
        new("Felix Brandt", "felix.brandt@example.com", "Backend Software Engineer, Ads Serving", Seniority.MID, EmploymentType.FTE, "ADS"),
        new("Mei-Ling Chen", "meiling.chen@example.com", "Quantitative Analyst", Seniority.MID, EmploymentType.FTE, "ADS"),
        new("Patrick Donnelly", "patrick.donnelly@example.com", "Account Executive", Seniority.MID, EmploymentType.FTE, "ADS"),
        new("Sofia Castellanos", "sofia.castellanos@example.com", "Product Marketing Manager", Seniority.MID, EmploymentType.FTE, "ADS"),

        // ---- Platforms & Devices (5) - Android, Chrome, Pixel, Nest.
        new("Derek Lawson", "derek.lawson@example.com", "Director of Engineering, Devices", Seniority.DIRECTOR, EmploymentType.FTE, "PLATFORMS"),
        new("Haruto Sato", "haruto.sato@example.com", "Android Platform Engineer", Seniority.MID, EmploymentType.FTE, "PLATFORMS"),
        new("Bianca Rossi", "bianca.rossi@example.com", "Silicon Design Engineer", Seniority.MID, EmploymentType.FTE, "PLATFORMS"),
        new("Kwame Asante", "kwame.asante@example.com", "Embedded Systems Engineer", Seniority.MID, EmploymentType.FTE, "PLATFORMS"),
        new("Julia Hoffmann", "julia.hoffmann@example.com", "UX Designer", Seniority.MID, EmploymentType.FTE, "PLATFORMS"),

        // ---- Google DeepMind & Research (6).
        new("Yusuf Demir", "yusuf.demir@example.com", "Director of Research", Seniority.DIRECTOR, EmploymentType.FTE, "DEEPMIND"),
        new("Ingrid Weber", "ingrid.weber@example.com", "Senior Research Scientist", Seniority.SENIOR, EmploymentType.FTE, "DEEPMIND"),
        new("Anika Sharma", "anika.sharma@example.com", "Research Scientist", Seniority.MID, EmploymentType.FTE, "DEEPMIND"),
        new("Pavel Novak", "pavel.novak@example.com", "Machine Learning Engineer", Seniority.MID, EmploymentType.FTE, "DEEPMIND"),
        new("Chiara Bianchi", "chiara.bianchi@example.com", "AI Infrastructure Engineer", Seniority.MID, EmploymentType.FTE, "DEEPMIND"),
        new("Rosa Delgado", "rosa.delgado@example.com", "Data Labeling Specialist", Seniority.JUNIOR, EmploymentType.TVC, "DEEPMIND"),

        // ---- YouTube (6).
        new("Adaeze Okoro", "adaeze.okoro@example.com", "VP of Product, YouTube", Seniority.VP, EmploymentType.FTE, "YOUTUBE"),
        new("Noah Blackwood", "noah.blackwood@example.com", "Senior Software Engineer", Seniority.SENIOR, EmploymentType.FTE, "YOUTUBE"),
        new("Tiana Brooks", "tiana.brooks@example.com", "Video Streaming Backend Engineer", Seniority.MID, EmploymentType.FTE, "YOUTUBE"),
        new("Luis Fernandez", "luis.fernandez@example.com", "Site Reliability Engineer", Seniority.MID, EmploymentType.FTE, "YOUTUBE"),
        new("Hannah Kim", "hannah.kim@example.com", "Product Manager, Creator Monetization", Seniority.MID, EmploymentType.FTE, "YOUTUBE"),
        new("Andre Kowalski", "andre.kowalski@example.com", "Content Moderation Specialist", Seniority.JUNIOR, EmploymentType.TVC, "YOUTUBE"),

        // ---- Global Affairs & Legal (4).
        new("Diane Kowalczyk", "diane.kowalczyk@example.com", "Director of Legal Affairs", Seniority.DIRECTOR, EmploymentType.FTE, "LEGAL"),
        new("Margaret Whitfield", "margaret.whitfield@example.com", "Corporate Counsel", Seniority.MID, EmploymentType.FTE, "LEGAL"),
        new("Ahmed Farouk", "ahmed.farouk@example.com", "Regulatory Compliance Specialist", Seniority.MID, EmploymentType.FTE, "LEGAL"),
        new("Rebecca Stern", "rebecca.stern@example.com", "Public Policy Manager", Seniority.MID, EmploymentType.FTE, "LEGAL"),

        // ---- Finance & Corporate Development (5) - the people who should
        // NOT be pulled into a failed-database incident, however senior.
        new("Gordon Blake", "gordon.blake@example.com", "Chief Financial Officer", Seniority.EXECUTIVE, EmploymentType.FTE, "FINANCE"),
        new("Robert Kinsley", "robert.kinsley@example.com", "Senior Finance Director", Seniority.DIRECTOR, EmploymentType.FTE, "FINANCE"),
        new("Jonathan Pierce", "jonathan.pierce@example.com", "Financial Analyst, FP&A", Seniority.MID, EmploymentType.FTE, "FINANCE"),
        new("Keiko Yamamoto", "keiko.yamamoto@example.com", "Senior Accountant", Seniority.SENIOR, EmploymentType.FTE, "FINANCE"),
        new("Natalie Dupont", "natalie.dupont@example.com", "Corporate Development Associate", Seniority.JUNIOR, EmploymentType.FTE, "FINANCE"),

        // ---- People Operations (4).
        new("Amara Nwachukwu", "amara.nwachukwu@example.com", "Director of People Operations", Seniority.DIRECTOR, EmploymentType.FTE, "PEOPLEOPS"),
        new("Carmen Ortiz", "carmen.ortiz@example.com", "Technical Recruiter", Seniority.MID, EmploymentType.FTE, "PEOPLEOPS"),
        new("Oliver Grant", "oliver.grant@example.com", "People Partner", Seniority.MID, EmploymentType.FTE, "PEOPLEOPS"),
        new("Zanele Dlamini", "zanele.dlamini@example.com", "Learning & Development Specialist", Seniority.MID, EmploymentType.FTE, "PEOPLEOPS"),

        // ---- Global Marketing (3).
        new("Lucia Moretti", "lucia.moretti@example.com", "Senior Marketing Director", Seniority.DIRECTOR, EmploymentType.FTE, "MARKETING"),
        new("Emma Lindgren", "emma.lindgren@example.com", "Brand Strategy Manager", Seniority.MID, EmploymentType.FTE, "MARKETING"),
        new("Hugo Martins", "hugo.martins@example.com", "Consumer Insights Analyst", Seniority.MID, EmploymentType.FTE, "MARKETING"),

        // ---- Global Business Organization (3).
        new("Henrik Dahl", "henrik.dahl@example.com", "Senior Director, Global Sales", Seniority.DIRECTOR, EmploymentType.FTE, "GBO"),
        new("Sandra Yeboah", "sandra.yeboah@example.com", "Customer Support Lead", Seniority.LEAD, EmploymentType.FTE, "GBO"),
        new("Thomas Reinhardt", "thomas.reinhardt@example.com", "Business Development Manager", Seniority.MID, EmploymentType.FTE, "GBO"),

        // ---- Real Estate & Workplace Services (3).
        new("Grace Okafor", "grace.okafor@example.com", "Workplace Services Director", Seniority.DIRECTOR, EmploymentType.FTE, "REWS"),
        new("Ana Moreira", "ana.moreira@example.com", "Workplace Operations Manager", Seniority.MANAGER, EmploymentType.FTE, "REWS"),
        new("Ibrahim Keita", "ibrahim.keita@example.com", "Facilities Maintenance Technician", Seniority.JUNIOR, EmploymentType.TVC, "REWS"),

        // ---- Campus Operations & Hospitality (4) - mostly contractors.
        new("Miguel Ferreira", "miguel.ferreira@example.com", "Campus Services Director", Seniority.DIRECTOR, EmploymentType.FTE, "CAMPUS"),
        new("Giulia Ferraro", "giulia.ferraro@example.com", "Executive Chef", Seniority.LEAD, EmploymentType.TVC, "CAMPUS"),
        new("Paulo Silva", "paulo.silva@example.com", "Shuttle Fleet Dispatcher", Seniority.MID, EmploymentType.TVC, "CAMPUS"),
        new("Nadia Rahman", "nadia.rahman@example.com", "On-Site Physical Therapist", Seniority.MID, EmploymentType.TVC, "CAMPUS"),
    ];
}
