using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Représente la liste de noeuds à chaque étage
/// </summary>
public struct FloorInfoNodeBE : IBufferElementData, IEquatable<FloorInfoNodeBE>
{
    #region Variables d'instance

    /// <summary>
    /// L'id du PathPointDTO correspondant à ce noeud
    /// </summary>
    public int DTOID;

    /// <summary>
    /// La positio de ce point dans la scène
    /// </summary>
    public float3 Position;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Compare deux noeuds
    /// </summary>
    /// <param name="obj">Le noeud à comparer</param>
    /// <returns>TRUE si les deux noeuds sont identiques</returns>
    public override bool Equals(object obj)
    {
        return obj is FloorInfoNodeBE node && this.Equals(node);
    }

    /// <summary>
    /// Compare deux noeuds
    /// </summary>
    /// <param name="obj">Le noeud à comparer</param>
    /// <returns>TRUE si les deux noeuds sont identiques</returns>
    public bool Equals(FloorInfoNodeBE obj)
    {
        return this.DTOID == obj.DTOID &&
               this.Position.Equals(obj.Position);
    }

    /// <summary>
    /// Nécessaire pour faire fonctionner le Equals()
    /// </summary>
    public override int GetHashCode()
    {
        return this.DTOID.GetHashCode() +
               this.Position.GetHashCode();
    }

    #endregion
}
