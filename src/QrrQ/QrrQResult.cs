namespace QrrQ;

/// <summary>
/// Result of <see cref="QrrQGenerator.TryCreate"/>: either a finished
/// <see cref="QrrQCode"/> or a <see cref="QrrQError"/> (union type, C# 15).
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
