using Unity.Mathematics;

/// <summary>
/// Repr�sente un segment entre deux noeuds d'un chemin
/// </summary>
public struct PathArc
{
    #region Variables d'instance

    /// <summary>
    /// Les IDs des points de ce segment
    /// (x: d�but ; y : fin)
    /// </summary>
    public int2 PointsIDs;

    /// <summary>
    /// Co�t d'usage de ce segment
    /// </summary>
    public float Cost;

    #endregion
}
