using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Catalog.Resources;

public sealed class AssetsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "assets",
        Description = "Asset (i valori delle credenziali non sono esposti da Orchestrator).",
        Path = "odata/Assets",
        Scope = "OR.Assets.Read",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Enum("ValueScope"), F.Enum("ValueType"), F.Text("Value", false),
            F.Text("StringValue"), F.Bool("BoolValue"), F.Long("IntValue"), F.Text("Description", false),
        ],
    };
}

public sealed class FoldersResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "folders",
        Description = "Cartelle del tenant.",
        Path = "odata/Folders",
        Scope = "OR.Folders.Read",
        NameField = "DisplayName",
        FolderScoped = false,
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("DisplayName"), F.Text("FullyQualifiedName"), F.Text("Description", false),
            F.Enum("FolderType"), F.Enum("ProvisionType"), F.Enum("PermissionModel"), F.Long("ParentId"),
        ],
    };
}

public sealed class PersonalWorkspacesResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "personalworkspaces",
        Description = "Spazi di lavoro personali.",
        Path = "odata/PersonalWorkspaces",
        Scope = "OR.Folders.Read",
        FolderScoped = false,
        Fields = [F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Long("OwnerId"), F.Text("OwnerName"), F.Bool("IsActive")],
    };
}

public sealed class UsersResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "users",
        Description = "Utenti, gruppi e account robot.",
        Path = "odata/Users",
        Scope = "OR.Users.Read",
        NameField = "UserName",
        FolderScoped = false,
        TimeField = "CreationTime",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Text("Surname"), F.Text("UserName"), F.Text("EmailAddress"),
            F.Text("FullName"), F.Bool("IsActive"), F.Enum("Type"), F.Time("CreationTime"), F.Time("LastLoginTime"),
        ],
    };
}

public sealed class RolesResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "roles",
        Description = "Ruoli e permessi.",
        Path = "odata/Roles",
        Scope = "OR.Users.Read",
        GuidField = null,
        FolderScoped = false,
        Fields = [F.Long("Id"), F.Text("Name"), F.Text("DisplayName"), F.Enum("Type"), F.Bool("IsStatic"), F.Bool("IsEditable")],
    };
}

public sealed class SettingsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "settings",
        Description = "Impostazioni del tenant.",
        Path = "odata/Settings",
        Scope = "OR.Settings.Read",
        KeyKind = KeyKind.Textual,
        GuidField = null,
        FolderScoped = false,
        Fields = [F.Text("Id"), F.Text("Name"), F.Text("Value", false), F.Enum("Scope")],
    };
}

public sealed class LicenseResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "license",
        Description = "Licenza del tenant.",
        Path = "odata/Settings/UiPath.Server.Configuration.OData.GetLicense",
        Scope = "OR.License.Read",
        IsEntitySet = false,
        Shape = PayloadShape.Entity,
        SupportsODataQuery = false,
        SupportsPaging = false,
        FolderScoped = false,
    };
}

public sealed class WebhooksResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "webhooks",
        Description = "Webhook configurati.",
        Path = "odata/Webhooks",
        Scope = "OR.Webhooks.Read",
        NameField = "Url",
        FolderScoped = false,
        Fields = [F.Long("Id"), F.Guid("Key"), F.Text("Url"), F.Bool("Enabled"), F.Bool("AllowInsecureSsl"), F.Bool("SubscribeToAllEvents")],
    };
}

public sealed class CalendarsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "calendars",
        Description = "Calendari di esclusione dei trigger.",
        Path = "odata/Calendars",
        Scope = "OR.Settings.Read",
        FolderScoped = false,
        Fields = [F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Text("TimeZoneId")],
    };
}

public sealed class AlertsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "alerts",
        Description = "Avvisi dell'utente corrente.",
        Path = "odata/Alerts",
        Scope = "OR.Monitoring.Read",
        NameField = "NotificationName",
        GuidField = null,
        FolderScoped = false,
        TimeField = "CreationTime",
        Fields =
        [
            F.Long("Id"), F.Text("NotificationName"), F.Enum("Component"), F.Enum("Severity"), F.Time("CreationTime"),
            F.Enum("State"), F.Text("Message", false), F.Text("Data", false),
        ],
    };
}
