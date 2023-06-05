using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Représente la liste des voisins de chaque noeud d'un étage
/// </summary>
public struct FloorInfoNodeNeighbourBE : IBufferElementData
{
    #region Variables d'instance

    /// <summary>
    /// Les IDs du noeud et de son voisins, respectivement
    /// </summary>
    public int2 IDs;

    #endregion
}
