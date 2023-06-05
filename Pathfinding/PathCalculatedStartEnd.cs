using System;
using Unity.Mathematics;

/// <summary>
/// Représente un chemin calculé par le PathFinder
/// </summary>
public struct PathCalculatedStartEnd : IEquatable<PathCalculatedStartEnd>
{
    #region Variables d'instance

    /// <summary>
    /// Le point de départ
    /// </summary>
    public float3 Start;

    /// <summary>
    /// Le point de départ
    /// </summary>
    public float3 End;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Compare deux chemins
    /// </summary>
    /// <param name="obj">Le chemin à comparer</param>
    /// <returns>TRUE si les deux chemins sont identiques</returns>
    public override bool Equals(object obj)
    {
        return obj is PathCalculatedStartEnd pathStartEnd && this.Equals(pathStartEnd);
    }

    /// <summary>
    /// Compare deux chemins
    /// </summary>
    /// <param name="obj">Le chemin à comparer</param>
    /// <returns>TRUE si les deux chemins sont identiques</returns>
    public bool Equals(PathCalculatedStartEnd other)
    {
        return this.Start.Equals(other.Start) && this.End.Equals(other.End);
    }

    /// <summary>
    /// Nécessaire pour faire fonctionner le Equals()
    /// </summary>
    public override int GetHashCode()
    {
        return this.Start.GetHashCode() +
               this.End.GetHashCode();
    }

    #endregion
}
