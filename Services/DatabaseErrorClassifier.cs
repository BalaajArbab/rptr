using Microsoft.Data.SqlClient;

namespace rptr.Services;

internal static class DatabaseErrorClassifier
{
    public static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqlException { Number: 2601 or 2627 };
}
