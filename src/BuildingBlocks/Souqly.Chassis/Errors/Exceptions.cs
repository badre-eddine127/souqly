namespace Souqly.Chassis.Errors;

/// <summary>Ressource absente, ou invisible pour l'appelant (on renvoie 404 plutôt que 403 pour ne pas révéler son existence).</summary>
public class NotFoundException(string resource, object id)
    : Exception($"{resource} {id} introuvable");

/// <summary>Règle métier non respectée : 422.</summary>
public class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Conflit d'état (transition interdite, version périmée) : 409.</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>Action interdite pour un utilisateur authentifié : 403.</summary>
public class ForbiddenException(string message) : Exception(message);
