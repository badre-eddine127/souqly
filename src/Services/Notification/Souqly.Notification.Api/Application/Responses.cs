namespace Souqly.Notification.Api.Application;

/// <summary>Vue d'une notification.</summary>
public sealed record NotificationResponse(Guid Id, string Kind, Guid OrderId, string Title, string Body, DateTimeOffset CreatedAt, bool Read);
