using Inno.Scripting.Api;
using Inno.Core.Serialization;
using Inno.Core.Serialization.Converters;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Serialization",
    "Inno.Core.Serialization",
    ScriptingApiScope.Runtime)]
[assembly: ScriptingApiNamespace(
    "InnoEngine.Serialization",
    "Inno.Core.Serialization.Converters",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(ISerializable), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(OnSerializableRestored), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(PropertyVisibility), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(RequiresSerializationConverterAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GenerateSerializationConverterAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SerializablePropertyAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SerializedProperty), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SerializationContext), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SerializationConverter<>), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SerializationExtensionAttribute), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SerializationReader), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(SerializationWriter), ScriptingApiScope.Runtime)]
