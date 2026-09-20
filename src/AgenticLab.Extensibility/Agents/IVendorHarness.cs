namespace AgenticLab.Extensibility.Agents;

/// <summary>Selectable host instructions and presentation metadata, independent of model credentials.</summary>
public interface IVendorHarness
{
    /// <summary>The stable host key sent in the chat API's Vendor field.</summary>
    string Key { get; }
    /// <summary>Replacement host instructions; empty preserves the agent's own harness.</summary>
    string Harness { get; }
    /// <summary>The host's user-facing name.</summary>
    string DisplayName { get; }
    /// <summary>Presentation label only; the model client is selected by the application.</summary>
    string ModelLabel { get; }
    /// <summary>Ordered agent names and their labels in the host picker.</summary>
    IReadOnlyList<VendorMode> Modes { get; }
}

/// <summary>A backend agent name paired with the host's user-facing mode label.</summary>
public sealed record VendorMode(string Agent, string Label);