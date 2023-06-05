using System;
using Unity.Mathematics;

/// <summary>
/// Représente un point d'un chemin
/// </summary>
public struct PathNode : IEquatable<PathNode>
{
    #region Variables d'instance

    /// <summary>
    /// L'id du noeud
    /// </summary>
    public int ID;

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
        return obj is PathNode node && this.Equals(node);
    }

    /// <summary>
    /// Compare deux noeuds
    /// </summary>
    /// <param name="obj">Le noeud à comparer</param>
    /// <returns>TRUE si les deux noeuds sont identiques</returns>
    public bool Equals(PathNode other)
    {
        return this.ID == other.ID &&
               this.DTOID == other.DTOID &&
               this.Position.Equals(other.Position);
    }

    /// <summary>
    /// Nécessaire pour faire fonctionner le Equals()
    /// </summary>
    public override int GetHashCode()
    {
        return this.ID.GetHashCode() +
               this.DTOID.GetHashCode() +
               this.Position.GetHashCode();
    }

    #endregion
}