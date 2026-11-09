namespace Veyra.Ingestion.Otlp;

/// <summary>
/// An OTLP request body could not be read. The endpoints answer it with <c>400 Bad Request</c>, which tells the
/// sender not to retry.
/// </summary>
/// <param name="message">What is wrong with the body.</param>
internal sealed class OtlpFormatException(string message) : Exception(message);
