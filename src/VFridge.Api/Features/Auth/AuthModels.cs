using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Features.Auth;

public sealed record VerifyEmailRequest(string Token);
