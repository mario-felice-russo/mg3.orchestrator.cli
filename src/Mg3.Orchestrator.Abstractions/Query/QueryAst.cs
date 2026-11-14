namespace Mg3.Orchestrator.Abstractions.Query;

/// <summary>Albero sintattico di una query DSL.</summary>
public sealed record QueryAst(SourceNode? Source, IReadOnlyList<StageNode> Stages);

/// <param name="Name">Nome della risorsa o id del comando referenziato (senza <c>$</c>).</param>
public sealed record SourceNode(string Name, bool IsReference, string? Alias, int Position)
{
    public string SourceKey => IsReference ? "$" + Name : Name;
}

public abstract record StageNode(int Position);

public sealed record WhereStage(Expr Condition, int Position) : StageNode(Position);

public sealed record Projection(Expr Expression, string? Alias);

public sealed record SelectStage(IReadOnlyList<Projection> Projections, int Position) : StageNode(Position);

public sealed record OrderKey(Expr Expression, bool Descending);

public sealed record OrderByStage(IReadOnlyList<OrderKey> Keys, int Position) : StageNode(Position);

public sealed record TakeStage(int Count, int Position) : StageNode(Position);

public sealed record SkipStage(int Count, int Position) : StageNode(Position);

public sealed record DistinctStage(IReadOnlyList<FieldExpr> Fields, int Position) : StageNode(Position);

public sealed record GroupByStage(IReadOnlyList<FieldExpr> Fields, int Position) : StageNode(Position);

public sealed record AggregateDefinition(string Name, string Function, Expr? Argument);

public sealed record AggregateStage(IReadOnlyList<AggregateDefinition> Aggregates, int Position) : StageNode(Position);

public sealed record JoinStage(bool Left, SourceNode Right, string Alias, FieldExpr LeftKey, FieldExpr RightKey, int Position) : StageNode(Position);

public sealed record FlattenStage(FieldExpr Field, int Position) : StageNode(Position);

public sealed record CountStage(int Position) : StageNode(Position);

public abstract record Expr;

/// <summary>Letterale: null, bool, decimal, string, <see cref="TimeSpan"/> o lista.</summary>
public sealed record LiteralExpr(object? Value) : Expr;

public sealed record FieldExpr(IReadOnlyList<string> Path) : Expr
{
    public string DottedName => string.Join('.', Path);
}

public enum BinaryOperator
{
    Or,
    And,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    In,
    Contains,
    StartsWith,
    EndsWith,
    Matches,
    Add,
    Subtract,
    Multiply,
    Divide,
}

public enum UnaryOperator
{
    Not,
    Negate,
}

public sealed record BinaryExpr(BinaryOperator Operator, Expr Left, Expr Right) : Expr;

public sealed record UnaryExpr(UnaryOperator Operator, Expr Operand) : Expr;

public sealed record IsNullExpr(Expr Operand, bool Negated) : Expr;

public sealed record CallExpr(string Function, IReadOnlyList<Expr> Arguments) : Expr;

public sealed record ListExpr(IReadOnlyList<Expr> Items) : Expr;
