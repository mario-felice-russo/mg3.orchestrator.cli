using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Catalog;

/// <summary>Scorciatoie per dichiarare i campi delle risorse.</summary>
internal static class F
{
    public static FieldDescriptor Text(string name, bool filterable = true) => new(name, FieldType.Text, filterable);

    public static FieldDescriptor Long(string name, bool filterable = true) => new(name, FieldType.WholeNumber, filterable);

    public static FieldDescriptor Num(string name, bool filterable = true) => new(name, FieldType.Number, filterable);

    public static FieldDescriptor Bool(string name, bool filterable = true) => new(name, FieldType.Boolean, filterable);

    public static FieldDescriptor Time(string name, bool filterable = true) => new(name, FieldType.Timestamp, filterable);

    public static FieldDescriptor Guid(string name, bool filterable = true) => new(name, FieldType.Identifier, filterable);

    public static FieldDescriptor Enum(string name, bool filterable = true) => new(name, FieldType.Enumeration, filterable);

    public static FieldDescriptor Obj(string name) => new(name, FieldType.Complex, false);
}
