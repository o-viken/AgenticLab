namespace TheSeries.AiService.Application.Agents;

/// <summary>
/// Supplies the vendor-flavoured <em>harness</em> system prompt for a single brand/vendor. This is the
/// seam between the reusable <see cref="VendorHarnessCatalog"/> infrastructure (in the Application layer)
/// and the representative prompt <em>content</em> (in the Demo layer): each vendor ships one
/// implementation under <c>Demo/Vendors/&lt;Vendor&gt;</c>, and they are injected into the catalog at
/// start-up. Keeping the prompts behind this interface lets the Application layer resolve a vendor's
/// harness without depending on the Demo layer.
/// </summary>
public interface IVendorHarness
{
    /// <summary>
    /// The vendor/brand key this harness applies to, matched case-insensitively against a request's
    /// <c>Vendor</c> value (e.g. <c>copilot</c>, <c>claude</c>, <c>claude-code</c>).
    /// </summary>
    string Key { get; }

    /// <summary>
    /// The harness system prompt that replaces the shared <see cref="AgentDefinitionBase.Harness"/> for a
    /// run made under this vendor, while the agent's own persona is kept.
    /// </summary>
    string Harness { get; }

    /// <summary>The friendly product/brand name shown for this vendor (e.g. <c>GitHub Copilot</c>).</summary>
    string DisplayName { get; }

    /// <summary>
    /// The simulated provider/model label shown for this vendor (e.g. <c>Claude Sonnet 4.5 (Anthropic)</c>),
    /// used to mimic that the product runs on its own model. Presentation only — the real backend is always
    /// Azure OpenAI.
    /// </summary>
    string ModelLabel { get; }

    /// <summary>
    /// The "modes" this vendor offers, mirroring the product's mode picker: each pairs a backend agent name
    /// with the product-flavoured label shown for it. Order is the display order.
    /// </summary>
    IReadOnlyList<VendorMode> Modes { get; }
}

/// <summary>
/// A vendor "mode": a backend agent the vendor surfaces, paired with the product-flavoured label shown for
/// it in the mode picker (e.g. the <c>Coder</c> agent shown as <c>agent</c>).
/// </summary>
/// <param name="Agent">The backend agent name this mode maps to.</param>
/// <param name="Label">The product-flavoured label shown for the mode.</param>
public sealed record VendorMode(string Agent, string Label);
