using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Repr�sente les deux extr�mit�s d'un chemin calcul�
/// par le PathFinderSystem
/// </summary>
public struct PathEntityStartEndCD : IComponentData
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
        return obj is PathEntityStartEndCD pathStartEnd && this.Equals(pathStartEnd);
    }

    /// <summary>
    /// Compare deux chemins
    /// </summary>
    /// <param name="other">Le chemin � comparer</param>
    /// <returns>TRUE si les deux chemins sont identiques</returns>
    public bool Equals(PathEntityStartEndCD other)
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
