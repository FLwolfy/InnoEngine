using System;

namespace Inno.Editor.Inspection;

/// <summary>
/// Creates one discovered inspection drawer through the owning editor composition root.
/// </summary>
/// <param name="drawerType">
/// The concrete drawer type selected from the active type catalog.
/// </param>
/// <returns>
/// A new drawer instance whose runtime type is <paramref name="drawerType"/>.
/// </returns>
/// <exception cref="ArgumentNullException">
/// Implementations may throw when <paramref name="drawerType"/> is <see langword="null"/>.
/// </exception>
public delegate IInspectionDrawer InspectionDrawerFactory(Type drawerType);
