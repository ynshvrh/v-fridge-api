namespace VFridge.Api.Contracts;

/// <summary>
/// Standard error envelope returned by every non-validation 4xx / 5xx response.
/// <c>Code</c> is a stable machine-readable identifier (e.g. <c>EMAIL_NOT_VERIFIED</c>) that
/// clients can branch on; <c>Error</c> is the English human-readable message.
/// </summary>
public sealed record ApiError(string Code, string Error);
