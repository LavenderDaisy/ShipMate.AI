namespace ShipMate.AI.Console.Knowledge;

/// <summary>
/// Seed carrier-rules knowledge base. In a production system these would be ingested from
/// carrier policy PDFs/pages; here they are curated snippets covering the questions the
/// RAG assistant is expected to answer (hazardous materials, prohibited items, international
/// eligibility, dimensional weight, etc.). Keywords are kept explicit so retrieval works
/// well even with the lightweight local embedding.
/// </summary>
public static class CarrierKnowledgeBase
{
    public static IReadOnlyList<KnowledgeDocument> Documents { get; } = new List<KnowledgeDocument>
    {
        new()
        {
            Id = "hazmat-lithium",
            Title = "Lithium batteries and air transport",
            Content = "Lithium batteries are classified as hazardous materials (hazmat). " +
                      "Standalone lithium-ion batteries generally cannot ship by air (overnight/express) " +
                      "without special dangerous-goods handling and certification. Batteries installed in " +
                      "equipment may ship with restrictions. Ground service is usually required for loose " +
                      "lithium batteries. Watt-hour limits and UN3480/UN3481 labeling apply."
        },
        new()
        {
            Id = "prohibited-items",
            Title = "Prohibited and restricted items",
            Content = "Commonly prohibited items include explosives, flammable liquids and gases, " +
                      "firearms and ammunition, illegal drugs, perishable food without proper packaging, " +
                      "live animals, and hazardous chemicals. Alcohol and tobacco are restricted and require " +
                      "licensed shippers. Cash and negotiable items are not insurable and discouraged."
        },
        new()
        {
            Id = "international-china",
            Title = "Shipping to China — eligibility and restrictions",
            Content = "International shipments to China are supported by major carriers (UPS, FedEx, USPS). " +
                      "A commercial invoice and accurate customs declaration are required. Restricted imports " +
                      "include certain electronics, used goods, and items subject to Chinese customs duties. " +
                      "Delivery times are longer and customs clearance can add several days. Batteries and " +
                      "liquids face additional international air restrictions."
        },
        new()
        {
            Id = "dimensional-weight",
            Title = "Dimensional (volumetric) weight",
            Content = "Carriers bill the greater of actual weight and dimensional weight. Dimensional weight " +
                      "is length x width x height divided by a dim factor (commonly 139 for domestic US inches). " +
                      "Large but light packages are charged by dimensional weight. Reducing box size lowers cost."
        },
        new()
        {
            Id = "residential-surcharge",
            Title = "Residential delivery surcharge",
            Content = "Deliveries to residential addresses incur a surcharge compared to commercial addresses. " +
                      "Address classification is determined by the carrier. Residential surcharges apply on top " +
                      "of base rates and vary by service level."
        },
        new()
        {
            Id = "international-general",
            Title = "International shipping basics",
            Content = "International shipments require a commercial invoice, HS tariff codes, and a customs " +
                      "declaration of contents and value. Duties and taxes may be billed to sender or recipient " +
                      "(DDP vs DDU). Prohibited and restricted items vary by destination country. Lithium " +
                      "batteries, liquids, and aerosols have strict international air-transport limits."
        }
    };
}
