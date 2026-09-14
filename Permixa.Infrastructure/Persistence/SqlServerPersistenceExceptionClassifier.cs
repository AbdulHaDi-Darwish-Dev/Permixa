using Permixa.Application.Common.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence;

public sealed class SqlServerPersistenceExceptionClassifier : IPersistenceExceptionClassifier
{
    public bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException dbUpdate)
            {
                if (IsSqlUnique(dbUpdate.InnerException))
                    return true;
            }

            if (IsSqlUnique(current))
                return true;
        }

        return false;
    }

    private static bool IsSqlUnique(Exception? exception) =>
        exception is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
