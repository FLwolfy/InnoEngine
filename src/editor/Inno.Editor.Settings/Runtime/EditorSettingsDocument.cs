using System;
using System.Collections.Generic;

using Inno.Core.Serialization;

namespace Inno.Editor.Settings;

[GenerateSerializationConverter]
internal sealed class EditorSettingsDocument : ISerializable
{
    [SerializableProperty]
    internal Dictionary<string, EditorSettingObject> values = new(StringComparer.Ordinal);
}
