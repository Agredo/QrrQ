namespace QrrQ;

/// <summary>
/// Ergebnis von <see cref="QrrQGenerator.TryCreate"/>: entweder ein fertiger
/// <see cref="QrrQCode"/> oder ein <see cref="QrrQError"/> (Union-Typ, C# 15).
/// </summary>
/// <example>
/// <code>
/// var text = QrrQGenerator.TryCreate("A", "B") switch
/// {
///     QrrQCode code => $"Version {code.Version}",
///     QrrQError error => error.Message,
/// };
/// </code>
/// </example>
public union QrrQResult(QrrQCode, QrrQError);
