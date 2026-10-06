namespace DotNetForge.Abstractions.Database;

/// <summary>
/// Base of every database error, whatever the provider. Providers translate driver exceptions into one of the
/// subclasses and keep the driver exception as <see cref="Exception.InnerException"/> for diagnostics
/// (.docs/database/architecture.md#errors). Messages never contain connection strings or parameter values.
/// </summary>
public abstract class DatabaseException : Exception
{
    protected DatabaseException(string message, Exception? innerException = null) : base(message, innerException) { }

    /// <summary>Provider display name, when known.</summary>
    public string? Provider { get; private set; }

    /// <summary>Configured database name, when known.</summary>
    public string? Database { get; private set; }

    public DatabaseOperation? Operation { get; private set; }

    public string? Collection { get; private set; }

    /// <summary>Records where the error happened (only fills what is not set yet) and returns this exception.</summary>
    public DatabaseException WithContext(string? provider, string? database, DatabaseOperation? operation, string? collection)
    {
        Provider ??= provider;
        Database ??= database;
        Operation ??= operation;
        Collection ??= collection;
        return this;
    }
}

/// <summary>The server can't be reached, or the connection broke.</summary>
public sealed class DatabaseConnectionException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);

/// <summary>The server rejected the credentials.</summary>
public sealed class DatabaseAuthenticationException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);

/// <summary>The command exceeded its timeout (not raised for caller cancellation, which stays an <see cref="OperationCanceledException"/>).</summary>
public sealed class DatabaseTimeoutException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);

/// <summary>The command was invalid for this database, or the database rejected it.</summary>
public sealed class DatabaseQueryException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);

/// <summary>The database, table/collection or field does not exist.</summary>
public sealed class DatabaseNotFoundException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);

/// <summary>A write violated a unique constraint or key.</summary>
public sealed class DatabaseConflictException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);

/// <summary>No provider is registered under that name, or the provider can't perform the operation.</summary>
public sealed class DatabaseProviderException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);

/// <summary>A database is configured incorrectly; aborts startup with this message.</summary>
public sealed class DatabaseConfigurationException(string message, Exception? innerException = null)
    : DatabaseException(message, innerException);
