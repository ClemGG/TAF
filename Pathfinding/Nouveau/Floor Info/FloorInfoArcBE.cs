using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Repr�sente un segment entre deux noeuds d'un chemin
/// </summary>
public struct FloorInfoArcBE : IBufferElementData
{
    #region Variables d'instance

    /// <summary>
    /// Les IDs des points de ce segment
    /// (x: d�but ; y : fin)
    /// </summary>
    public int2 NodesIDs;

    /// <summary>
    /// Co�t d'usage de ce segment
    /// </summary>
    public float LengthSq;

    #endregion
}
