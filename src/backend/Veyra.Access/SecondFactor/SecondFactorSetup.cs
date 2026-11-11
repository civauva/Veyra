namespace Veyra.Access.SecondFactor;

/// <summary>What a person needs to add Veyra to their authenticator app.</summary>
/// <param name="Secret">The secret in Base32, for typing it in by hand.</param>
/// <param name="Uri">The <c>otpauth://</c> address the QR code shows.</param>
internal sealed record SecondFactorSetup(string Secret, string Uri);
