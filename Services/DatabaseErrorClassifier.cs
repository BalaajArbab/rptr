using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace rptr.Services;

internal static class DatabaseErrorClassifier
{
    public static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var baseException = exception.GetBaseException();
        return baseException is SqlException { Number: 2601 or 2627 }
            || baseException is SqliteException
            {
                SqliteErrorCode: 19,
                SqliteExtendedErrorCode: 1555 or 2067
            };
    }
}
