namespace PostmanClone.ExecutionService.Sending.Ssrf;

/// <summary>The outcome of checking one IP address. <see cref="Category"/> is set only when the address is refused.</summary>
public readonly record struct AddressVerdict(bool Allowed, string? Category);
