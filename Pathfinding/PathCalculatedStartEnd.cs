using System;
using Unity.Mathematics;

/// <summary>
/// Repr�sente un chemin calcul� par le PathFinder
/// </summary>
public struct PathCalculatedStartEnd : IEquatable<PathCalculatedStartEnd>
{
    #region Variables d'instance

    /// <summary>
    /// Le point de d�part
    /// </summary>
    public float3 Start;

    /// <summary>
    /// Le point de d�part
    /// </summary>
    public float3 End;

    #endregion

    #region Fonctions publiques

    /// <summary>
    /// Compare deux chemins
    /// </summary>
    /// <param name="obj">Le chemin � comparer</param>
    /// <returns>TRUE si les deux chemins sont identiques</returns>
    public override bool Equals(object obj)
    {
        return obj is PathCalculatedStartEnd pathStartEnd && this.Equals(pathStartEnd);
    }

    /// <summary>
    /// Compare deux chemins
    /// </summary>
    /// <param name="obj">Le chemin � comparer</param>
    /// <returns>TRUE si les deux chemins sont identiques</returns>
    public bool Equals(PathCalculatedStartEnd other)
    {
        return this.Start.Equals(other.Start) && this.End.Equals(other.End);
    }

    /// <summary>
    /// N�cessaire pour faire fonctionner le Equals()
    /// </summary>
    public override int GetHashCode()
    {
        return this.Start.GetHashCode() +
               this.End.GetHashCode();
    }

    #endregion
}
