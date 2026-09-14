namespace Permixa.Application.Common.Abstractions;

/// <summary>
/// Classifies persistence exceptions without exposing database provider types to use cases.
/// </summary>
public interface IPersistenceExceptionClassifier
{
    bool IsUniqueConstraintViolation(Exception exception);
}
