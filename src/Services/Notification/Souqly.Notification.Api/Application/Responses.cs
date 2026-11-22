namespace Souqly.Notification.Api.Application;

/// <summary>Vue d'une notification.</summary>
public sealed record NotificationResponse(Guid Id, string Kind, Guid? OrderId, long? ProductId, string Title, string Body, DateTimeOffset CreatedAt, bool Read);

/// <summary>Nombre de notifications passées de « non lue » à « lue ».</summary>
public sealed record ReadAllResponse(int Updated);
