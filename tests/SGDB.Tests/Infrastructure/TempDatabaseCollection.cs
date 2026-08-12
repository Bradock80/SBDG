namespace SGDB.Tests.Infrastructure;

/// <summary>
/// DatabaseService guarda connection string estática — testes de banco
/// não podem rodar em paralelo uns com os outros.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TempDatabaseCollection
{
    public const string Name = "TempDatabase";
}
