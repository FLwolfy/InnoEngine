namespace Inno.Core.Identity;

/// <summary>
/// Contract for objects that can be registered into an <see cref="IdentityRegistry"/>.
/// </summary>
public interface IIdentityObject
{
    Identity identity { get; set; }
}
