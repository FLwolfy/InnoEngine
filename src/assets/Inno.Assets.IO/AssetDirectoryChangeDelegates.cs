using System.Collections.Generic;

namespace Inno.Assets.IO;

public delegate void AssetDirectoryChangedHandler(in AssetDirectoryChange change);

public delegate void AssetDirectoryChangesFlushedHandler(IReadOnlyList<AssetDirectoryChange> changes);