namespace Souqly.Payment.Api.Application;

/// <summary>Vue d'un paiement.</summary>
public sealed record PaymentResponse(Guid Id, Guid OrderId, string Status, string Amount, string Currency, string? FailureReason, DateTimeOffset UpdatedAt);
