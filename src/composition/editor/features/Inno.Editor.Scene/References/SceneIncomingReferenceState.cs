using System;

namespace Inno.Editor.Scene;

internal sealed record SceneIncomingReferenceState(Guid ownerId, string propertyName, byte[] data);
