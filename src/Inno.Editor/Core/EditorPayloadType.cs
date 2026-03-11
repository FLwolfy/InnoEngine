namespace Inno.Editor.Core;

public sealed class EditorPayloadType
{
    private const string PAYLOAD_PREFIX = "__PAYLOAD:";
    
    public const string ASSET_REF_PAYLOAD = PAYLOAD_PREFIX + "ASSET_REF";
    public const string GAMEOBJECT_PAYLOAD = PAYLOAD_PREFIX + "GAMEOBJECT";
    public const string PATH_PAYLOAD = PAYLOAD_PREFIX + "PATH";
}