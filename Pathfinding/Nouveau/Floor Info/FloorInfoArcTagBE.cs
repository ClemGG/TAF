using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Représente la liste des tags de chaque arc d'un étage
/// </summary>
public struct FloorInfoArcTagBE : IBufferElementData
{
    #region Variables d'instance

    /// <summary>
    /// Les ids de l'arc lié à ce tag
    /// </summary>
    public int2 NodesIDs;

    /// <summary>
    /// Le tag d'un arc
    /// </summary>
    public FixedString64Bytes Tag;

    #endregion
}
