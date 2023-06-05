using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Représente un segment entre deux noeuds d'un chemin
/// </summary>
public struct FloorInfoArcBE : IBufferElementData
{
    #region Variables d'instance

    /// <summary>
    /// Les IDs des points de ce segment
    /// (x: début ; y : fin)
    /// </summary>
    public int2 NodesIDs;

    /// <summary>
    /// Coût d'usage de ce segment
    /// </summary>
    public float LengthSq;

    #endregion
}
