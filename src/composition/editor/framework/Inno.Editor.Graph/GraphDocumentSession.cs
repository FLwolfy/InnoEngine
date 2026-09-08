using Inno.Core.Graphs;
using Inno.Core.Identity;

namespace Inno.Editor.Graph;

internal sealed class GraphDocumentSession(GraphDocument document) : IdentityObject
{
    internal GraphDocument document { get; set; } = document;
    internal ulong revision { get; set; }
    internal bool isDirty { get; set; }
    internal bool available { get; set; } = true;
}
