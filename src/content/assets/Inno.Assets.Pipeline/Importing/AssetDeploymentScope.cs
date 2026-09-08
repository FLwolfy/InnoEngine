namespace Inno.Assets.Pipeline;

/// <summary>
/// Defines whether an imported asset participates in deployed runtime content.
/// </summary>
public enum AssetDeploymentScope
{
    /// <summary>
    /// The asset is deployed and must produce a named <c>runtime</c> artifact output.
    /// </summary>
    Runtime,

    /// <summary>
    /// The asset exists only for authoring workflows and is omitted from deployed catalogs.
    /// </summary>
    AuthoringOnly
}
